using System.Globalization;
using System.Text.Json;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Services;

/// <summary>
/// Fachlogik für Aufgaben.
///
/// Sichtbarkeit: Eine Aufgabe sehen Ersteller und verantwortliche Person.
/// Aufgaben mit Projekt sehen alle angemeldeten Personen (Projektboard im Teams-Kanal).
/// Verwaltungsrechte erweitern die Sichtbarkeit privater Aufgaben ausdrücklich NICHT.
/// </summary>
public sealed class TaskService(
    AppDbContext db,
    CurrentUser me,
    AppClock clock,
    RealtimeHub hub,
    NotificationOutbox outbox)
{
    public const int MaxTitle = 500;
    public const int MaxNotes = 20000;
    public const int MaxChecklist = 100;

    public static readonly string[] Views =
        ["mein-tag", "wichtig", "geplant", "zugewiesen", "delegiert", "wiedervorlagen", "liste", "projekt", "erledigt", "suche"];

    private IQueryable<TaskItem> Base() => db.Tasks
        .Include(t => t.CreatedBy)
        .Include(t => t.Assignee)
        .Include(t => t.CompletedBy)
        .Include(t => t.Project)
        .Include(t => t.Checklist)
        .AsSplitQuery();

    private IQueryable<TaskItem> Visible(IQueryable<TaskItem> q)
    {
        var u = me.Id;
        return q.Where(t => t.CreatedById == u || t.AssigneeId == u || t.ProjectId != null);
    }

    // ---------------------------------------------------------------- Lesen

    public async Task<List<TaskDto>> ListAsync(string view, int? id, string? search, CancellationToken ct)
    {
        var u = me.Id;
        var today = clock.Today;
        var startToday = clock.StartOfDayUtc(today);
        var recent = clock.UtcNow.AddDays(-7);
        var month = clock.UtcNow.AddDays(-30);
        var open = Values.Open;
        var done = Values.Done;

        var q = Base();
        switch (view)
        {
            case "mein-tag":
                q = q.Where(t => t.AssigneeId == u && (
                    (t.Status == open && (t.MyDayOn == today || (t.DueDate != null && t.DueDate <= today))) ||
                    (t.Status == done && t.CompletedAt >= startToday && (t.MyDayOn == today || (t.DueDate != null && t.DueDate <= today)))));
                break;
            case "wichtig":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.Priority == "hoch");
                break;
            case "geplant":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.DueDate != null);
                break;
            case "zugewiesen":
                q = q.Where(t => t.AssigneeId == u && t.CreatedById != u && (t.Status == open || t.CompletedAt >= recent));
                break;
            case "delegiert":
                q = q.Where(t => t.CreatedById == u && t.AssigneeId != u && (t.Status == open || t.CompletedAt >= recent));
                break;
            case "wiedervorlagen":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.Kind == "wiedervorlage");
                break;
            case "liste":
                var listId = id ?? throw ApiException.Invalid("Liste fehlt.");
                if (!await db.TaskLists.AnyAsync(l => l.Id == listId && l.OwnerId == u, ct))
                {
                    throw ApiException.NotFound("Liste");
                }
                q = q.Where(t => t.ListId == listId && (t.AssigneeId == u || t.CreatedById == u) && (t.Status == open || t.CompletedAt >= recent));
                break;
            case "projekt":
                var projectId = id ?? throw ApiException.Invalid("Projekt fehlt.");
                q = q.Where(t => t.ProjectId == projectId && (t.Status == open || t.CompletedAt >= month));
                break;
            case "erledigt":
                q = q.Where(t => (t.AssigneeId == u || t.CreatedById == u) && t.Status == done && t.CompletedAt >= month);
                break;
            case "suche":
                q = Visible(q);
                break;
            default:
                throw ApiException.Invalid("Unbekannte Ansicht.");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            q = q.Where(t => EF.Functions.ILike(t.Title, pattern, "\\") || EF.Functions.ILike(t.Notes, pattern, "\\"));
        }
        else if (view == "suche")
        {
            return [];
        }

        IOrderedQueryable<TaskItem> ordered = view == "erledigt"
            ? q.OrderByDescending(t => t.CompletedAt)
            : q.OrderBy(t => t.Status == done ? 1 : 0)
                .ThenBy(t => t.DueDate == null ? 1 : 0)
                .ThenBy(t => t.DueDate)
                .ThenBy(t => t.DueTime)
                .ThenBy(t => t.Priority == "hoch" ? 0 : t.Priority == "normal" ? 1 : 2)
                .ThenByDescending(t => t.CreatedAt);

        var items = await ordered.Take(500).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<CountsDto> CountsAsync(CancellationToken ct)
    {
        var u = me.Id;
        var today = clock.Today;
        var open = Values.Open;
        var mine = db.Tasks.Where(t => t.AssigneeId == u && t.Status == open);

        var myDay = await mine.CountAsync(t => t.MyDayOn == today || (t.DueDate != null && t.DueDate <= today), ct);
        var important = await mine.CountAsync(t => t.Priority == "hoch", ct);
        var planned = await mine.CountAsync(t => t.DueDate != null, ct);
        var assigned = await mine.CountAsync(t => t.CreatedById != u, ct);
        var followUps = await mine.CountAsync(t => t.Kind == "wiedervorlage", ct);
        var overdue = await mine.CountAsync(t => t.DueDate != null && t.DueDate < today, ct);
        var delegated = await db.Tasks.CountAsync(t => t.CreatedById == u && t.AssigneeId != u && t.Status == open, ct);

        var lists = await db.Tasks
            .Where(t => t.ListId != null && t.Status == open && (t.AssigneeId == u || t.CreatedById == u))
            .GroupBy(t => t.ListId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var projects = await db.Tasks
            .Where(t => t.ProjectId != null && t.Status == open)
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new CountsDto(myDay, important, planned, assigned, delegated, followUps, overdue, lists, projects);
    }

    public async Task<TaskDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        var events = await db.TaskEvents
            .Include(e => e.Actor)
            .Where(e => e.TaskId == id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Take(300)
            .ToListAsync(ct);
        return new TaskDetailDto(ToDto(task), events.Select(e => new EventDto(
            e.Id, e.Kind, e.Text, e.Actor is null ? null : UserRef.From(e.Actor), e.CreatedAt)).ToList());
    }

    // ---------------------------------------------------------------- Schreiben

    public async Task<TaskDto> CreateAsync(CreateTaskRequest req, string source, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var assignee = req.AssigneeId is int aid && aid != me.Id ? await ActiveUserAsync(aid, ct) : me.User;
        var task = new TaskItem
        {
            Title = ValidTitle(req.Title),
            Notes = ValidNotes(req.Notes ?? ""),
            Priority = ValidPriority(req.Priority ?? "normal"),
            Kind = ValidKind(req.Kind ?? "aufgabe"),
            Recurrence = ValidRecurrence(req.Recurrence ?? ""),
            CreatedById = me.Id,
            AssigneeId = assignee.Id,
            ListId = req.ListId is int lid ? await OwnListAsync(lid, ct) : null,
            ProjectId = req.ProjectId is int pid ? await OpenProjectAsync(pid, ct) : null,
            DueDate = ParseDate(req.DueDate),
            DueTime = ParseTime(req.DueTime),
            RemindAt = req.RemindAt?.ToUniversalTime(),
            // "Mein Tag" plant die verantwortliche Person selbst – beim Delegieren nicht übernehmen.
            MyDayOn = req.MyDay == true && assignee.Id == me.Id ? clock.Today : null,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (task.DueTime is not null && task.DueDate is null)
        {
            throw ApiException.Invalid("Eine Uhrzeit braucht auch ein Datum.");
        }
        if (task.Recurrence != "" && task.DueDate is null)
        {
            task.DueDate = clock.Today;
        }
        var checklist = (req.Checklist ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxChecklist).ToList();
        for (var i = 0; i < checklist.Count; i++)
        {
            task.Checklist.Add(new ChecklistItem { Title = ValidChecklistTitle(checklist[i]), Position = i });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        AddEvent(task.Id, "angelegt", source == "vorlage" ? "Aus Vorlage angelegt" : "");
        if (assignee.Id != me.Id)
        {
            AddEvent(task.Id, "zugewiesen", $"an {assignee.DisplayName}");
            outbox.Enqueue(assignee.Id, NotificationOutbox.Assigned, task.Id, task.Title, task.Notes, me.User.DisplayName);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        task.CreatedBy = me.User;
        task.Assignee = assignee;
        if (task.ProjectId is int p)
        {
            task.Project = await db.Projects.FindAsync([p], ct);
        }
        Publish(task);
        return ToDto(task);
    }

    public async Task<TaskDto> PatchAsync(int id, JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw ApiException.Invalid("Erwartet wird ein JSON-Objekt.");
        }
        var task = await LoadVisibleAsync(id, ct);
        var previousAssignee = task.AssigneeId;
        var previousDue = task.DueDate;

        if (body.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() != task.Version)
        {
            throw ApiException.Conflict("Die Aufgabe wurde inzwischen geändert. Bitte neu laden.");
        }

        User? newAssignee = null;
        foreach (var prop in body.EnumerateObject())
        {
            var v = prop.Value;
            var isNull = v.ValueKind == JsonValueKind.Null;
            switch (prop.Name)
            {
                case "version":
                    break;
                case "title":
                    task.Title = ValidTitle(RequireString(v, "Titel"));
                    break;
                case "notes":
                    task.Notes = ValidNotes(isNull ? "" : RequireString(v, "Notizen"));
                    break;
                case "priority":
                    task.Priority = ValidPriority(RequireString(v, "Priorität"));
                    break;
                case "kind":
                    task.Kind = ValidKind(RequireString(v, "Art"));
                    break;
                case "recurrence":
                    task.Recurrence = ValidRecurrence(isNull ? "" : RequireString(v, "Wiederholung"));
                    break;
                case "dueDate":
                    task.DueDate = isNull ? null : ParseDate(RequireString(v, "Fälligkeit"));
                    if (task.DueDate is null)
                    {
                        task.DueTime = null;
                    }
                    break;
                case "dueTime":
                    task.DueTime = isNull ? null : ParseTime(RequireString(v, "Uhrzeit"));
                    break;
                case "remindAt":
                    task.RemindAt = isNull ? null : v.GetDateTimeOffset().ToUniversalTime();
                    task.RemindedAt = null;
                    break;
                case "myDay":
                    task.MyDayOn = v.ValueKind == JsonValueKind.True ? clock.Today : null;
                    break;
                case "listId":
                    task.ListId = isNull ? null : await OwnListAsync(v.GetInt32(), ct);
                    break;
                case "projectId":
                    task.ProjectId = isNull ? null : await OpenProjectAsync(v.GetInt32(), ct);
                    break;
                case "assigneeId":
                    newAssignee = await ActiveUserAsync(v.GetInt32(), ct);
                    task.AssigneeId = newAssignee.Id;
                    break;
                default:
                    throw ApiException.Invalid($"Unbekanntes Feld: {prop.Name}");
            }
        }

        if (task.DueTime is not null && task.DueDate is null)
        {
            throw ApiException.Invalid("Eine Uhrzeit braucht auch ein Datum.");
        }
        if (task.Recurrence != "" && task.DueDate is null)
        {
            task.DueDate = clock.Today;
        }

        task.Version++;
        task.UpdatedAt = clock.UtcNow;

        if (newAssignee is not null && newAssignee.Id != previousAssignee)
        {
            AddEvent(task.Id, "zugewiesen", $"an {newAssignee.DisplayName}");
            if (newAssignee.Id != me.Id)
            {
                outbox.Enqueue(newAssignee.Id, NotificationOutbox.Assigned, task.Id, task.Title, task.Notes, me.User.DisplayName);
            }
            task.Assignee = newAssignee;
        }
        if (task.DueDate != previousDue)
        {
            AddEvent(task.Id, "termin", task.DueDate is { } d ? $"fällig am {d:dd.MM.yyyy}" : "Fälligkeit entfernt");
        }

        await db.SaveChangesAsync(ct);
        if (task.ProjectId is int p && task.Project?.Id != p)
        {
            task.Project = await db.Projects.FindAsync([p], ct);
        }
        else if (task.ProjectId is null)
        {
            task.Project = null;
        }
        Publish(task, previousAssignee);
        return ToDto(task);
    }

    public async Task<TaskDto> CompleteAsync(int id, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        if (task.IsDone)
        {
            return ToDto(task);
        }

        var now = clock.UtcNow;
        task.Status = Values.Done;
        task.CompletedAt = now;
        task.CompletedById = me.Id;
        task.CompletedBy = me.User;
        task.Version++;
        task.UpdatedAt = now;
        AddEvent(task.Id, "erledigt", "");

        if (task.CreatedById != me.Id)
        {
            outbox.Enqueue(task.CreatedById, NotificationOutbox.Completed, task.Id, task.Title, "", me.User.DisplayName);
        }

        TaskItem? next = null;
        if (task.Recurrence != "")
        {
            next = BuildNextOccurrence(task);
            db.Tasks.Add(next);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (next is not null)
        {
            AddEvent(next.Id, "angelegt", $"Wiederholung ({Recurrence.Label(task.Recurrence)})");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);

        Publish(task);
        if (next is not null)
        {
            Publish(next);
        }
        return ToDto(task);
    }

    public async Task<TaskDto> ReopenAsync(int id, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        if (!task.IsDone)
        {
            return ToDto(task);
        }
        task.Status = Values.Open;
        task.CompletedAt = null;
        task.CompletedById = null;
        task.CompletedBy = null;
        task.Version++;
        task.UpdatedAt = clock.UtcNow;
        AddEvent(task.Id, "wieder-geoeffnet", "");
        await db.SaveChangesAsync(ct);
        Publish(task);
        return ToDto(task);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        if (!CanDelete(task))
        {
            throw ApiException.Forbidden("Nur die Person, die die Aufgabe angelegt hat, kann sie löschen.");
        }
        var targets = new[] { task.CreatedById, task.AssigneeId };
        var isProject = task.ProjectId is not null;
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
        var message = new { type = "task-deleted", id };
        if (isProject)
        {
            hub.Broadcast(message);
        }
        else
        {
            hub.Publish(targets, message);
        }
    }

    public async Task<TaskDto> AddChecklistItemAsync(int id, ChecklistRequest req, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        if (task.Checklist.Count >= MaxChecklist)
        {
            throw ApiException.Invalid($"Höchstens {MaxChecklist} Punkte pro Checkliste.");
        }
        task.Checklist.Add(new ChecklistItem
        {
            Title = ValidChecklistTitle(req.Title ?? ""),
            Position = task.Checklist.Count == 0 ? 0 : task.Checklist.Max(c => c.Position) + 1,
        });
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        Publish(task);
        return ToDto(task);
    }

    public async Task<TaskDto> UpdateChecklistItemAsync(int id, int itemId, ChecklistRequest req, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        var item = task.Checklist.FirstOrDefault(c => c.Id == itemId) ?? throw ApiException.NotFound("Checklistenpunkt");
        if (req.Title is not null)
        {
            item.Title = ValidChecklistTitle(req.Title);
        }
        if (req.Done is bool doneValue)
        {
            item.Done = doneValue;
        }
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        Publish(task);
        return ToDto(task);
    }

    public async Task<TaskDto> DeleteChecklistItemAsync(int id, int itemId, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        var item = task.Checklist.FirstOrDefault(c => c.Id == itemId) ?? throw ApiException.NotFound("Checklistenpunkt");
        task.Checklist.Remove(item);
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        Publish(task);
        return ToDto(task);
    }

    public async Task<EventDto> CommentAsync(int id, CommentRequest req, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > 4000)
        {
            throw ApiException.Invalid("Der Kommentar muss zwischen 1 und 4000 Zeichen lang sein.");
        }
        var ev = AddEvent(task.Id, "kommentar", text);
        foreach (var target in new[] { task.CreatedById, task.AssigneeId }.Distinct().Where(x => x != me.Id))
        {
            outbox.Enqueue(target, NotificationOutbox.Commented, task.Id, task.Title, text, me.User.DisplayName);
        }
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        Publish(task);
        return new EventDto(ev.Id, ev.Kind, ev.Text, UserRef.From(me.User), ev.CreatedAt);
    }

    // ---------------------------------------------------------------- Hilfen

    internal TaskItem BuildNextOccurrence(TaskItem task)
    {
        var baseDate = task.DueDate ?? clock.Today;
        var nextDate = Recurrence.NextOnOrAfter(baseDate, task.Recurrence, clock.Today);
        var next = new TaskItem
        {
            Title = task.Title,
            Notes = task.Notes,
            Kind = task.Kind,
            Priority = task.Priority,
            CreatedById = task.CreatedById,
            AssigneeId = task.AssigneeId,
            ListId = task.ListId,
            ProjectId = task.ProjectId,
            DueDate = nextDate,
            DueTime = task.DueTime,
            RemindAt = task.RemindAt?.AddDays(nextDate.DayNumber - baseDate.DayNumber),
            Recurrence = task.Recurrence,
            Source = "wiederholung",
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };
        foreach (var item in task.Checklist.OrderBy(c => c.Position))
        {
            next.Checklist.Add(new ChecklistItem { Title = item.Title, Position = item.Position });
        }
        return next;
    }

    private async Task<TaskItem> LoadVisibleAsync(int id, CancellationToken ct)
    {
        var task = await Base().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw ApiException.NotFound("Aufgabe");
        if (!(task.CreatedById == me.Id || task.AssigneeId == me.Id || task.ProjectId is not null))
        {
            // Bewusst 404 statt 403: private Aufgaben anderer sollen nicht einmal "existieren".
            throw ApiException.NotFound("Aufgabe");
        }
        return task;
    }

    private bool CanDelete(TaskItem t) => t.CreatedById == me.Id || (t.ProjectId is not null && me.IsAdmin);

    private TaskEvent AddEvent(int taskId, string kind, string text)
    {
        var ev = new TaskEvent { TaskId = taskId, ActorId = me.Id, Kind = kind, Text = text, CreatedAt = clock.UtcNow };
        db.TaskEvents.Add(ev);
        return ev;
    }

    private void Publish(TaskItem task, params int[] extraUsers)
    {
        var message = new { type = "task", id = task.Id };
        if (task.ProjectId is not null)
        {
            hub.Broadcast(message);
            return;
        }
        hub.Publish(new[] { task.CreatedById, task.AssigneeId }.Concat(extraUsers), message);
    }

    private async Task<User> ActiveUserAsync(int id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct)
        ?? throw ApiException.Invalid("Diese Person gibt es nicht oder sie ist nicht aktiv.");

    private async Task<int> OwnListAsync(int id, CancellationToken ct) =>
        await db.TaskLists.AnyAsync(l => l.Id == id && l.OwnerId == me.Id, ct) ? id : throw ApiException.Invalid("Diese Liste gehört dir nicht.");

    private async Task<int> OpenProjectAsync(int id, CancellationToken ct) =>
        await db.Projects.AnyAsync(p => p.Id == id && !p.Archived, ct) ? id : throw ApiException.Invalid("Dieses Projekt gibt es nicht oder es ist archiviert.");

    public TaskDto ToDto(TaskItem t)
    {
        var today = clock.Today;
        var nowLocal = TimeOnly.FromDateTime(clock.LocalNow);
        var overdue = !t.IsDone && t.DueDate is { } d &&
                      (d < today || (d == today && t.DueTime is { } time && time < nowLocal));
        return new TaskDto(
            t.Id,
            t.Title,
            t.Notes,
            t.Status,
            t.Kind,
            t.Priority,
            UserRef.From(t.CreatedBy),
            UserRef.From(t.Assignee),
            t.ListId,
            ProjectRef.From(t.Project),
            t.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            t.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            t.RemindAt,
            t.MyDayOn == today,
            t.Recurrence,
            t.CompletedAt,
            t.CompletedBy is null ? null : UserRef.From(t.CompletedBy),
            t.Checklist.OrderBy(c => c.Position).Select(c => new ChecklistDto(c.Id, c.Title, c.Done)).ToList(),
            t.Source,
            t.CreatedAt,
            t.UpdatedAt,
            t.Version,
            t.CreatedById != t.AssigneeId,
            overdue,
            CanDelete(t));
    }

    // ---------------------------------------------------------------- Validierung

    internal static string ValidTitle(string? title)
    {
        var value = (title ?? "").Trim();
        if (value.Length == 0)
        {
            throw ApiException.Invalid("Bitte einen Titel eingeben.");
        }
        return value.Length > MaxTitle ? throw ApiException.Invalid($"Der Titel darf höchstens {MaxTitle} Zeichen haben.") : value;
    }

    private static string ValidNotes(string notes) =>
        notes.Length > MaxNotes ? throw ApiException.Invalid($"Notizen dürfen höchstens {MaxNotes} Zeichen haben.") : notes;

    private static string ValidChecklistTitle(string title)
    {
        var value = title.Trim();
        return value.Length is 0 or > 300 ? throw ApiException.Invalid("Checklistenpunkte brauchen 1 bis 300 Zeichen.") : value;
    }

    internal static string ValidPriority(string v) =>
        Values.Priorities.Contains(v) ? v : throw ApiException.Invalid("Priorität muss niedrig, normal oder hoch sein.");

    internal static string ValidKind(string v) =>
        Values.Kinds.Contains(v) ? v : throw ApiException.Invalid("Art muss aufgabe oder wiedervorlage sein.");

    internal static string ValidRecurrence(string v) =>
        Values.Recurrences.Contains(v) ? v : throw ApiException.Invalid("Unbekannte Wiederholung.");

    internal static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw ApiException.Invalid("Datum bitte im Format JJJJ-MM-TT.");
    }

    internal static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : throw ApiException.Invalid("Uhrzeit bitte im Format HH:MM.");
    }

    private static string RequireString(JsonElement v, string field) =>
        v.ValueKind == JsonValueKind.String ? v.GetString()! : throw ApiException.Invalid($"{field}: Text erwartet.");

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
