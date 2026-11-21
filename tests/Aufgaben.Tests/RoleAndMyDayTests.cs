using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class RoleAndMyDayTests(AppFixture app)
{
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private Task<int> UserId(string oid) =>
        app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync());

    [Fact]
    public async Task Admin_flag_is_removed_when_the_token_no_longer_carries_the_role()
    {
        // Sitzung ohne Rolle, in der Datenbank aber noch als Verwaltung markiert:
        // so sieht es aus, nachdem die App-Rolle in Entra entzogen wurde.
        var maria = await app.LoginAsync("dev-maria");
        await app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == "dev-maria")
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true)));

        var res = await maria.GetAsync("/api/admin/access");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == "dev-maria").Select(u => u.IsAdmin).SingleAsync()));
    }

    [Fact]
    public async Task My_day_belongs_to_the_responsible_person()
    {
        var anna = await app.LoginAsync("dev-anna");
        var thomas = await app.LoginAsync("dev-thomas");
        var thomasId = await UserId("dev-thomas");

        // Übergeben: "Mein Tag" der bisherigen Person wandert nicht mit
        var own = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Heute erledigen, dann übergeben", myDay = true }));
        Assert.True(own.MyDay);
        var moved = await Read<TaskDto>(await anna.PatchAsJsonAsync($"/api/tasks/{own.Id}", new { assigneeId = thomasId }));
        Assert.False(moved.MyDay);
        var thomasDay = await Read<List<TaskDto>>(await thomas.GetAsync("/api/tasks?view=mein-tag"));
        Assert.DoesNotContain(thomasDay, t => t.Id == own.Id);

        // Fremden Tag planen geht nicht, den eigenen schon
        var delegated = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Für Thomas", assigneeId = thomasId }));
        var refused = await anna.PatchAsJsonAsync($"/api/tasks/{delegated.Id}", new { myDay = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var planned = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{delegated.Id}", new { myDay = true }));
        Assert.True(planned.MyDay);
    }
}
