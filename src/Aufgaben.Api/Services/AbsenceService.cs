using System.Globalization;
using System.Linq.Expressions;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Services;

/// <summary>
/// Urlaubsübergabe: Urlaub mit Vertretung eintragen, eigene Aufgaben an die Vertretung übergeben oder bis
/// nach dem Urlaub zurückstellen, nach der Rückkehr „Was ist passiert?“ und „Zurück an mich“.
///
/// Übergeben ist eine normale Zuweisung an die Vertretung (mit Benachrichtigung). Es entstehen keine
/// Einsichtsrechte, und die Sichtbarkeitsregeln aus <see cref="TaskService"/> bleiben, wie sie sind: Was die
/// Person nach der Übergabe nicht mehr sehen darf, erscheint nur in ihrer eigenen Übersicht – mit Titel und Stand.
/// </summary>
public sealed class AbsenceService(
    AppDbContext db,
    CurrentUser me,
    AppClock clock,
    TaskService tasks,
    NotificationOutbox outbox,
    RealtimeHub hub)
{
    /// <summary>Höchstens so weit im Voraus und so lang am Stück.</summary>
    public const int MaxDays = 366;

    /// <summary>So lange nach der Rückkehr bleibt „Was ist passiert?“ in „Mein Tag“ stehen.</summary>
    public const int SummaryDays = 30;

    public const int MaxNote = 300;
    public const int MaxEvents = 100;

    /// <summary>Erster Arbeitstag nach dem letzten Urlaubstag (Samstag und Sonntag zählen nicht).</summary>
    internal static DateOnly ReturnDay(DateOnly until) => TaskService.Workday(until.AddDays(1));

    /// <summary>
    /// Kommt die Aufgabe in die Übergabe? Offen, der Person zugewiesen und bis zum letzten Urlaubstag fällig
    /// (auch schon überfällig) – oder „Warte auf …“ mit einem Tag zum Nachhaken bis dahin.
    /// </summary>
    internal static Expression<Func<TaskItem, bool>> CandidateRule(int userId, DateOnly until) =>
        t => t.Status == Values.Open && t.AssigneeId == userId
             && (t.DueDate <= until || (t.WaitingFor != "" && (t.SnoozedUntil == null || t.SnoozedUntil <= until)));

    private IQueryable<TaskItem> Candidates(DateOnly until) => db.Tasks.Where(CandidateRule(me.Id, until));

    // ---------------------------------------------------------------- Lesen

    /// <summary>Eigener Urlaub (eingetragen oder zurück mit offener Übersicht) und laufende Vertretungen.</summary>
    public async Task<AbsenceStatusDto> StatusAsync(CancellationToken ct)
    {
        var u = me.Id;
        var today = clock.Today;
        var mine = await db.Absences.Include(a => a.Deputy)
            .Where(a => a.UserId == u && ((a.ClosedAt == null && a.Until >= today) || (a.SummaryClosedAt == null && a.From <= today)))
            .OrderByDescending(a => a.From)
            .Take(10)
            .ToListAsync(ct);
        var current = mine.FirstOrDefault(a => a.ClosedAt == null && a.Until >= today);
        var returned = mine.FirstOrDefault(a => a.SummaryClosedAt == null && IsBack(a, today) && BackOn(a).AddDays(SummaryDays) >= today);

        var deputy = await db.Absences.Include(a => a.User)
            .Where(a => a.DeputyId == u && a.ClosedAt == null && a.From <= today && a.Until >= today)
            .OrderBy(a => a.Until)
            .ToListAsync(ct);
        var deputyIds = deputy.Select(a => a.Id).ToList();
        var open = Values.Open;
        var handedOver = AbsenceTask.HandedOver;
        var openCounts = await db.AbsenceTasks
            .Where(h => deputyIds.Contains(h.AbsenceId) && h.Decision == handedOver && h.ReturnedAt == null
                        && db.Tasks.Any(t => t.Id == h.TaskId && t.AssigneeId == u && t.Status == open))
            .GroupBy(h => h.AbsenceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new AbsenceStatusDto(
            current is null ? null : await ToDtoAsync(current, ct),
            returned is null ? null : await ToDtoAsync(returned, ct),
            deputy.Select(a => new DeputyDto(a.Id, UserRef.From(a.User), Iso(a.From), Iso(a.Until), openCounts.GetValueOrDefault(a.Id))).ToList());
    }

    /// <summary>Was für den Urlaub zu entscheiden ist – und was schon entschieden wurde.</summary>
    public async Task<AbsencePlanDto> PlanAsync(int id, CancellationToken ct)
    {
        var absence = await OwnAsync(id, ct);
        var decisions = await db.AbsenceTasks
            .Where(h => h.AbsenceId == id && h.ReturnedAt == null)
            .ToDictionaryAsync(h => h.TaskId, h => h.Decision, ct);
        var decided = decisions.Keys.ToList();
        var open = Values.Open;
        var candidates = Candidates(absence.Until).Select(t => t.Id);

        var items = await db.Tasks.Include(t => t.Assignee)
            .Where(t => t.Status == open && (candidates.Contains(t.Id) || decided.Contains(t.Id)))
            .OrderBy(t => t.DueDate != null ? t.DueDate : t.SnoozedUntil)
            .ThenBy(t => t.Title)
            .Take(300)
            .ToListAsync(ct);

        var ids = items.Select(t => t.Id).ToList();
        var visible = (await tasks.Visible(db.Tasks).Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        return new AbsencePlanDto(
            await ToDtoAsync(absence, ct),
            items.Select(t => ToItem(t, decisions.GetValueOrDefault(t.Id), visible.Contains(t.Id))).ToList());
    }

    /// <summary>„Was ist passiert?“ – übergebene Aufgaben mit Stand und Neues an den eigenen Aufgaben im Urlaub.</summary>
    public async Task<AbsenceSummaryDto> SummaryAsync(int id, CancellationToken ct)
    {
        var absence = await OwnAsync(id, ct);
        var u = me.Id;
        var open = Values.Open;
        var rows = await db.AbsenceTasks.Where(h => h.AbsenceId == id).ToListAsync(ct);
        var handed = rows.Where(h => h.Decision == AbsenceTask.HandedOver).ToDictionary(h => h.TaskId);
        var handedIds = handed.Keys.ToList();
        var active = handed.Values.Where(h => h.ReturnedAt == null).Select(h => h.TaskId).ToList();

        var handedTasks = await db.Tasks.Include(t => t.Assignee).Include(t => t.CompletedBy)
            .Where(t => handedIds.Contains(t.Id))
            .OrderBy(t => t.Title)
            .ToListAsync(ct);
        var visible = (await tasks.Visible(db.Tasks).Where(t => handedIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var handovers = handedTasks.Select(t => new HandoverStateDto(
            t.Id,
            t.Title,
            t.Status,
            UserRef.From(t.Assignee),
            t.CompletedBy is null ? null : UserRef.From(t.CompletedBy),
            t.CompletedAt,
            t.Status == open && t.AssigneeId == absence.DeputyId && handed[t.Id].ReturnedAt == null,
            visible.Contains(t.Id))).ToList();

        // Neues an den eigenen Aufgaben – und an übergebenen, solange sie bei der Vertretung liegen.
        // Was die Vertretung weitergegeben hat, gehört nicht mehr dazu (Sichtbarkeit wie sonst auch).
        var start = clock.StartOfDayUtc(absence.From);
        var end = absence.ClosedAt ?? clock.StartOfDayUtc(ReturnDay(absence.Until));
        var deputyId = absence.DeputyId;
        var related = db.Tasks.Where(t => t.CreatedById == u || t.AssigneeId == u || (active.Contains(t.Id) && t.AssigneeId == deputyId));
        var events = await (
                from e in db.TaskEvents
                join t in related on e.TaskId equals t.Id
                where e.CreatedAt >= start && e.CreatedAt < end && e.ActorId != u
                orderby e.CreatedAt descending, e.Id descending
                select new { e.Id, e.TaskId, t.Title, e.Kind, e.Text, e.Actor, e.CreatedAt })
            .Take(MaxEvents)
            .ToListAsync(ct);

        return new AbsenceSummaryDto(
            await ToDtoAsync(absence, ct),
            handovers,
            rows.Count(h => h.Decision == AbsenceTask.Kept),
            events.Select(e => new SummaryEventDto(e.Id, e.TaskId, e.Title, e.Kind, e.Text, e.Actor is null ? null : UserRef.From(e.Actor), e.CreatedAt)).ToList());
    }

    // ---------------------------------------------------------------- Urlaub eintragen, beenden, löschen

    public async Task<AbsenceDto> CreateAsync(AbsenceRequest req, CancellationToken ct)
    {
        var today = clock.Today;
        var from = TaskService.ParseDate(req.From) ?? throw ApiException.Invalid("Bitte den ersten Urlaubstag angeben.");
        var until = TaskService.ParseDate(req.Until) ?? throw ApiException.Invalid("Bitte den letzten Urlaubstag angeben.");
        if (until < from)
        {
            throw ApiException.Invalid("Der letzte Urlaubstag liegt vor dem ersten.");
        }
        if (until < today)
        {
            throw ApiException.Invalid("Dieser Urlaub ist schon vorbei.");
        }
        if (from > today.AddDays(MaxDays) || until.DayNumber - from.DayNumber >= MaxDays)
        {
            throw ApiException.Invalid("Höchstens ein Jahr im Voraus und höchstens ein Jahr am Stück.");
        }
        var deputyId = req.DeputyId ?? throw ApiException.Invalid("Bitte eine Vertretung auswählen.");
        if (deputyId == me.Id)
        {
            throw ApiException.Invalid("Die Vertretung muss jemand anderes sein.");
        }
        var deputy = await db.Users.FirstOrDefaultAsync(x => x.Id == deputyId && x.IsActive, ct)
                     ?? throw ApiException.Invalid("Diese Person gibt es nicht oder sie ist nicht aktiv.");

        var u = me.Id;
        var existing = await db.Absences.FirstOrDefaultAsync(a => a.UserId == u && a.ClosedAt == null && a.Until >= today, ct);
        if (existing is not null)
        {
            throw ApiException.Invalid($"Es ist schon ein Urlaub eingetragen ({existing.From:dd.MM.}–{existing.Until:dd.MM.yyyy}). Bitte erst diesen beenden oder löschen.");
        }
        if (await db.Absences.AnyAsync(a => a.UserId == deputyId && a.ClosedAt == null && a.From <= until && a.Until >= from, ct))
        {
            throw ApiException.Invalid($"{deputy.DisplayName} ist in dieser Zeit selbst abwesend – bitte eine andere Vertretung wählen.");
        }

        var absence = new Absence { UserId = u, From = from, Until = until, DeputyId = deputy.Id, Deputy = deputy, CreatedAt = clock.UtcNow };
        db.Absences.Add(absence);
        await db.SaveChangesAsync(ct);
        PublishAbsence(absence);
        return ToDto(absence, 0, 0);
    }

    /// <summary>
    /// Vorzeitig zurück: Ab jetzt gilt die Person als zurück, Behaltenes ist sofort wieder da. Übergebenes
    /// bleibt bei der Vertretung, bis es mit „Zurück an mich“ geholt wird.
    /// </summary>
    public async Task<AbsenceDto> EndAsync(int id, CancellationToken ct)
    {
        var absence = await OwnCurrentAsync(id, ct);
        if (absence.From > clock.Today)
        {
            throw ApiException.Invalid("Der Urlaub hat noch nicht angefangen – zum Absagen bitte löschen.");
        }
        var changed = await UndoKeptAsync(absence, toMyDay: true, ct);
        absence.ClosedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        foreach (var task in changed)
        {
            await tasks.PublishAsync(task, ct);
        }
        PublishAbsence(absence);
        return await ToDtoAsync(absence, ct);
    }

    /// <summary>
    /// Urlaub absagen: Was noch bei der Vertretung liegt, kommt zurück, Zurückgestelltes ist wieder da wie vorher.
    /// Was die Vertretung schon erledigt hat, bleibt erledigt.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var absence = await OwnCurrentAsync(id, ct);
        var started = absence.From <= clock.Today;
        var changed = await UndoKeptAsync(absence, toMyDay: started, ct);
        var back = await HandedTasksAsync(absence, null, ct);
        foreach (var (task, row) in back)
        {
            TakeBack(absence, task, row);
        }
        db.Absences.Remove(absence);
        await db.SaveChangesAsync(ct);
        foreach (var task in changed)
        {
            await tasks.PublishAsync(task, ct);
        }
        foreach (var (task, _) in back)
        {
            await tasks.PublishAsync(task, ct, absence.DeputyId);
        }
        PublishAbsence(absence);
    }

    /// <summary>„Was ist passiert?“ schließen.</summary>
    public async Task CloseSummaryAsync(int id, CancellationToken ct)
    {
        var absence = await OwnAsync(id, ct);
        absence.SummaryClosedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
        hub.Publish([me.Id], new { type = "absences" });
    }

    // ---------------------------------------------------------------- Aufgaben übergeben oder behalten

    /// <summary>Eine Aufgabe übergeben (mit optionalem Hinweis als Kommentar) oder behalten.</summary>
    public async Task<AbsenceItemDto> DecideAsync(int id, int taskId, AbsenceDecisionRequest req, CancellationToken ct)
    {
        var absence = await OwnCurrentAsync(id, ct);
        var note = (req.Note ?? "").Trim();
        if (note.Length > MaxNote)
        {
            throw ApiException.Invalid($"Der Hinweis darf höchstens {MaxNote} Zeichen haben.");
        }
        var task = await LoadTaskAsync(absence, taskId, ct);
        var row = await db.AbsenceTasks.FirstOrDefaultAsync(h => h.AbsenceId == id && h.TaskId == taskId, ct);
        var previousAssignee = task.AssigneeId;

        switch (req.Decision)
        {
            case AbsenceTask.HandedOver:
                if (task.AssigneeId == me.Id)
                {
                    var deputy = await ActiveDeputyAsync(absence, ct);
                    if (row is { Decision: AbsenceTask.Kept, ReturnedAt: null })
                    {
                        UndoKeep(absence, task, row, toMyDay: false);
                    }
                    row = HandOver(absence, deputy, task, row, note);
                    outbox.Enqueue(deputy.Id, NotificationOutbox.Assigned, task.Id, task.Title, HandoverText(absence, note), me.User.DisplayName);
                }
                break;
            case AbsenceTask.Kept:
                if (task.AssigneeId != me.Id)
                {
                    TakeBack(absence, task, row!);
                }
                if (row is not { Decision: AbsenceTask.Kept, ReturnedAt: null })
                {
                    row = Keep(absence, task, row);
                }
                break;
            default:
                throw ApiException.Invalid("Entscheidung: uebergeben oder behalten.");
        }

        await db.SaveChangesAsync(ct);
        await tasks.PublishAsync(task, ct, previousAssignee);
        PublishAbsence(absence);
        var canOpen = await tasks.Visible(db.Tasks).AnyAsync(t => t.Id == task.Id, ct);
        return ToItem(task, row?.Decision, canOpen);
    }

    /// <summary>„Alle übergeben“: alles, was noch nicht entschieden ist – mit einer einzigen Benachrichtigung.</summary>
    public async Task<AbsenceCountDto> HandOverAllAsync(int id, CancellationToken ct)
    {
        var absence = await OwnCurrentAsync(id, ct);
        var deputy = await ActiveDeputyAsync(absence, ct);
        var decided = db.AbsenceTasks.Where(h => h.AbsenceId == id && h.ReturnedAt == null).Select(h => h.TaskId);
        var open = await Candidates(absence.Until).Include(t => t.Assignee)
            .Where(t => !decided.Contains(t.Id))
            .OrderBy(t => t.DueDate != null ? t.DueDate : t.SnoozedUntil).ThenBy(t => t.Title)
            .Take(300)
            .ToListAsync(ct);
        if (open.Count == 0)
        {
            return new AbsenceCountDto(0);
        }
        var ids = open.Select(t => t.Id).ToList();
        var rows = await db.AbsenceTasks.Where(h => h.AbsenceId == id && ids.Contains(h.TaskId)).ToDictionaryAsync(h => h.TaskId, ct);
        foreach (var task in open)
        {
            HandOver(absence, deputy, task, rows.GetValueOrDefault(task.Id), "");
        }
        if (open.Count == 1)
        {
            outbox.Enqueue(deputy.Id, NotificationOutbox.Assigned, open[0].Id, open[0].Title, HandoverText(absence, ""), me.User.DisplayName);
        }
        else
        {
            var list = string.Join("\n", open.Take(25).Select(t => "– " + t.Title)) + (open.Count > 25 ? $"\n… und {open.Count - 25} weitere" : "");
            outbox.Enqueue(deputy.Id, NotificationOutbox.Handover, null, $"{open.Count} Aufgaben für die Urlaubsvertretung",
                HandoverText(absence, "") + "\n\n" + list, me.User.DisplayName);
        }
        await db.SaveChangesAsync(ct);
        foreach (var task in open)
        {
            await tasks.PublishAsync(task, ct, me.Id);
        }
        PublishAbsence(absence);
        return new AbsenceCountDto(open.Count);
    }

    /// <summary>„Zurück an mich“: einzelne oder (ohne Auswahl) alle Aufgaben, die noch bei der Vertretung liegen.</summary>
    public async Task<AbsenceCountDto> TakeBackAsync(int id, AbsenceTaskIdsRequest req, CancellationToken ct)
    {
        var absence = await OwnAsync(id, ct);
        var back = await HandedTasksAsync(absence, req.TaskIds is { Count: > 0 } chosen ? chosen : null, ct);
        foreach (var (task, row) in back)
        {
            TakeBack(absence, task, row);
        }
        await db.SaveChangesAsync(ct);
        foreach (var (task, _) in back)
        {
            await tasks.PublishAsync(task, ct, absence.DeputyId);
        }
        PublishAbsence(absence);
        return new AbsenceCountDto(back.Count);
    }

    // ---------------------------------------------------------------- Hilfen

    private AbsenceTask HandOver(Absence absence, User deputy, TaskItem task, AbsenceTask? row, string note)
    {
        var today = clock.Today;
        task.AssigneeId = deputy.Id;
        task.Assignee = deputy;
        // „Warte auf …“ übernimmt die Vertretung samt Tag zum Nachhaken – sie bekommt dann die Erinnerung.
        // Sonst war Zurückstellen die Planung der abwesenden Person.
        if (task.WaitingFor == "")
        {
            task.SnoozedUntil = null;
        }
        // „Mein Tag“ plant jede Person selbst; nur der Tag zum Nachhaken bleibt.
        task.MyDayOn = task.WaitingFor != "" && task.SnoozedUntil > today && task.MyDayOn == task.SnoozedUntil ? task.MyDayOn : null;
        Touch(task);
        tasks.AddEvent(task.Id, "vertretung", $"an {deputy.DisplayName}, bis {absence.Until:dd.MM.yyyy}");
        if (note != "")
        {
            tasks.AddEvent(task.Id, "kommentar", note);
        }
        return Mark(absence, task.Id, row, AbsenceTask.HandedOver);
    }

    /// <summary>Behalten: bis zum ersten Arbeitstag nach dem Urlaub zurückstellen (wie „Später“).</summary>
    private AbsenceTask Keep(Absence absence, TaskItem task, AbsenceTask? row)
    {
        var returnDay = ReturnDay(absence.Until);
        var previousSnooze = task.SnoozedUntil;
        var previousDue = task.DueDate;
        row = Mark(absence, task.Id, row, AbsenceTask.Kept);
        row.PreviousSnoozedUntil = previousSnooze;
        row.PreviousDueDate = previousDue;

        // Schon später zurückgestellt – oder die Frist ist schon da: Zurückstellen ginge nicht, die Frist geht vor.
        var later = task.SnoozedUntil >= returnDay;
        var dueNow = task.WaitingFor == "" && task.Kind != "wiedervorlage" && task.DueDate <= clock.Today;
        if (!later && !dueNow)
        {
            tasks.ApplyWaitingAndSnooze(task, null, true, returnDay, false);
            if (task.DueDate != previousDue)
            {
                tasks.AddEvent(task.Id, "termin", $"fällig am {task.DueDate:dd.MM.yyyy}");
            }
            Touch(task);
        }
        return row;
    }

    /// <summary>Behalten rückgängig machen – nur, solange noch der Tag gilt, den die Übergabe gesetzt hat.</summary>
    private bool UndoKeep(Absence absence, TaskItem task, AbsenceTask row, bool toMyDay)
    {
        var returnDay = ReturnDay(absence.Until);
        row.ReturnedAt = clock.UtcNow;
        if (task.SnoozedUntil != returnDay || task.IsDone)
        {
            return false;
        }
        var today = clock.Today;
        // Bei „Warte auf …“ bleibt ein schon erreichter Tag zum Nachhaken stehen – dann ist Nachhaken dran.
        task.SnoozedUntil = row.PreviousSnoozedUntil is { } previous && (previous > today || task.WaitingFor != "") ? previous : null;
        if (task.Kind == "wiedervorlage" && task.DueDate == returnDay && row.PreviousDueDate is { } due && due != returnDay)
        {
            task.DueDate = due;
            tasks.AddEvent(task.Id, "termin", $"fällig am {due:dd.MM.yyyy}");
        }
        task.MyDayOn = task.SnoozedUntil > today ? task.SnoozedUntil : toMyDay ? today : null;
        task.FollowUpNotifiedAt = null;
        tasks.AddEvent(task.Id, "zurueckgeholt", "");
        Touch(task);
        return true;
    }

    private async Task<List<TaskItem>> UndoKeptAsync(Absence absence, bool toMyDay, CancellationToken ct)
    {
        var rows = await db.AbsenceTasks
            .Where(h => h.AbsenceId == absence.Id && h.Decision == AbsenceTask.Kept && h.ReturnedAt == null)
            .ToListAsync(ct);
        var ids = rows.Select(h => h.TaskId).ToList();
        var u = me.Id;
        var kept = await db.Tasks.Include(t => t.Assignee).Where(t => ids.Contains(t.Id) && t.AssigneeId == u).ToDictionaryAsync(t => t.Id, ct);
        var changed = new List<TaskItem>();
        foreach (var row in rows)
        {
            if (kept.TryGetValue(row.TaskId, out var task) && UndoKeep(absence, task, row, toMyDay))
            {
                changed.Add(task);
            }
            else
            {
                row.ReturnedAt = clock.UtcNow;
            }
        }
        return changed;
    }

    /// <summary>Zurück an die abwesende Person. Wer auf etwas wartet, wartet weiter – sonst steht sie heute in „Mein Tag“.</summary>
    private void TakeBack(Absence absence, TaskItem task, AbsenceTask row)
    {
        var today = clock.Today;
        task.AssigneeId = me.Id;
        task.Assignee = me.User;
        if (task.WaitingFor == "")
        {
            task.SnoozedUntil = null;
        }
        task.MyDayOn = task.SnoozedUntil > today ? task.SnoozedUntil : today;
        Touch(task);
        tasks.AddEvent(task.Id, "vertretung-zurueck", $"von {absence.Deputy.DisplayName}");
        row.ReturnedAt = clock.UtcNow;
    }

    /// <summary>Übergebene Aufgaben, die noch offen bei der Vertretung liegen (optional nur bestimmte).</summary>
    private async Task<List<(TaskItem Task, AbsenceTask Row)>> HandedTasksAsync(Absence absence, List<int>? only, CancellationToken ct)
    {
        var rows = await db.AbsenceTasks
            .Where(h => h.AbsenceId == absence.Id && h.Decision == AbsenceTask.HandedOver && h.ReturnedAt == null)
            .ToDictionaryAsync(h => h.TaskId, ct);
        var ids = rows.Keys.Where(id => only is null || only.Contains(id)).ToList();
        var open = Values.Open;
        var deputyId = absence.DeputyId;
        var list = await db.Tasks.Include(t => t.Assignee)
            .Where(t => ids.Contains(t.Id) && t.Status == open && t.AssigneeId == deputyId)
            .ToListAsync(ct);
        return list.Select(t => (t, rows[t.Id])).ToList();
    }

    /// <summary>Eigene offene Aufgabe – oder eine, die in diesem Urlaub übergeben wurde und noch bei der Vertretung liegt.</summary>
    private async Task<TaskItem> LoadTaskAsync(Absence absence, int taskId, CancellationToken ct)
    {
        var u = me.Id;
        var open = Values.Open;
        var deputyId = absence.DeputyId;
        var handedOver = AbsenceTask.HandedOver;
        var task = await db.Tasks.Include(t => t.Assignee)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.Status == open
                                      && (t.AssigneeId == u
                                          || (t.AssigneeId == deputyId && db.AbsenceTasks.Any(h => h.AbsenceId == absence.Id && h.TaskId == t.Id
                                                                                                 && h.Decision == handedOver && h.ReturnedAt == null))), ct);
        return task ?? throw ApiException.NotFound("Offene Aufgabe");
    }

    private AbsenceTask Mark(Absence absence, int taskId, AbsenceTask? row, string decision)
    {
        if (row is null)
        {
            row = new AbsenceTask { AbsenceId = absence.Id, TaskId = taskId, Decision = decision };
            db.AbsenceTasks.Add(row);
        }
        row.Decision = decision;
        row.DecidedAt = clock.UtcNow;
        row.ReturnedAt = null;
        return row;
    }

    private void Touch(TaskItem task)
    {
        task.Version++;
        task.UpdatedAt = clock.UtcNow;
    }

    private async Task<User> ActiveDeputyAsync(Absence absence, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(x => x.Id == absence.DeputyId && x.IsActive, ct)
        ?? throw ApiException.Invalid("Die Vertretung ist nicht mehr aktiv – bitte den Urlaub löschen und mit einer anderen Vertretung neu eintragen.");

    private string HandoverText(Absence absence, string note) =>
        $"Urlaubsvertretung für {me.User.DisplayName} vom {absence.From:dd.MM.} bis {absence.Until:dd.MM.yyyy}." + (note == "" ? "" : "\n\n" + note);

    private async Task<Absence> OwnAsync(int id, CancellationToken ct)
    {
        var u = me.Id;
        return await db.Absences.Include(a => a.Deputy).FirstOrDefaultAsync(a => a.Id == id && a.UserId == u, ct)
               ?? throw ApiException.NotFound("Urlaub");
    }

    /// <summary>Eingetragen oder laufend – nicht vorbei.</summary>
    private async Task<Absence> OwnCurrentAsync(int id, CancellationToken ct)
    {
        var absence = await OwnAsync(id, ct);
        return absence.ClosedAt is null && absence.Until >= clock.Today
            ? absence
            : throw ApiException.Invalid("Dieser Urlaub ist schon vorbei.");
    }

    private static bool IsBack(Absence a, DateOnly today) => a.ClosedAt is not null || today > a.Until;

    /// <summary>Erster Tag zurück: nach dem letzten Urlaubstag oder – vorzeitig beendet – der Tag des Endes.</summary>
    private DateOnly BackOn(Absence a) =>
        a.ClosedAt is { } closed ? DateOnly.FromDateTime(clock.ToLocal(closed)) : a.Until.AddDays(1);

    private void PublishAbsence(Absence absence) => hub.Publish([absence.UserId, absence.DeputyId], new { type = "absences" });

    private async Task<AbsenceDto> ToDtoAsync(Absence a, CancellationToken ct)
    {
        var counts = await db.AbsenceTasks
            .Where(h => h.AbsenceId == a.Id && h.ReturnedAt == null && db.Tasks.Any(t => t.Id == h.TaskId))
            .GroupBy(h => h.Decision)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return ToDto(a, counts.GetValueOrDefault(AbsenceTask.HandedOver), counts.GetValueOrDefault(AbsenceTask.Kept));
    }

    private AbsenceDto ToDto(Absence a, int handedOver, int kept)
    {
        var today = clock.Today;
        return new AbsenceDto(a.Id, Iso(a.From), Iso(a.Until), Iso(ReturnDay(a.Until)), UserRef.From(a.Deputy),
            a.From <= today, IsBack(a, today), handedOver, kept);
    }

    private static AbsenceItemDto ToItem(TaskItem t, string? decision, bool canOpen) => new(
        t.Id,
        t.Title,
        t.Kind,
        t.DueDate is { } due ? Iso(due) : null,
        t.WaitingFor,
        t.WaitingFor != "" && t.SnoozedUntil is { } followUp ? Iso(followUp) : null,
        decision,
        UserRef.From(t.Assignee),
        canOpen);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
