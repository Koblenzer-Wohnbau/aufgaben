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

public class AbsenceRuleTests
{
    [Fact]
    public void Return_day_is_the_first_workday_after_the_last_day_off()
    {
        // 2026-11-27 ist ein Freitag
        Assert.Equal(new DateOnly(2026, 11, 30), AbsenceService.ReturnDay(new DateOnly(2026, 11, 27)));
        Assert.Equal(new DateOnly(2026, 11, 30), AbsenceService.ReturnDay(new DateOnly(2026, 11, 28)));
        Assert.Equal(new DateOnly(2026, 11, 26), AbsenceService.ReturnDay(new DateOnly(2026, 11, 25)));
    }

    [Fact]
    public void Handover_covers_due_and_overdue_tasks_and_follow_ups_until_the_last_day()
    {
        var until = new DateOnly(2026, 12, 4);
        var rule = AbsenceService.CandidateRule(7, until).Compile();
        TaskItem Task(DateOnly? due = null, string waitingFor = "", DateOnly? followUp = null, int assignee = 7, string status = Values.Open) =>
            new() { Title = "Aufgabe", AssigneeId = assignee, DueDate = due, WaitingFor = waitingFor, SnoozedUntil = followUp, Status = status };

        Assert.True(rule(Task(due: until)));
        Assert.True(rule(Task(due: new DateOnly(2026, 11, 2)))); // längst überfällig
        Assert.False(rule(Task(due: until.AddDays(1))));
        Assert.False(rule(Task()));
        Assert.True(rule(Task(waitingFor: "Fa. Müller", followUp: until)));
        Assert.True(rule(Task(waitingFor: "Fa. Müller")));
        Assert.False(rule(Task(waitingFor: "Fa. Müller", followUp: until.AddDays(1))));
        Assert.False(rule(Task(due: until, status: Values.Done)));
        Assert.False(rule(Task(due: until, assignee: 8)));
    }

    [Fact]
    public void Handing_over_many_tasks_uses_the_existing_assignment_activity()
    {
        var (type, subject, preview) = NotificationTexts.For(new Notification
        {
            Kind = NotificationOutbox.Handover,
            Title = "3 Aufgaben für die Urlaubsvertretung",
            ActorName = "Max Becker",
        });
        Assert.Equal("taskAssigned", type);
        Assert.Equal("Max Becker hat dir 3 Aufgaben für die Urlaubsvertretung übergeben", subject);
        Assert.Equal(subject, preview);
    }
}

[Collection("api")]
public sealed class AbsenceApiTests(AppFixture app) : IAsyncLifetime
{
    public Task InitializeAsync() => SetAbsencesAsync(true);
    public Task DisposeAsync() => SetAbsencesAsync(false);

    private Task<int> SetAbsencesAsync(bool enabled) =>
        app.WithDbAsync(db => db.FeatureSettings.ExecuteUpdateAsync(set => set.SetProperty(s => s.AbsencesEnabled, enabled)));

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

    private static async Task<TaskDto> Create(HttpClient client, object body) =>
        await Read<TaskDto>(await client.PostAsJsonAsync("/api/tasks", body));

    private static async Task<TaskDto> Get(HttpClient client, int taskId) =>
        (await Read<TaskDetailDto>(await client.GetAsync($"/api/tasks/{taskId}"))).Task;

    private static async Task<List<string>> EventKinds(HttpClient client, int taskId) =>
        (await Read<TaskDetailDto>(await client.GetAsync($"/api/tasks/{taskId}"))).Events.Select(e => e.Kind).ToList();

    private static Task<HttpResponseMessage> Decide(HttpClient client, int absenceId, int taskId, string decision, string? note = null) =>
        client.PostAsJsonAsync($"/api/absences/{absenceId}/tasks/{taskId}", new { decision, note });

    /// <summary>Eigene Personen je Test, „Alle übergeben“ berührt so weder Testdaten noch andere Tests.</summary>
    private async Task<(HttpClient Client, int Id)> PersonAsync(string name)
    {
        var oid = "urlaub-" + Guid.NewGuid().ToString("N")[..12];
        var id = await app.WithDbAsync(async db =>
        {
            var user = new User { EntraOid = oid, DisplayName = name, Email = $"{oid}@example.test", CreatedAt = DateTimeOffset.UtcNow };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });
        return (await app.LoginAsync(oid), id);
    }

    [Fact]
    public async Task Handover_reaches_the_deputy_and_the_return_shows_what_happened()
    {
        var (max, maxId) = await PersonAsync("Max Urlaub");
        var (eva, evaId) = await PersonAsync("Eva Vertretung");
        var (anna, _) = await PersonAsync("Anna Auftrag");
        var today = Today;

        var dueSoon = await Create(max, new { title = "Nebenkostenabrechnung prüfen", dueDate = Iso(today.AddDays(3)) });
        var overdue = await Create(max, new { title = "Rückruf Mieter Schmidt", dueDate = Iso(today.AddDays(-2)) });
        var later = await Create(max, new { title = "Jahresplanung", dueDate = Iso(today.AddDays(30)) });
        var loose = await Create(max, new { title = "Ablage sortieren" });
        var keepMe = await Create(max, new { title = "Mitarbeitergespräch vorbereiten", dueDate = Iso(today.AddDays(4)) });
        var waiting = await Create(max, new { title = "Angebot Dachdecker" });
        await Read<TaskDto>(await max.PatchAsJsonAsync($"/api/tasks/{waiting.Id}", new { waitingFor = "Fa. Müller", snoozedUntil = Iso(today.AddDays(2)) }));
        var fromAnna = await Create(anna, new { title = "Schlüsselübergabe Haus 3", assigneeId = maxId, dueDate = Iso(today.AddDays(1)) });

        var absence = await Read<AbsenceDto>(await max.PostAsJsonAsync("/api/absences", new { from = Iso(today), until = Iso(today.AddDays(7)), deputyId = evaId }));
        Assert.Equal(Iso(AbsenceService.ReturnDay(today.AddDays(7))), absence.ReturnDay);
        Assert.True(absence.Started);
        Assert.False(absence.Ended);

        // Im Urlaub fällig, überfällig oder Nachhaken, Späteres und Terminloses nicht
        var plan = await Read<AbsencePlanDto>(await max.GetAsync($"/api/absences/{absence.Id}/plan"));
        var planned = plan.Items.Select(i => i.TaskId).ToList();
        Assert.Equal(new[] { overdue.Id, fromAnna.Id, waiting.Id, dueSoon.Id, keepMe.Id }.Order(), planned.Order());
        Assert.DoesNotContain(later.Id, planned);
        Assert.DoesNotContain(loose.Id, planned);
        Assert.All(plan.Items, i => Assert.Null(i.Decision));

        // Einzeln übergeben, mit Hinweis als Kommentar und normaler Benachrichtigung
        var handed = await Read<AbsenceItemDto>(await Decide(max, absence.Id, dueSoon.Id, "uebergeben", "  Unterlagen liegen im Fach "));
        Assert.Equal("uebergeben", handed.Decision);
        Assert.Equal(evaId, handed.Assignee.Id);
        Assert.True(handed.CanOpen); // selbst angelegt
        var detail = await Read<TaskDetailDto>(await eva.GetAsync($"/api/tasks/{dueSoon.Id}"));
        Assert.Contains(detail.Events, e => e.Kind == "vertretung");
        Assert.Contains(detail.Events, e => e.Kind == "kommentar" && e.Text == "Unterlagen liegen im Fach");
        var assigned = await app.WithDbAsync(db => db.Notifications
            .SingleAsync(n => n.UserId == evaId && n.TaskId == dueSoon.Id && n.Kind == NotificationOutbox.Assigned));
        Assert.Contains("Urlaubsvertretung für Max Urlaub", assigned.Body);
        Assert.Contains("Unterlagen liegen im Fach", assigned.Body);

        // Bleibt bei mir: bis zum ersten Arbeitstag danach zurückgestellt
        Assert.Equal("behalten", (await Read<AbsenceItemDto>(await Decide(max, absence.Id, keepMe.Id, "behalten"))).Decision);
        var keptTask = await Get(max, keepMe.Id);
        Assert.Equal(absence.ReturnDay, keptTask.SnoozedUntil);
        Assert.True(keptTask.IsSnoozed);
        // Schon überfällig: Die Frist geht vor, nichts wird ausgeblendet
        await Read<AbsenceItemDto>(await Decide(max, absence.Id, overdue.Id, "behalten"));
        Assert.Null((await Get(max, overdue.Id)).SnoozedUntil);

        // Alle übrigen übergeben, eine einzige Benachrichtigung
        Assert.Equal(2, (await Read<AbsenceCountDto>(await max.PostAsync($"/api/absences/{absence.Id}/handover-all", null))).Count);
        var bundle = await app.WithDbAsync(db => db.Notifications.SingleAsync(n => n.UserId == evaId && n.Kind == NotificationOutbox.Handover));
        Assert.Equal("2 Aufgaben für die Urlaubsvertretung", bundle.Title);
        Assert.Contains("Angebot Dachdecker", bundle.Body);
        Assert.False(await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == evaId && n.Kind == NotificationOutbox.Assigned
                                                                            && (n.TaskId == waiting.Id || n.TaskId == fromAnna.Id))));
        // „Warte auf …“ übernimmt die Vertretung samt Tag zum Nachhaken
        var waitingForEva = await Get(eva, waiting.Id);
        Assert.Equal("Fa. Müller", waitingForEva.WaitingFor);
        Assert.Equal(Iso(today.AddDays(2)), waitingForEva.SnoozedUntil);

        // Sichtbarkeit wie sonst: Annas Aufgabe liegt jetzt bei Eva, Max ist weder Ersteller noch verantwortlich
        Assert.Equal(HttpStatusCode.NotFound, (await max.GetAsync($"/api/tasks/{fromAnna.Id}")).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.AccessGrants.AnyAsync(g => g.OwnerId == maxId || g.ViewerId == evaId)));

        // Die Vertretung sieht das Banner und die übergebenen Aufgaben
        var evaStatus = await Read<AbsenceStatusDto>(await eva.GetAsync("/api/absences/status"));
        var deputy = Assert.Single(evaStatus.Deputy);
        Assert.Equal(maxId, deputy.Person.Id);
        Assert.Equal(3, deputy.Open);
        var evaList = await Read<List<TaskDto>>(await eva.GetAsync($"/api/tasks?view=vertretung&id={absence.Id}"));
        Assert.Equal(new[] { dueSoon.Id, waiting.Id, fromAnna.Id }.Order(), evaList.Select(t => t.Id).Order());

        // Im Urlaub: Eva erledigt, Anna kommentiert und gibt Max etwas Neues
        await Read<TaskDto>(await eva.PostAsync($"/api/tasks/{dueSoon.Id}/complete", null));
        await Read<EventDto>(await anna.PostAsJsonAsync($"/api/tasks/{fromAnna.Id}/comments", new { text = "Schlüssel sind beim Hausmeister" }));
        var fresh = await Create(anna, new { title = "Neue Mieteranfrage", assigneeId = maxId });

        // Vorzeitig zurück: Behaltenes ist sofort wieder da
        Assert.True((await Read<AbsenceDto>(await max.PostAsync($"/api/absences/{absence.Id}/end", null))).Ended);
        var backTask = await Get(max, keepMe.Id);
        Assert.Null(backTask.SnoozedUntil);
        Assert.True(backTask.MyDay);
        var maxStatus = await Read<AbsenceStatusDto>(await max.GetAsync("/api/absences/status"));
        Assert.Null(maxStatus.Current);
        Assert.Equal(absence.Id, maxStatus.Returned?.Id);
        Assert.Empty((await Read<AbsenceStatusDto>(await eva.GetAsync("/api/absences/status"))).Deputy);

        // Was ist passiert?
        var summary = await Read<AbsenceSummaryDto>(await max.GetAsync($"/api/absences/{absence.Id}/summary"));
        var done = Assert.Single(summary.Handovers, h => h.TaskId == dueSoon.Id);
        Assert.Equal("erledigt", done.Status);
        Assert.Equal(evaId, done.CompletedBy?.Id);
        Assert.False(done.CanTakeBack);
        var annaTask = Assert.Single(summary.Handovers, h => h.TaskId == fromAnna.Id);
        Assert.True(annaTask.CanTakeBack);
        Assert.False(annaTask.CanOpen);
        Assert.True(Assert.Single(summary.Handovers, h => h.TaskId == waiting.Id).CanTakeBack);
        Assert.Equal(2, summary.Kept);
        Assert.Contains(summary.Events, e => e.TaskId == dueSoon.Id && e.Kind == "erledigt" && e.Actor?.Id == evaId);
        Assert.Contains(summary.Events, e => e.TaskId == fromAnna.Id && e.Kind == "kommentar" && e.Text == "Schlüssel sind beim Hausmeister");
        Assert.Contains(summary.Events, e => e.TaskId == fresh.Id && e.TaskTitle == "Neue Mieteranfrage");
        Assert.DoesNotContain(summary.Events, e => e.Actor?.Id == maxId);

        // Zurück an mich: erst eine, dann alle übrigen
        var one = await max.PostAsJsonAsync($"/api/absences/{absence.Id}/take-back", new { taskIds = new[] { fromAnna.Id } });
        Assert.Equal(1, (await Read<AbsenceCountDto>(one)).Count);
        var annaBack = await Get(max, fromAnna.Id);
        Assert.Equal(maxId, annaBack.Assignee.Id);
        Assert.True(annaBack.MyDay);
        Assert.Contains("vertretung-zurueck", await EventKinds(max, fromAnna.Id));
        Assert.Equal(1, (await Read<AbsenceCountDto>(await max.PostAsJsonAsync($"/api/absences/{absence.Id}/take-back", new { }))).Count);
        var waitingBack = await Get(max, waiting.Id);
        Assert.Equal(maxId, waitingBack.Assignee.Id);
        Assert.Equal("Fa. Müller", waitingBack.WaitingFor);
        Assert.Equal(0, (await Read<AbsenceCountDto>(await max.PostAsJsonAsync($"/api/absences/{absence.Id}/take-back", new { }))).Count);

        // Übersicht schließen
        Assert.Equal(HttpStatusCode.NoContent, (await max.PostAsync($"/api/absences/{absence.Id}/summary/close", null)).StatusCode);
        Assert.Null((await Read<AbsenceStatusDto>(await max.GetAsync("/api/absences/status"))).Returned);
    }

    [Fact]
    public async Task Cancelling_the_holiday_brings_everything_back_as_it_was()
    {
        var (mia, miaId) = await PersonAsync("Mia Abgesagt");
        var (jo, joId) = await PersonAsync("Jo Vertretung");
        var today = Today;

        var waiting = await Create(mia, new { title = "Antwort Hausverwaltung" });
        await Read<TaskDto>(await mia.PatchAsJsonAsync($"/api/tasks/{waiting.Id}", new { waitingFor = "Hausverwaltung Nord", snoozedUntil = Iso(today.AddDays(3)) }));
        var resubmission = await Create(mia, new { title = "Mietvertrag Wiedervorlage", kind = "wiedervorlage", dueDate = Iso(today.AddDays(5)) });
        var report = await Create(mia, new { title = "Monatsbericht", dueDate = Iso(today.AddDays(4)) });

        var absence = await Read<AbsenceDto>(await mia.PostAsJsonAsync("/api/absences", new { from = Iso(today.AddDays(2)), until = Iso(today.AddDays(9)), deputyId = joId }));
        Assert.False(absence.Started);

        await Read<AbsenceItemDto>(await Decide(mia, absence.Id, waiting.Id, "behalten"));
        Assert.Equal(absence.ReturnDay, (await Get(mia, waiting.Id)).SnoozedUntil);
        await Read<AbsenceItemDto>(await Decide(mia, absence.Id, resubmission.Id, "behalten"));
        Assert.Equal(absence.ReturnDay, (await Get(mia, resubmission.Id)).DueDate); // das Datum der Wiedervorlage wandert mit
        await Read<AbsenceItemDto>(await Decide(mia, absence.Id, report.Id, "uebergeben"));
        Assert.Equal(joId, (await Get(jo, report.Id)).Assignee.Id);

        // Noch nicht angefangen: beenden geht nicht, nur absagen
        var end = await mia.PostAsync($"/api/absences/{absence.Id}/end", null);
        Assert.Equal(HttpStatusCode.BadRequest, end.StatusCode);
        Assert.Contains("löschen", await Error(end));

        Assert.Equal(HttpStatusCode.NoContent, (await mia.DeleteAsync($"/api/absences/{absence.Id}")).StatusCode);
        var waitingAfter = await Get(mia, waiting.Id);
        Assert.Equal(Iso(today.AddDays(3)), waitingAfter.SnoozedUntil);
        Assert.Equal("Hausverwaltung Nord", waitingAfter.WaitingFor);
        var resubmissionAfter = await Get(mia, resubmission.Id);
        Assert.Equal(Iso(today.AddDays(5)), resubmissionAfter.DueDate);
        Assert.Null(resubmissionAfter.SnoozedUntil);
        var reportAfter = await Get(mia, report.Id);
        Assert.Equal(miaId, reportAfter.Assignee.Id);
        var kinds = await EventKinds(mia, report.Id);
        Assert.Contains("vertretung", kinds);
        Assert.Contains("vertretung-zurueck", kinds);

        Assert.Equal(HttpStatusCode.NotFound, (await mia.GetAsync($"/api/absences/{absence.Id}/plan")).StatusCode);
        Assert.Null((await Read<AbsenceStatusDto>(await mia.GetAsync("/api/absences/status"))).Current);
    }

    [Fact]
    public async Task Next_occurrence_of_a_recurring_task_stays_part_of_the_handover()
    {
        var (max, maxId) = await PersonAsync("Max Wiederholung");
        var (eva, evaId) = await PersonAsync("Eva Wiederholung");
        var today = Today;
        var weekly = await Create(max, new { title = "Zählerstände ablesen", dueDate = Iso(today.AddDays(1)), recurrence = "woechentlich" });
        var absence = await Read<AbsenceDto>(await max.PostAsJsonAsync("/api/absences", new { from = Iso(today), until = Iso(today.AddDays(5)), deputyId = evaId }));

        Assert.Equal(1, (await Read<AbsenceCountDto>(await max.PostAsync($"/api/absences/{absence.Id}/handover-all", null))).Count);
        // Nur eine Aufgabe: die normale Benachrichtigung zur Zuweisung
        Assert.True(await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == evaId && n.TaskId == weekly.Id && n.Kind == NotificationOutbox.Assigned)));

        await Read<TaskDto>(await eva.PostAsync($"/api/tasks/{weekly.Id}/complete", null));
        var list = await Read<List<TaskDto>>(await eva.GetAsync($"/api/tasks?view=vertretung&id={absence.Id}"));
        var next = Assert.Single(list, t => t.Id != weekly.Id);
        Assert.Equal("offen", next.Status);
        Assert.Equal(evaId, next.Assignee.Id);

        Assert.Equal(1, (await Read<AbsenceCountDto>(await max.PostAsJsonAsync($"/api/absences/{absence.Id}/take-back", new { }))).Count);
        Assert.Equal(maxId, (await Get(max, next.Id)).Assignee.Id);
    }

    [Fact]
    public async Task Holidays_are_checked_and_only_the_owner_plans_them()
    {
        var (ulla, ullaId) = await PersonAsync("Ulla Prüfung");
        var (ben, benId) = await PersonAsync("Ben Vertretung");
        var (olga, _) = await PersonAsync("Olga Andere");
        var today = Today;

        async Task<string> Refused(HttpClient client, object body)
        {
            var res = await client.PostAsJsonAsync("/api/absences", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            return await Error(res);
        }

        Assert.Contains("vor dem ersten", await Refused(ulla, new { from = Iso(today.AddDays(5)), until = Iso(today.AddDays(2)), deputyId = benId }));
        Assert.Contains("jemand anderes", await Refused(ulla, new { from = Iso(today), until = Iso(today.AddDays(2)), deputyId = ullaId }));
        Assert.Contains("Vertretung", await Refused(ulla, new { from = Iso(today), until = Iso(today.AddDays(2)) }));
        Assert.Contains("vorbei", await Refused(ulla, new { from = Iso(today.AddDays(-5)), until = Iso(today.AddDays(-1)), deputyId = benId }));

        var absence = await Read<AbsenceDto>(await ulla.PostAsJsonAsync("/api/absences", new { from = Iso(today.AddDays(1)), until = Iso(today.AddDays(3)), deputyId = benId }));
        Assert.Contains("schon ein Urlaub", await Refused(ulla, new { from = Iso(today.AddDays(10)), until = Iso(today.AddDays(12)), deputyId = benId }));
        // Wer selbst in der Zeit weg ist, kann nicht vertreten
        Assert.Contains("selbst abwesend", await Refused(olga, new { from = Iso(today.AddDays(2)), until = Iso(today.AddDays(4)), deputyId = ullaId }));

        // Fremde sehen und ändern nichts
        Assert.Equal(HttpStatusCode.NotFound, (await olga.GetAsync($"/api/absences/{absence.Id}/plan")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await olga.PostAsync($"/api/absences/{absence.Id}/handover-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await olga.DeleteAsync($"/api/absences/{absence.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/absences/{absence.Id}/summary")).StatusCode);

        // Übergeben lassen sich nur eigene Aufgaben
        var foreign = await Create(olga, new { title = "Fremde Aufgabe", dueDate = Iso(today.AddDays(1)) });
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(ulla, absence.Id, foreign.Id, "uebergeben")).StatusCode);
        var own = await Create(ulla, new { title = "Eigene Aufgabe", dueDate = Iso(today.AddDays(1)) });
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(ulla, absence.Id, own.Id, "irgendwas")).StatusCode);
        var longNote = await Decide(ulla, absence.Id, own.Id, "uebergeben", new string('x', AbsenceService.MaxNote + 1));
        Assert.Equal(HttpStatusCode.BadRequest, longNote.StatusCode);
        Assert.Equal(ullaId, (await Get(ulla, own.Id)).Assignee.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await ulla.DeleteAsync($"/api/absences/{absence.Id}")).StatusCode);
    }
}
