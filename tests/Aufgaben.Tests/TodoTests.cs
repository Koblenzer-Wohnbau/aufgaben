using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class TodoTests(AppFixture app)
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
    public async Task Shared_list_is_visible_to_members_only_and_ends_when_leaving()
    {
        var anna = await app.LoginAsync("dev-anna");
        var thomas = await app.LoginAsync("dev-thomas");
        var maria = await app.LoginAsync("dev-maria");
        var thomasId = await UserId("dev-thomas");

        var list = await Read<ListDto>(await anna.PostAsJsonAsync("/api/lists", new { name = "Büro-Einkauf", color = "gruen" }));
        var annaTask = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Druckerpapier", listId = list.Id }));

        // Vor dem Teilen: Thomas sieht nichts
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks?view=liste&id={list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{annaTask.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await anna.PostAsJsonAsync($"/api/lists/{list.Id}/members", new { userId = thomasId })).StatusCode);
        var shared = await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == thomasId && n.Kind == NotificationOutbox.ListShared));
        Assert.True(shared);

        // Thomas sieht die Liste, ihre Aufgaben und kann selbst etwas hinzufügen
        var thomasLists = await Read<List<ListDto>>(await thomas.GetAsync("/api/lists"));
        var seen = Assert.Single(thomasLists, l => l.Id == list.Id);
        Assert.False(seen.IsOwner);
        Assert.Equal("Anna Schneider", seen.Owner.Name);
        var items = await Read<List<TaskDto>>(await thomas.GetAsync($"/api/tasks?view=liste&id={list.Id}"));
        Assert.Contains(items, t => t.Id == annaTask.Id);
        var thomasTask = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Kaffee", listId = list.Id }));

        var annaView = await Read<List<TaskDto>>(await anna.GetAsync($"/api/tasks?view=liste&id={list.Id}"));
        Assert.Contains(annaView, t => t.Id == thomasTask.Id);

        // Nicht-Mitglieder bleiben draußen
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/tasks?view=liste&id={list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/tasks/{annaTask.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PostAsJsonAsync($"/api/lists/{list.Id}/members", new { userId = thomasId })).StatusCode);

        // Mitglied darf nicht teilen und nicht löschen, aber verlassen
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.DeleteAsync($"/api/lists/{list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await thomas.DeleteAsync($"/api/lists/{list.Id}/members/{thomasId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks?view=liste&id={list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{annaTask.Id}")).StatusCode);
    }

    [Fact]
    public async Task Tasks_view_shows_own_tasks_without_list()
    {
        var jonas = await app.LoginAsync("dev-jonas");
        var list = await Read<ListDto>(await jonas.PostAsJsonAsync("/api/lists", new { name = "Werkstatt" }));
        var loose = await Read<TaskDto>(await jonas.PostAsJsonAsync("/api/tasks", new { title = "Lose Aufgabe" }));
        var inList = await Read<TaskDto>(await jonas.PostAsJsonAsync("/api/tasks", new { title = "In Liste", listId = list.Id }));

        var view = await Read<List<TaskDto>>(await jonas.GetAsync("/api/tasks?view=aufgaben"));
        Assert.Contains(view, t => t.Id == loose.Id);
        Assert.DoesNotContain(view, t => t.Id == inList.Id);

        var counts = await Read<CountsDto>(await jonas.GetAsync("/api/counts"));
        Assert.True(counts.Inbox >= 1);
        Assert.Equal(1, counts.Lists[list.Id]);
    }

    [Fact]
    public async Task Suggestions_offer_leftovers_due_soon_and_important_tasks()
    {
        var maria = await app.LoginAsync("dev-maria");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Berlin"));

        var soon = await Read<TaskDto>(await maria.PostAsJsonAsync("/api/tasks", new { title = "Bald fällig", dueDate = today.AddDays(2).ToString("yyyy-MM-dd") }));
        var later = await Read<TaskDto>(await maria.PostAsJsonAsync("/api/tasks", new { title = "Später fällig", dueDate = today.AddDays(30).ToString("yyyy-MM-dd") }));
        var important = await Read<TaskDto>(await maria.PostAsJsonAsync("/api/tasks", new { title = "Wichtig ohne Termin", priority = "hoch" }));
        var already = await Read<TaskDto>(await maria.PostAsJsonAsync("/api/tasks", new { title = "Schon im Tag", myDay = true, dueDate = today.AddDays(1).ToString("yyyy-MM-dd") }));
        var leftover = await Read<TaskDto>(await maria.PostAsJsonAsync("/api/tasks", new { title = "Von gestern" }));
        await app.WithDbAsync(async db =>
        {
            var t = await db.Tasks.SingleAsync(x => x.Id == leftover.Id);
            t.MyDayOn = today.AddDays(-1);
            t.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
            return await db.SaveChangesAsync();
        });

        var s = await Read<SuggestionsDto>(await maria.GetAsync("/api/suggestions"));
        Assert.Contains(s.Yesterday, t => t.Id == leftover.Id);
        Assert.Contains(s.DueSoon, t => t.Id == soon.Id);
        Assert.Contains(s.Important, t => t.Id == important.Id);
        var all = s.Yesterday.Concat(s.DueSoon).Concat(s.Important).Concat(s.Recent).ToList();
        Assert.DoesNotContain(all, t => t.Id == already.Id);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count()); // keine Doppelungen
        Assert.Contains(s.Recent, t => t.Id == later.Id); // frisch angelegt, Termin noch weit weg
    }
}
