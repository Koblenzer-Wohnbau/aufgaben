using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aufgaben.Tests;

public class WaitingRuleTests
{
    [Fact]
    public void Follow_up_day_is_a_week_later_and_never_on_a_weekend()
    {
        Assert.Equal(new DateOnly(2026, 11, 30), TaskService.Workday(new DateOnly(2026, 11, 28)));
        Assert.Equal(new DateOnly(2026, 11, 30), TaskService.Workday(new DateOnly(2026, 11, 29)));
        Assert.Equal(new DateOnly(2026, 11, 24), TaskService.Workday(new DateOnly(2026, 11, 24)));
        Assert.Equal(new DateOnly(2026, 11, 27), TaskService.DefaultFollowUp(new DateOnly(2026, 11, 20)));
        Assert.Equal(new DateOnly(2026, 11, 30), TaskService.DefaultFollowUp(new DateOnly(2026, 11, 21)));
    }

    [Fact]
    public void Follow_up_notification_uses_the_existing_reminder_activity()
    {
        var (type, subject, preview) = NotificationTexts.For(new Notification
        {
            Kind = NotificationOutbox.FollowUp,
            Title = "Angebot Dachdecker",
            ActorName = "Fa. Müller",
        });
        Assert.Equal("taskReminder", type);
        Assert.Equal("Nachhaken: Angebot Dachdecker", subject);
        Assert.Equal("Noch keine Antwort von Fa. Müller – nachhaken?", preview);
    }
}

[Collection("api")]
public sealed class WaitingApiTests(AppFixture app)
{
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task<string> Error(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString() ?? "";

    private DateOnly Today => app.Factory.Services.GetRequiredService<AppClock>().Today;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<bool> InView(HttpClient client, string view, int taskId) =>
        (await Read<List<TaskDto>>(await client.GetAsync($"/api/tasks?view={view}"))).Any(t => t.Id == taskId);

    private static async Task<List<string>> EventKinds(HttpClient client, int taskId) =>
        JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{taskId}")).RootElement
            .GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()!).ToList();

    private Task<int> UserId(string oid) =>
        app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync());

    private async Task QueueFollowUpsAsync()
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        await BackgroundWorker.QueueFollowUpsAsync(scope.ServiceProvider, CancellationToken.None);
    }

    [Fact]
    public async Task Waiting_task_leaves_the_lists_and_comes_back_with_a_nudge_on_the_follow_up_day()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var task = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Angebot Dachdecker einholen" }));

        // Ohne eigenen Tag: in einer Woche nachhaken
        var waiting = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { waitingFor = "  Fa. Müller " }));
        Assert.Equal("Fa. Müller", waiting.WaitingFor);
        Assert.Equal(Iso(Today), waiting.WaitingSince);
        Assert.Equal(Iso(TaskService.DefaultFollowUp(Today)), waiting.SnoozedUntil);
        Assert.True(waiting.IsSnoozed);
        Assert.Contains("warten", await EventKinds(thomas, task.Id));

        Assert.False(await InView(thomas, "aufgaben", task.Id));
        Assert.True(await InView(thomas, "warteschleife", task.Id));
        var counts = await Read<CountsDto>(await thomas.GetAsync("/api/counts"));
        Assert.True(counts.Waiting >= 1);

        // Der Tag zum Nachhaken ist da
        var today = Today;
        await app.WithDbAsync(db => db.Tasks.Where(t => t.Id == task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SnoozedUntil, today).SetProperty(t => t.MyDayOn, today)));
        Assert.True(await InView(thomas, "aufgaben", task.Id));
        Assert.True(await InView(thomas, "mein-tag", task.Id));
        Assert.True(await InView(thomas, "warteschleife", task.Id));
        Assert.True((await Read<CountsDto>(await thomas.GetAsync("/api/counts"))).WaitingDue >= 1);

        // Genau eine Erinnerung, auch wenn der Hintergrunddienst mehrfach läuft
        await QueueFollowUpsAsync();
        await QueueFollowUpsAsync();
        var nudges = await app.WithDbAsync(db => db.Notifications
            .Where(n => n.TaskId == task.Id && n.Kind == NotificationOutbox.FollowUp).ToListAsync());
        var nudge = Assert.Single(nudges);
        Assert.Equal("Fa. Müller", nudge.ActorName);
        Assert.Contains("Fa. Müller", nudge.Body);

        // Antwort ist da: kein Warten mehr, die Aufgabe steht im Tag
        var answered = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { waitingFor = (string?)null }));
        Assert.Equal("", answered.WaitingFor);
        Assert.Null(answered.SnoozedUntil);
        Assert.True(answered.MyDay);
        Assert.False(await InView(thomas, "warteschleife", task.Id));
        Assert.Contains("antwort-da", await EventKinds(thomas, task.Id));
    }

    [Fact]
    public async Task Snoozing_hides_until_the_day_but_a_deadline_always_wins()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var today = Today;

        var loose = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Ablage sortieren" }));
        var snoozed = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{loose.Id}", new { snoozedUntil = Iso(today.AddDays(3)) }));
        Assert.True(snoozed.IsSnoozed);
        Assert.False(await InView(thomas, "aufgaben", loose.Id));
        Assert.True(await InView(thomas, "warteschleife", loose.Id));
        Assert.True(await InView(thomas, "suche&q=Ablage%20sortieren", loose.Id));

        // Heute oder früher geht nicht
        var past = await thomas.PatchAsJsonAsync($"/api/tasks/{loose.Id}", new { snoozedUntil = Iso(today) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Contains("ab morgen", await Error(past));

        // Zurückholen: wieder da und im Tag
        var back = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{loose.Id}", new { snoozedUntil = (string?)null }));
        Assert.False(back.IsSnoozed);
        Assert.True(back.MyDay);
        Assert.Contains("zurueckgeholt", await EventKinds(thomas, loose.Id));

        // Schon fällige normale Aufgabe: Fälligkeit verschieben statt zurückstellen
        var due = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Rechnung prüfen", dueDate = Iso(today) }));
        var refused = await thomas.PatchAsJsonAsync($"/api/tasks/{due.Id}", new { snoozedUntil = Iso(today.AddDays(2)) });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Fälligkeit", await Error(refused));

        // Wiedervorlage: Das Datum der Wiedervorlage wandert mit
        var resubmission = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Mietvertrag Wiedervorlage", kind = "wiedervorlage", dueDate = Iso(today) }));
        var moved = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{resubmission.Id}", new { snoozedUntil = Iso(today.AddDays(5)) }));
        Assert.Equal(Iso(today.AddDays(5)), moved.DueDate);
        Assert.False(await InView(thomas, "wiedervorlagen", resubmission.Id));

        // Frist vor dem Tag: Die Aufgabe ist zur Frist wieder da
        var deadline = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Frist Widerspruch", dueDate = Iso(today.AddDays(2)) }));
        await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{deadline.Id}", new { snoozedUntil = Iso(today.AddDays(9)) }));
        Assert.False(await InView(thomas, "geplant", deadline.Id));
        await app.WithDbAsync(db => db.Tasks.Where(t => t.Id == deadline.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DueDate, today)));
        Assert.True(await InView(thomas, "geplant", deadline.Id));
        Assert.True(await InView(thomas, "mein-tag", deadline.Id));

        // Erledigt: nichts bleibt zurückgestellt
        await Read<TaskDto>(await thomas.PostAsync($"/api/tasks/{deadline.Id}/complete", null));
        var finished = await app.WithDbAsync(db => db.Tasks.SingleAsync(t => t.Id == deadline.Id));
        Assert.Null(finished.SnoozedUntil);
    }

    [Fact]
    public async Task Only_the_responsible_person_plans_waiting_and_a_handover_resets_it()
    {
        var anna = await app.LoginAsync("dev-anna");
        var thomas = await app.LoginAsync("dev-thomas");
        var thomasId = await UserId("dev-thomas");
        var mariaId = await UserId("dev-maria");
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Schlüssel bei Hausmeister abholen", assigneeId = thomasId }));

        // Wer nur delegiert hat, blendet der verantwortlichen Person nichts aus
        var refused = await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { waitingFor = "Hausmeister" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var waiting = await Read<TaskDto>(await thomas.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { waitingFor = "Hausmeister", snoozedUntil = Iso(Today.AddDays(4)) }));
        Assert.Equal(Iso(Today.AddDays(4)), waiting.SnoozedUntil);
        // Für die delegierende Person bleibt die Aufgabe sichtbar – mit Stand
        var delegated = await Read<List<TaskDto>>(await anna.GetAsync("/api/tasks?view=delegiert"));
        Assert.Equal("Hausmeister", Assert.Single(delegated, t => t.Id == task.Id).WaitingFor);

        // Neue verantwortliche Person: Warten und Zurückstellen gehörten der bisherigen
        var handedOver = await Read<TaskDto>(await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { assigneeId = mariaId }));
        Assert.Equal("", handedOver.WaitingFor);
        Assert.Null(handedOver.SnoozedUntil);
    }
}
