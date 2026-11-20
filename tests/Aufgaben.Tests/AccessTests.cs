using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class AccessTests(AppFixture app)
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

    [Fact]
    public async Task Read_grant_works_only_after_confirmation_and_ends_with_revocation()
    {
        var admin = await app.LoginAsync("dev-anna");
        var owner = await app.LoginAsync("dev-thomas");
        var viewer = await app.LoginAsync("dev-maria");
        var thomasId = await UserId("dev-thomas");
        var mariaId = await UserId("dev-maria");

        var privateTask = await Read<TaskDto>(await owner.PostAsJsonAsync("/api/tasks", new { title = "Privat: Arzttermin verschieben" }));

        // Ohne Freigabe: nichts zu sehen
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks?view=person&id={thomasId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks/{privateTask.Id}")).StatusCode);

        // Nur die Verwaltung darf Freigaben anlegen
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/access")).StatusCode);
        var grant = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = thomasId, viewerId = mariaId, permission = "lesen", validFrom = Today, purpose = "Urlaubsvertretung",
        }));
        Assert.Equal("entwurf", grant.Status);

        // Entwurf gibt noch keinen Zugriff
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks?view=person&id={thomasId}")).StatusCode);

        // Bestätigung braucht den Ablageort des unterschriebenen Originals
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "" })).StatusCode);
        var active = await Read<AccessGrantDto>(await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "Personalakte" }));
        Assert.True(active.IsCurrentlyActive);
        var notified = await app.WithDbAsync(db => db.Notifications.CountAsync(n => n.Kind == NotificationOutbox.Access && (n.UserId == thomasId || n.UserId == mariaId)));
        Assert.True(notified >= 2);

        // Jetzt sieht Maria Thomas' Aufgaben – aber nur lesend
        var mine = await Read<List<MyAccessDto>>(await viewer.GetAsync("/api/access/mine"));
        Assert.Contains(mine, m => m.Owner.Id == thomasId);
        var tasks = await Read<List<TaskDto>>(await viewer.GetAsync($"/api/tasks?view=person&id={thomasId}"));
        Assert.Contains(tasks, t => t.Id == privateTask.Id);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/tasks/{privateTask.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync($"/api/tasks/{privateTask.Id}", new { title = "geändert" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/tasks/{privateTask.Id}/complete", null)).StatusCode);

        // Transparenz: Thomas sieht die Freigabe und das Protokoll
        var aboutMe = await Read<AccessAboutMeDto>(await owner.GetAsync("/api/access/about-me"));
        Assert.Contains(aboutMe.Grants, g => g.Id == grant.Id && g.Viewer.Id == mariaId);
        Assert.Contains(aboutMe.Log, l => l.Action == "angesehen" && l.Viewer.Id == mariaId);
        Assert.Contains(aboutMe.Log, l => l.Action == "geoeffnet" && l.TaskId == privateTask.Id);

        // Bestätigte Freigaben lassen sich nicht löschen, nur widerrufen
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/access/{grant.Id}")).StatusCode);
        var revoked = await Read<AccessGrantDto>(await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/revoke", new { note = "Vertretung beendet" }));
        Assert.Equal("widerrufen", revoked.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks?view=person&id={thomasId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks/{privateTask.Id}")).StatusCode);
    }

    [Fact]
    public async Task Edit_grant_allows_changes_and_logs_them()
    {
        var admin = await app.LoginAsync("dev-anna");
        var owner = await app.LoginAsync("dev-jonas");
        var viewer = await app.LoginAsync("dev-thomas");
        var jonasId = await UserId("dev-jonas");
        var thomasId = await UserId("dev-thomas");

        var task = await Read<TaskDto>(await owner.PostAsJsonAsync("/api/tasks", new { title = "Heizung Haus 3 prüfen" }));
        var grant = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = jonasId, viewerId = thomasId, permission = "bearbeiten", validFrom = Today, purpose = "Krankheitsvertretung",
        }));
        await Read<AccessGrantDto>(await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "Original im Sekretariat" }));

        var done = await Read<TaskDto>(await viewer.PostAsync($"/api/tasks/{task.Id}/complete", null));
        Assert.Equal("erledigt", done.Status);
        var log = await Read<List<AccessLogDto>>(await admin.GetAsync($"/api/admin/access/{grant.Id}/log"));
        Assert.Contains(log, l => l.Action == "geaendert" && l.TaskId == task.Id && l.Viewer.Id == thomasId);
    }

    [Fact]
    public async Task Expired_grants_give_no_access_and_drafts_can_be_deleted()
    {
        var admin = await app.LoginAsync("dev-anna");
        var viewer = await app.LoginAsync("dev-jonas");
        var mariaId = await UserId("dev-maria");
        var jonasId = await UserId("dev-jonas");

        var grant = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = mariaId, viewerId = jonasId, permission = "lesen", validFrom = "2025-01-01", validUntil = "2025-01-31", purpose = "Alte Vertretung",
        }));
        // Ein bereits abgelaufener Zeitraum lässt sich nicht mehr aktivieren
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/access/{grant.Id}/confirm", new { note = "Personalakte" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tasks?view=person&id={mariaId}")).StatusCode);

        var draft = await Read<AccessGrantDto>(await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = mariaId, viewerId = jonasId, permission = "lesen", purpose = "Test",
        }));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/access/{draft.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/access", new
        {
            ownerId = mariaId, viewerId = mariaId, permission = "lesen", purpose = "Selbst",
        })).StatusCode);
    }
}
