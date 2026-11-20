using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class GrantAndEditTests(AppFixture app)
{
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private Task<int> UserId(string oid) =>
        app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync());

    private static string Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Berlin")).ToString("yyyy-MM-dd");

    private static async Task<AccessGrantDto> ActiveGrant(HttpClient admin, int ownerId, int viewerId, string permission)
    {
        var grant = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId, viewerId, permission, validFrom = Today, purpose = "Vertretung im Test",
        }));
        return await Read<AccessGrantDto>(await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "Personalakte" }));
    }

    [Fact]
    public async Task Edit_grant_changes_content_but_not_ownership_and_search_is_logged()
    {
        var admin = await app.LoginAsync("dev-anna");
        var owner = await app.LoginAsync("dev-jonas");
        var viewer = await app.LoginAsync("dev-thomas");
        var jonasId = await UserId("dev-jonas");
        var thomasId = await UserId("dev-thomas");
        var title = "Heizungsraum Haus 2 aufräumen " + Guid.NewGuid().ToString("N")[..6];
        var task = await Read<TaskDto>(await owner.PostAsJsonAsync("/api/tasks", new { title }));
        var grant = await ActiveGrant(admin, jonasId, thomasId, "bearbeiten");

        // Suche über die Freigabe wird protokolliert
        var found = await Read<List<TaskDto>>(await viewer.GetAsync($"/api/tasks?view=suche&q={Uri.EscapeDataString(title)}"));
        Assert.Contains(found, t => t.Id == task.Id);
        Assert.True(await app.WithDbAsync(db => db.AccessLog.AnyAsync(l => l.GrantId == grant.Id && l.ViewerId == thomasId && l.Action == "angesehen")));

        // Detail: bearbeitbar, aber über die Freigabe
        var detail = await Read<TaskDetailDto>(await viewer.GetAsync($"/api/tasks/{task.Id}"));
        Assert.True(detail.CanEdit);
        Assert.True(detail.ViaGrant);

        // Inhalt ja, Zuständigkeit und Projekt nein
        Assert.Equal(HttpStatusCode.OK, (await viewer.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { notes = "Schlüssel liegt beim Hausmeister" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { assigneeId = thomasId })).StatusCode);
        var projectId = await app.WithDbAsync(db => db.Projects.Select(p => p.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { projectId })).StatusCode);

        // Die eigene Sicht bleibt unverändert voll bearbeitbar
        var own = await Read<TaskDetailDto>(await owner.GetAsync($"/api/tasks/{task.Id}"));
        Assert.True(own.CanEdit);
        Assert.False(own.ViaGrant);
    }

    [Fact]
    public async Task Read_grant_detail_is_read_only()
    {
        // Eigene Testpersonen, damit die Freigabe keine anderen Tests beeinflusst.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await app.WithDbAsync(async db =>
        {
            db.Users.Add(new Aufgaben.Api.Data.User { EntraOid = "ro-owner-" + suffix, DisplayName = "Lea Lesbar", CreatedAt = DateTimeOffset.UtcNow });
            db.Users.Add(new Aufgaben.Api.Data.User { EntraOid = "ro-viewer-" + suffix, DisplayName = "Ole Vertretung", CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        var admin = await app.LoginAsync("dev-anna");
        var owner = await app.LoginAsync("ro-owner-" + suffix);
        var viewer = await app.LoginAsync("ro-viewer-" + suffix);
        var task = await Read<TaskDto>(await owner.PostAsJsonAsync("/api/tasks", new { title = "Nur lesbar für die Vertretung" }));
        await ActiveGrant(admin, await UserId("ro-owner-" + suffix), await UserId("ro-viewer-" + suffix), "lesen");

        var detail = await Read<TaskDetailDto>(await viewer.GetAsync($"/api/tasks/{task.Id}"));
        Assert.False(detail.CanEdit);
        Assert.True(detail.ViaGrant);
    }

    [Fact]
    public async Task Expired_draft_cannot_be_confirmed_and_creation_is_logged()
    {
        var admin = await app.LoginAsync("dev-anna");
        var grant = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = await UserId("dev-maria"), viewerId = await UserId("dev-thomas"), permission = "lesen",
            validFrom = "2020-01-01", validUntil = "2020-01-31", purpose = "Alte Vertretung",
        }));
        Assert.True(await app.WithDbAsync(db => db.AccessLog.AnyAsync(l => l.GrantId == grant.Id && l.Action == "angelegt")));
        var res = await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "Personalakte" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_value_type_is_a_bad_request()
    {
        var anna = await app.LoginAsync("dev-anna");
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Falscher Datentyp" }));
        var res = await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { assigneeId = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("assigneeId", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recurring_task_creates_only_one_follow_up_even_when_reopened()
    {
        var anna = await app.LoginAsync("dev-anna");
        var title = "Zählerstände ablesen " + Guid.NewGuid().ToString("N")[..6];
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title, dueDate = Today, recurrence = "woechentlich" }));

        await Read<TaskDto>(await anna.PostAsync($"/api/tasks/{task.Id}/complete", null));
        await Read<TaskDto>(await anna.PostAsync($"/api/tasks/{task.Id}/reopen", null));
        await Read<TaskDto>(await anna.PostAsync($"/api/tasks/{task.Id}/complete", null));

        var count = await app.WithDbAsync(db => db.Tasks.CountAsync(t => t.Title == title));
        Assert.Equal(2, count);
    }
}
