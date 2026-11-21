using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aufgaben.Tests;

[Collection("api")]
public sealed class TrashTests(AppFixture app)
{
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task<List<TaskDto>> View(HttpClient client, string view) =>
        await Read<List<TaskDto>>(await client.GetAsync($"/api/tasks?view={view}"));

    private Task<int> UserId(string oid) =>
        app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync());

    private Task<bool> ExistsInDb(int id) =>
        app.WithDbAsync(db => db.Tasks.IgnoreQueryFilters().AnyAsync(t => t.Id == id));

    private static async Task<TaskDto> Create(HttpClient client, object body) =>
        await Read<TaskDto>(await client.PostAsJsonAsync("/api/tasks", body));

    [Fact]
    public async Task Deleting_moves_to_the_trash_where_it_can_be_restored_or_purged()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var task = await Create(thomas, new { title = "Altes Angebot Fenster", checklist = new[] { "Preise vergleichen" } });

        Assert.Equal(HttpStatusCode.NoContent, (await thomas.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        // Überall weg …
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.DoesNotContain(await View(thomas, "aufgaben"), t => t.Id == task.Id);
        Assert.DoesNotContain(await View(thomas, "suche&q=Altes%20Angebot%20Fenster"), t => t.Id == task.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.PostAsync($"/api/tasks/{task.Id}/complete", null)).StatusCode);
        // … nur im Papierkorb, mit wer und wann
        var trashed = Assert.Single(await View(thomas, "papierkorb"), t => t.Id == task.Id);
        Assert.NotNull(trashed.DeletedAt);
        Assert.Equal("Thomas Becker", trashed.DeletedBy?.Name);
        Assert.True((await Read<CountsDto>(await thomas.GetAsync("/api/counts"))).Trash >= 1);

        // Andere sehen fremde Papierkörbe nicht
        var maria = await app.LoginAsync("dev-maria");
        Assert.DoesNotContain(await View(maria, "papierkorb"), t => t.Id == task.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PostAsync($"/api/tasks/{task.Id}/restore", null)).StatusCode);

        // Wiederherstellen: alles wieder da, mit Verlauf
        var restored = await Read<TaskDto>(await thomas.PostAsync($"/api/tasks/{task.Id}/restore", null));
        Assert.Null(restored.DeletedAt);
        Assert.Single(restored.Checklist);
        var detail = JsonDocument.Parse(await thomas.GetStringAsync($"/api/tasks/{task.Id}")).RootElement;
        var kinds = detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Contains("papierkorb", kinds);
        Assert.Contains("wiederhergestellt", kinds);

        // Endgültig löschen geht nur aus dem Papierkorb
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.DeleteAsync($"/api/tasks/{task.Id}/endgueltig")).StatusCode);
        await thomas.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await thomas.DeleteAsync($"/api/tasks/{task.Id}/endgueltig")).StatusCode);
        Assert.False(await ExistsInDb(task.Id));
    }

    [Fact]
    public async Task Bulk_actions_skip_what_the_person_may_not_do()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var anna = await app.LoginAsync("dev-anna");
        var thomasId = await UserId("dev-thomas");
        var own = new List<TaskDto>();
        for (var i = 1; i <= 3; i++)
        {
            own.Add(await Create(thomas, new { title = $"Sammelaktion {i}" }));
        }
        var fromAnna = await Create(anna, new { title = "Von Anna für Thomas", assigneeId = thomasId });
        var ids = own.Select(t => t.Id).Append(fromAnna.Id).Append(999_999).ToList();

        var done = await Read<BulkResultDto>(await thomas.PostAsJsonAsync("/api/tasks/bulk", new { action = "erledigen", ids }));
        Assert.Equal(new BulkResultDto(4, 1), done); // die von Anna darf Thomas erledigen, die unbekannte nicht

        var deleted = await Read<BulkResultDto>(await thomas.PostAsJsonAsync("/api/tasks/bulk", new { action = "loeschen", ids }));
        Assert.Equal(new BulkResultDto(3, 2), deleted); // löschen darf nur, wer angelegt hat
        Assert.Contains(await View(thomas, "erledigt"), t => t.Id == fromAnna.Id);

        var back = await Read<BulkResultDto>(await thomas.PostAsJsonAsync("/api/tasks/bulk", new { action = "wiederherstellen", ids }));
        Assert.Equal(3, back.Done);

        var unknown = await thomas.PostAsJsonAsync("/api/tasks/bulk", new { action = "verschenken", ids });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var tooMany = await thomas.PostAsJsonAsync("/api/tasks/bulk", new { action = "loeschen", ids = Enumerable.Range(1, TaskService.MaxBulk + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Completed_tasks_go_to_the_trash_in_one_step_and_the_trash_can_be_emptied()
    {
        var maria = await app.LoginAsync("dev-maria");
        var anna = await app.LoginAsync("dev-anna");
        var mariaId = await UserId("dev-maria");
        var a = await Create(maria, new { title = "Erledigt A" });
        var b = await Create(maria, new { title = "Erledigt B" });
        var open = await Create(maria, new { title = "Noch offen" });
        var fromAnna = await Create(anna, new { title = "Von Anna erledigt", assigneeId = mariaId });
        foreach (var t in new[] { a, b, fromAnna })
        {
            await Read<TaskDto>(await maria.PostAsync($"/api/tasks/{t.Id}/complete", null));
        }

        var result = await Read<BulkResultDto>(await maria.PostAsync("/api/tasks/erledigte-loeschen", null));
        Assert.True(result.Done >= 2);
        Assert.True(result.Skipped >= 1); // von Anna angelegt – bleibt
        var trash = await View(maria, "papierkorb");
        Assert.Contains(trash, t => t.Id == a.Id);
        Assert.Contains(trash, t => t.Id == b.Id);
        Assert.DoesNotContain(trash, t => t.Id == open.Id || t.Id == fromAnna.Id);

        var emptied = await Read<BulkResultDto>(await maria.PostAsync("/api/papierkorb/leeren", null));
        Assert.True(emptied.Done >= 2);
        Assert.Empty(await View(maria, "papierkorb"));
        Assert.False(await ExistsInDb(a.Id));
        Assert.True(await ExistsInDb(open.Id));
    }

    [Fact]
    public async Task The_trash_is_emptied_after_thirty_days()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var old = await Create(thomas, new { title = "Lange im Papierkorb" });
        var fresh = await Create(thomas, new { title = "Gerade gelöscht" });
        await thomas.DeleteAsync($"/api/tasks/{old.Id}");
        await thomas.DeleteAsync($"/api/tasks/{fresh.Id}");
        var longAgo = app.Factory.Services.GetRequiredService<AppClock>().UtcNow.AddDays(-TaskService.TrashDays - 1);
        await app.WithDbAsync(db => db.Tasks.IgnoreQueryFilters().Where(t => t.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, longAgo)));

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            await BackgroundWorker.PurgeTrashAsync(scope.ServiceProvider, CancellationToken.None);
        }

        Assert.False(await ExistsInDb(old.Id));
        Assert.True(await ExistsInDb(fresh.Id));
    }
}
