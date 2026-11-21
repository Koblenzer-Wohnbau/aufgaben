using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    public const int MaxWaitingFor = 200;

    /// <summary>So lange bleiben gelöschte Aufgaben im Papierkorb, danach löscht der Hintergrunddienst sie endgültig.</summary>
    public const int TrashDays = 30;

    /// <summary>Höchstzahl für Aktionen mit mehreren Aufgaben auf einmal.</summary>
    public const int MaxBulk = 500;

    public static readonly string[] Views =
        ["mein-tag", "aufgaben", "person", "wichtig", "geplant", "zugewiesen", "delegiert", "wiedervorlagen", "warteschleife", "liste", "projekt", "erledigt", "suche", "papierkorb", "vertretung"];

    /// <summary>
    /// Nicht zurückgestellt (oder der Tag ist da) – oder schon fällig: Eine Fälligkeit geht immer vor,
    /// damit keine Frist in der Warteschleife verschwindet.
    /// </summary>
    private static Expression<Func<TaskItem, bool>> Present(DateOnly today) =>
        t => t.SnoozedUntil == null || t.SnoozedUntil <= today || t.DueDate <= today;

    internal IQueryable<TaskItem> Base() => Base(db.Tasks);

    /// <summary>Papierkorb der angemeldeten Person: von ihr gelöscht oder von ihr angelegt.</summary>
    private IQueryable<TaskItem> Trash()
    {
        var u = me.Id;
        return Base(db.Tasks.IgnoreQueryFilters().Include(t => t.DeletedBy))
            .Where(t => t.DeletedAt != null && (t.DeletedById == u || t.CreatedById == u));
    }

    private static IQueryable<TaskItem> Base(IQueryable<TaskItem> tasks) => tasks
        .Include(t => t.CreatedBy)
        .Include(t => t.Assignee)
        .Include(t => t.CompletedBy)
        .Include(t => t.Project)
        .Include(t => t.Checklist)
        .Include(t => t.Attachments) // nur Metadaten, der Inhalt liegt in attachment_blobs
        .AsSplitQuery();

    /// <summary>Eigene Rechte: angelegt, verantwortlich, Projekt oder (geteilte) Liste.</summary>
    private IQueryable<TaskItem> VisibleOwn(IQueryable<TaskItem> q)
    {
        var u = me.Id;
        return q.Where(t => t.CreatedById == u || t.AssigneeId == u || t.ProjectId != null
            || (t.ListId != null && db.TaskLists.Any(l => l.Id == t.ListId && (l.OwnerId == u || l.Members.Any(m => m.UserId == u)))));
    }

    /// <summary>Eigene Rechte plus aktive Einsichtsrechte (für Änderungen nur mit "bearbeiten").</summary>
    internal IQueryable<TaskItem> Visible(IQueryable<TaskItem> q, bool forEdit = false)
    {
        var u = me.Id;
        var today = clock.Today;
        var active = AccessGrant.Active;
        return q.Where(t => t.CreatedById == u || t.AssigneeId == u || t.ProjectId != null
            || (t.ListId != null && db.TaskLists.Any(l => l.Id == t.ListId && (l.OwnerId == u || l.Members.Any(m => m.UserId == u))))
            || db.AccessGrants.Any(g => g.ViewerId == u && g.OwnerId == t.AssigneeId && g.Status == active
                                        && g.ValidFrom <= today && (g.ValidUntil == null || g.ValidUntil >= today)
                                        && (!forEdit || g.Permission == "bearbeiten")));
    }

    private IQueryable<AccessGrant> ActiveGrantsFor(int ownerId)
    {
        var u = me.Id;
        var today = clock.Today;
        return db.AccessGrants.Where(g => g.ViewerId == u && g.OwnerId == ownerId && g.Status == AccessGrant.Active
                                          && g.ValidFrom <= today && (g.ValidUntil == null || g.ValidUntil >= today));
    }

    /// <summary>
    /// Zugriff über ein Einsichtsrecht protokollieren. "angesehen" höchstens alle 10 Minuten je Freigabe,
    /// damit das Protokoll lesbar bleibt; Änderungen immer.
    /// </summary>
    private async Task LogGrantAccessAsync(int ownerId, string action, int? taskId, CancellationToken ct)
    {
        // Gibt es für dieselben Personen eine Freigabe zum Bearbeiten, zählt der Zugriff dort.
        var grant = await ActiveGrantsFor(ownerId)
            .OrderBy(g => g.Permission == "bearbeiten" ? 0 : 1).ThenByDescending(g => g.Id)
            .FirstOrDefaultAsync(ct);
        if (grant is null)
        {
            return;
        }
        var now = clock.UtcNow;
        if (action == "angesehen")
        {
            var since = now.AddMinutes(-10);
            if (await db.AccessLog.AnyAsync(l => l.GrantId == grant.Id && l.ViewerId == me.Id && l.Action == action && l.At >= since, ct))
            {
                return;
            }
        }
        db.AccessLog.Add(new AccessLogEntry { GrantId = grant.Id, ViewerId = me.Id, OwnerId = ownerId, Action = action, TaskId = taskId, At = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Eigene und mit mir geteilte Listen.</summary>
    private IQueryable<TaskList> AccessibleLists()
    {
        var u = me.Id;
        return db.TaskLists.Where(l => l.OwnerId == u || l.Members.Any(m => m.UserId == u));
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
        // Zurückgestellte Aufgaben fehlen nur in den eigenen Ansichten der verantwortlichen Person –
        // wer delegiert hat, im Projekt mitliest oder sucht, sieht sie weiterhin.
        var present = Present(today);
        switch (view)
        {
            case "mein-tag":
                q = q.Where(t => t.AssigneeId == u && (
                    (t.Status == open && (t.MyDayOn == today || (t.DueDate != null && t.DueDate <= today))) ||
                    (t.Status == done && t.CompletedAt >= startToday && (t.MyDayOn == today || (t.DueDate != null && t.DueDate <= today))))).Where(present);
                break;
            case "wichtig":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.Priority == "hoch").Where(present);
                break;
            case "geplant":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.DueDate != null).Where(present);
                break;
            case "zugewiesen":
                q = q.Where(t => t.AssigneeId == u && t.CreatedById != u && (t.Status == open || t.CompletedAt >= recent)).Where(present);
                break;
            case "delegiert":
                q = q.Where(t => t.CreatedById == u && t.AssigneeId != u && (t.Status == open || t.CompletedAt >= recent));
                break;
            case "wiedervorlagen":
                q = q.Where(t => t.AssigneeId == u && t.Status == open && t.Kind == "wiedervorlage").Where(present);
                break;
            case "warteschleife":
                // „Warte auf …“ (auch wenn der Tag zum Nachhaken schon da ist) und Zurückgestelltes
                q = q.Where(t => t.AssigneeId == u && t.Status == open && (t.WaitingFor != "" || t.SnoozedUntil > today));
                break;
            case "aufgaben":
                // Wie "Aufgaben" in To Do: alles, was mir gehört und in keiner Liste steckt.
                q = q.Where(t => t.AssigneeId == u && t.ListId == null && (t.Status == open || t.CompletedAt >= recent)).Where(present);
                break;
            case "liste":
                var listId = id ?? throw ApiException.Invalid("Liste fehlt.");
                if (!await AccessibleLists().AnyAsync(l => l.Id == listId, ct))
                {
                    throw ApiException.NotFound("Liste");
                }
                // Geteilte Liste: alle Aufgaben der Liste, egal wer sie angelegt hat. Zurückgestellt
                // heißt nur für die verantwortliche Person „ausgeblendet“.
                q = q.Where(t => t.ListId == listId && (t.Status == open || t.CompletedAt >= recent))
                     .Where(t => t.AssigneeId != u || t.SnoozedUntil == null || t.SnoozedUntil <= today || t.DueDate <= today);
                break;
            case "person":
                var ownerId = id ?? throw ApiException.Invalid("Person fehlt.");
                if (!await ActiveGrantsFor(ownerId).AnyAsync(ct))
                {
                    throw ApiException.NotFound("Freigabe");
                }
                await LogGrantAccessAsync(ownerId, "angesehen", null, ct);
                q = q.Where(t => t.AssigneeId == ownerId && (t.Status == open || t.CompletedAt >= recent));
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
            case "papierkorb":
                q = Trash();
                break;
            case "vertretung":
                // Was mir jemand für seinen Urlaub übergeben hat – nur, was mir (noch) zugewiesen ist.
                var absenceId = id ?? throw ApiException.Invalid("Abwesenheit fehlt.");
                var handedOver = AbsenceTask.HandedOver;
                q = q.Where(t => t.AssigneeId == u && (t.Status == open || t.CompletedAt >= recent)
                                 && db.AbsenceTasks.Any(h => h.AbsenceId == absenceId && h.TaskId == t.Id
                                                             && h.Decision == handedOver && h.ReturnedAt == null));
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

        IOrderedQueryable<TaskItem> ordered = view switch
        {
            "erledigt" => q.OrderByDescending(t => t.CompletedAt),
            "papierkorb" => q.OrderByDescending(t => t.DeletedAt),
            // Was zuerst wieder dran ist, steht oben.
            "warteschleife" => q.OrderBy(t => t.SnoozedUntil == null ? 1 : 0).ThenBy(t => t.SnoozedUntil).ThenByDescending(t => t.CreatedAt),
            _ => q.OrderBy(t => t.Status == done ? 1 : 0)
                .ThenBy(t => t.DueDate == null ? 1 : 0)
                .ThenBy(t => t.DueDate)
                .ThenBy(t => t.DueTime)
                .ThenBy(t => t.Priority == "hoch" ? 0 : t.Priority == "normal" ? 1 : 2)
                .ThenByDescending(t => t.CreatedAt),
        };

        var items = await ordered.Take(500).ToListAsync(ct);
        if (view == "suche" && items.Count > 0)
        {
            // Suchtreffer, die nur über ein Einsichtsrecht sichtbar sind, gelten als angesehen.
            var ids = items.Select(t => t.Id).ToList();
            var own = await VisibleOwn(db.Tasks).Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
            foreach (var ownerId in items.Where(t => !own.Contains(t.Id)).Select(t => t.AssigneeId).Distinct())
            {
                await LogGrantAccessAsync(ownerId, "angesehen", null, ct);
            }
        }
        return items.Select(ToDto).ToList();
    }

    public async Task<CountsDto> CountsAsync(CancellationToken ct)
    {
        var u = me.Id;
        var today = clock.Today;
        var open = Values.Open;

        // Zwei Abfragen mit bedingten Zählungen statt einer Abfrage pro Zähler – die Seitenleiste wird
        // bei jeder Live-Änderung neu geladen. Die Bedingungen müssen Ausdrücke bleiben (keine Lambdas
        // mit Anweisungsblock), sonst kann EF Core sie nicht in SQL übersetzen.
        var mine = db.Tasks.Where(t => t.Status == open && (t.AssigneeId == u || t.CreatedById == u));

        // Was in den eigenen Ansichten zu sehen ist: Zurückgestelltes zählt erst ab seinem Tag (siehe Present).
        var own = await mine
            .Where(t => t.AssigneeId == u)
            .Where(Present(today))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MyDay = g.Count(t => t.MyDayOn == today || t.DueDate <= today),
                Important = g.Count(t => t.Priority == "hoch"),
                Planned = g.Count(t => t.DueDate != null),
                Assigned = g.Count(t => t.CreatedById != u),
                FollowUps = g.Count(t => t.Kind == "wiedervorlage"),
                Inbox = g.Count(t => t.ListId == null),
            })
            .FirstOrDefaultAsync(ct);

        var other = await mine
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Overdue = g.Count(t => t.AssigneeId == u && t.DueDate < today),
                Delegated = g.Count(t => t.CreatedById == u && t.AssigneeId != u),
                Waiting = g.Count(t => t.AssigneeId == u && (t.WaitingFor != "" || t.SnoozedUntil > today)),
                WaitingDue = g.Count(t => t.AssigneeId == u && t.WaitingFor != "" && t.SnoozedUntil <= today),
            })
            .FirstOrDefaultAsync(ct);

        var accessible = AccessibleLists().Select(l => l.Id);
        var lists = await db.Tasks
            .Where(t => t.ListId != null && t.Status == open && accessible.Contains(t.ListId!.Value))
            .GroupBy(t => t.ListId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var projects = await db.Tasks
            .Where(t => t.ProjectId != null && t.Status == open)
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var trash = await db.Tasks.IgnoreQueryFilters()
            .CountAsync(t => t.DeletedAt != null && (t.DeletedById == u || t.CreatedById == u), ct);

        return new CountsDto(
            own?.MyDay ?? 0, own?.Important ?? 0, own?.Planned ?? 0, own?.Assigned ?? 0, other?.Delegated ?? 0,
            own?.FollowUps ?? 0, other?.Overdue ?? 0, lists, projects, own?.Inbox ?? 0, other?.Waiting ?? 0, other?.WaitingDue ?? 0,
            trash);
    }

    /// <summary>
    /// Vorschläge für "Mein Tag" (wie in To Do): Liegengebliebenes, bald Fälliges, Wichtiges
    /// und frisch Angelegtes – nur eigene, offene Aufgaben, die noch nicht im Tag stehen.
    /// </summary>
    public async Task<SuggestionsDto> SuggestionsAsync(CancellationToken ct)
    {
        var u = me.Id;
        var today = clock.Today;
        var soon = today.AddDays(5);
        var recent = clock.UtcNow.AddDays(-3);
        var open = Values.Open;

        var candidates = await Base()
            .Where(t => t.AssigneeId == u && t.Status == open
                        && t.MyDayOn != today
                        && (t.DueDate == null || t.DueDate > today)
                        && (t.SnoozedUntil == null || t.SnoozedUntil <= today)
                        && ((t.MyDayOn != null && t.MyDayOn < today)
                            || (t.DueDate != null && t.DueDate <= soon)
                            || t.Priority == "hoch"
                            || t.CreatedAt >= recent))
            .OrderBy(t => t.DueDate == null ? 1 : 0).ThenBy(t => t.DueDate).ThenByDescending(t => t.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        var used = new HashSet<int>();
        List<TaskDto> Pick(Func<TaskItem, bool> rule) =>
            candidates.Where(t => rule(t) && used.Add(t.Id)).Take(10).Select(ToDto).ToList();

        return new SuggestionsDto(
            Yesterday: Pick(t => t.MyDayOn is { } d && d < today),
            DueSoon: Pick(t => t.DueDate is { } d && d <= soon),
            Important: Pick(t => t.Priority == "hoch"),
            Recent: Pick(t => t.CreatedAt >= recent));
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
        var viaGrant = !await VisibleOwn(db.Tasks).AnyAsync(t => t.Id == id, ct);
        var canEdit = !viaGrant || await Visible(db.Tasks, true).AnyAsync(t => t.Id == id, ct);
        return new TaskDetailDto(ToDto(task), events.Select(e => new EventDto(
            e.Id, e.Kind, e.Text, e.Actor is null ? null : UserRef.From(e.Actor), e.CreatedAt)).ToList(), canEdit, viaGrant);
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
            ListId = req.ListId is int lid ? await AccessibleListAsync(lid, ct) : null,
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

        await using var tx = await BeginAsync(ct);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        AddEvent(task.Id, "angelegt", source switch
        {
            "vorlage" => "aus einer Vorlage",
            "mail" => "aus einer E-Mail",
            "ki" => "mit KI-Vorschlag",
            _ => "",
        });
        if (assignee.Id != me.Id)
        {
            AddEvent(task.Id, "zugewiesen", $"an {assignee.DisplayName}");
            outbox.Enqueue(assignee.Id, NotificationOutbox.Assigned, task.Id, task.Title, task.Notes, me.User.DisplayName);
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        task.CreatedBy = me.User;
        task.Assignee = assignee;
        if (task.ProjectId is int p)
        {
            task.Project = await db.Projects.FindAsync([p], ct);
        }
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    public async Task<TaskDto> PatchAsync(int id, JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw ApiException.Invalid("Erwartet wird ein JSON-Objekt.");
        }
        var task = await LoadEditableAsync(id, ct);
        var previousAssignee = task.AssigneeId;
        var previousDue = task.DueDate;

        if (body.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() != task.Version)
        {
            throw ApiException.Conflict("Die Aufgabe wurde inzwischen geändert. Bitte neu laden.");
        }

        // Wer nur über ein Einsichtsrecht bearbeitet, ändert Inhalte – nicht, wem die Aufgabe gehört
        // oder wer sie sieht.
        var viaGrant = !await VisibleOwn(db.Tasks).AnyAsync(t => t.Id == id, ct);
        const string grantLimit = "Mit einer Freigabe lassen sich Verantwortliche, Projekt und Liste nicht ändern.";

        User? newAssignee = null;
        bool? myDay = null;
        string? waitingFor = null;
        var snoozeSet = false;
        DateOnly? snooze = null;
        var field = "";
        try
        {
            foreach (var prop in body.EnumerateObject())
            {
                field = prop.Name;
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
                        myDay = v.ValueKind == JsonValueKind.True;
                        break;
                    case "snoozedUntil":
                        snoozeSet = true;
                        snooze = isNull ? null : ParseDate(RequireString(v, "Zurückstellen bis"));
                        break;
                    case "waitingFor":
                        waitingFor = isNull ? "" : RequireString(v, "Warte auf").Trim();
                        break;
                    case "listId":
                        if (viaGrant) throw ApiException.Forbidden(grantLimit);
                        task.ListId = isNull ? null : await AccessibleListAsync(v.GetInt32(), ct);
                        break;
                    case "projectId":
                        if (viaGrant) throw ApiException.Forbidden(grantLimit);
                        task.ProjectId = isNull ? null : await OpenProjectAsync(v.GetInt32(), ct);
                        break;
                    case "assigneeId":
                        if (viaGrant) throw ApiException.Forbidden(grantLimit);
                        newAssignee = await ActiveUserAsync(v.GetInt32(), ct);
                        task.AssigneeId = newAssignee.Id;
                        break;
                    default:
                        throw ApiException.Invalid($"Unbekanntes Feld: {prop.Name}");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            // z. B. Text statt Zahl oder null bei einem Pflichtwert
            throw ApiException.Invalid($"Ungültiger Wert im Feld „{field}“.");
        }

        if (task.DueTime is not null && task.DueDate is null)
        {
            throw ApiException.Invalid("Eine Uhrzeit braucht auch ein Datum.");
        }

        // "Mein Tag" gehört der verantwortlichen Person: Sie plant ihren Tag selbst.
        if (myDay == true && task.AssigneeId != me.Id)
        {
            throw ApiException.Invalid("„Mein Tag“ plant jede Person selbst – diese Aufgabe liegt bei jemand anderem.");
        }
        if (myDay is bool md)
        {
            task.MyDayOn = md ? clock.Today : null;
        }
        else if (task.AssigneeId != previousAssignee && task.AssigneeId != me.Id)
        {
            task.MyDayOn = null;
        }

        // „Mein Tag“ heißt jetzt: Eine zurückgestellte Aufgabe ist damit wieder da.
        if (myDay == true && !snoozeSet)
        {
            task.SnoozedUntil = null;
        }

        // Neue verantwortliche Person: Zurückstellen und „Warte auf …“ waren die Planung der bisherigen.
        if (task.AssigneeId != previousAssignee && waitingFor is null && !snoozeSet)
        {
            if (task.SnoozedUntil is not null && myDay is null)
            {
                task.MyDayOn = null;
            }
            task.SnoozedUntil = null;
            task.WaitingFor = "";
            task.WaitingSince = null;
        }

        if (waitingFor is not null || snoozeSet)
        {
            ApplyWaitingAndSnooze(task, waitingFor, snoozeSet, snooze, viaGrant);
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
        await PublishAsync(task, ct, previousAssignee);
        return ToDto(task);
    }

    public async Task<TaskDto> CompleteAsync(int id, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
        if (task.IsDone)
        {
            return ToDto(task);
        }

        var now = clock.UtcNow;
        task.Status = Values.Done;
        // Erledigt: Es wird auf nichts mehr gewartet und nichts mehr zurückgestellt.
        task.SnoozedUntil = null;
        task.WaitingFor = "";
        task.WaitingSince = null;
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

        // Wiederholung: höchstens eine Folgeaufgabe je Aufgabe, auch wenn sie wieder geöffnet
        // und erneut erledigt wird.
        TaskItem? next = null;
        if (task.Recurrence != "" && !await db.TaskEvents.AnyAsync(e => e.TaskId == task.Id && e.Kind == "folgeaufgabe", ct))
        {
            next = BuildNextOccurrence(task);
            db.Tasks.Add(next);
        }

        await using var tx = await BeginAsync(ct);
        await db.SaveChangesAsync(ct);
        if (next is not null)
        {
            AddEvent(next.Id, "angelegt", $"als Wiederholung ({Recurrence.Label(task.Recurrence)})");
            AddEvent(task.Id, "folgeaufgabe", $"nächster Termin {next.DueDate:dd.MM.yyyy}");
            // Urlaubsvertretung: Die Folgeaufgabe gehört mit zur Übergabe, damit „Zurück an mich“ sie erfasst.
            var handovers = await db.AbsenceTasks
                .Where(h => h.TaskId == task.Id && h.Decision == AbsenceTask.HandedOver && h.ReturnedAt == null)
                .Select(h => h.AbsenceId)
                .ToListAsync(ct);
            foreach (var absenceId in handovers)
            {
                db.AbsenceTasks.Add(new AbsenceTask { AbsenceId = absenceId, TaskId = next.Id, Decision = AbsenceTask.HandedOver, DecidedAt = now });
            }
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);

        await PublishAsync(task, ct);
        if (next is not null)
        {
            await PublishAsync(next, ct);
        }
        return ToDto(task);
    }

    public async Task<TaskDto> ReopenAsync(int id, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
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
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    /// <summary>In den Papierkorb legen. Endgültig gelöscht wird nach <see cref="TrashDays"/> Tagen oder von Hand.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
        if (!CanDelete(task))
        {
            throw ApiException.Forbidden("Nur die Person, die die Aufgabe angelegt hat, kann sie löschen.");
        }
        var now = clock.UtcNow;
        task.DeletedAt = now;
        task.DeletedById = me.Id;
        task.Version++;
        task.UpdatedAt = now;
        AddEvent(task.Id, "papierkorb", "");
        await db.SaveChangesAsync(ct);
        await PublishGoneAsync(task.Id, task.CreatedById, task.AssigneeId, task.ListId, task.ProjectId, ct);
    }

    /// <summary>Aus dem Papierkorb zurückholen – wer gelöscht oder angelegt hat.</summary>
    public async Task<TaskDto> RestoreAsync(int id, CancellationToken ct)
    {
        var task = await Trash().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw ApiException.NotFound("Aufgabe im Papierkorb");
        task.DeletedAt = null;
        task.DeletedById = null;
        task.DeletedBy = null;
        task.Version++;
        task.UpdatedAt = clock.UtcNow;
        AddEvent(task.Id, "wiederhergestellt", "");
        await db.SaveChangesAsync(ct);
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    /// <summary>Endgültig löschen – nur aus dem Papierkorb, mit Checkliste, Anhängen und Verlauf.</summary>
    public async Task PurgeAsync(int id, CancellationToken ct)
    {
        var task = await Trash().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw ApiException.NotFound("Aufgabe im Papierkorb");
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Den eigenen Papierkorb leeren; gibt die Zahl der endgültig gelöschten Aufgaben zurück.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken ct)
    {
        var u = me.Id;
        // Checklisten, Anhänge und Verlauf folgen per ON DELETE CASCADE.
        return await db.Tasks.IgnoreQueryFilters()
            .Where(t => t.DeletedAt != null && (t.DeletedById == u || t.CreatedById == u))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Alle erledigten Aufgaben, die die Person angelegt hat, in den Papierkorb legen. Erledigte Aufgaben,
    /// die andere angelegt haben, bleiben – die zählt <c>Skipped</c>.
    /// </summary>
    public async Task<BulkResultDto> DeleteCompletedAsync(CancellationToken ct)
    {
        var u = me.Id;
        var done = Values.Done;
        var mine = await db.Tasks
            .Where(t => t.Status == done && t.CreatedById == u)
            .Select(t => new { t.Id, t.CreatedById, t.AssigneeId, t.ListId, t.ProjectId })
            .ToListAsync(ct);
        var kept = await db.Tasks.CountAsync(t => t.Status == done && t.AssigneeId == u && t.CreatedById != u, ct);
        if (mine.Count == 0)
        {
            return new BulkResultDto(0, kept);
        }

        var ids = mine.Select(t => t.Id).ToList();
        var now = clock.UtcNow;
        await using var tx = await BeginAsync(ct);
        await db.Tasks.Where(t => ids.Contains(t.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.DeletedAt, now)
            .SetProperty(t => t.DeletedById, u)
            .SetProperty(t => t.UpdatedAt, now)
            .SetProperty(t => t.Version, t => t.Version + 1), ct);
        foreach (var id in ids)
        {
            AddEvent(id, "papierkorb", "");
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        foreach (var t in mine)
        {
            await PublishGoneAsync(t.Id, t.CreatedById, t.AssigneeId, t.ListId, t.ProjectId, ct);
        }
        return new BulkResultDto(mine.Count, kept);
    }

    /// <summary>
    /// Dieselbe Aktion für mehrere Aufgaben. Jede Aufgabe läuft durch dieselben Prüfungen wie einzeln;
    /// was nicht erlaubt oder nicht mehr da ist, wird übersprungen statt die ganze Aktion abzubrechen.
    /// </summary>
    public async Task<BulkResultDto> BulkAsync(BulkRequest req, CancellationToken ct)
    {
        var ids = (req.Ids ?? []).Distinct().ToList();
        if (ids.Count > MaxBulk)
        {
            throw ApiException.Invalid($"Höchstens {MaxBulk} Aufgaben auf einmal.");
        }
        Func<int, Task> action = req.Action switch
        {
            "erledigen" => id => CompleteAsync(id, ct),
            "loeschen" => id => DeleteAsync(id, ct),
            "wiederherstellen" => id => RestoreAsync(id, ct),
            "endgueltig-loeschen" => id => PurgeAsync(id, ct),
            _ => throw ApiException.Invalid("Unbekannte Aktion – erlaubt sind erledigen, loeschen, wiederherstellen und endgueltig-loeschen."),
        };
        var doneCount = 0;
        var skipped = 0;
        foreach (var id in ids)
        {
            try
            {
                await action(id);
                doneCount++;
            }
            catch (ApiException)
            {
                // Die Prüfungen schlagen fehl, bevor etwas geändert wird – es bleibt nichts halb gespeichert.
                skipped++;
            }
        }
        return new BulkResultDto(doneCount, skipped);
    }

    /// <summary>Live-Meldung „weg“ an alle, die die Aufgabe bisher gesehen haben.</summary>
    private async Task PublishGoneAsync(int id, int createdById, int assigneeId, int? listId, int? projectId, CancellationToken ct)
    {
        var message = new { type = "task-deleted", id };
        if (projectId is not null)
        {
            hub.Broadcast(message);
            return;
        }
        var targets = new List<int> { createdById, assigneeId };
        if (listId is int l)
        {
            targets.AddRange(await ListAudienceAsync(l, ct));
        }
        hub.Publish(targets, message);
    }

    public async Task<TaskDto> AddChecklistItemAsync(int id, ChecklistRequest req, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
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
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    public async Task<TaskDto> UpdateChecklistItemAsync(int id, int itemId, ChecklistRequest req, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
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
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    public async Task<TaskDto> DeleteChecklistItemAsync(int id, int itemId, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
        var item = task.Checklist.FirstOrDefault(c => c.Id == itemId) ?? throw ApiException.NotFound("Checklistenpunkt");
        task.Checklist.Remove(item);
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    /// <summary>Anhang laden – nur wenn die Aufgabe für die Person sichtbar ist.</summary>
    public async Task<(TaskAttachment Meta, byte[] Data)> GetAttachmentAsync(int id, int attachmentId, CancellationToken ct)
    {
        var task = await LoadVisibleAsync(id, ct);
        var meta = task.Attachments.FirstOrDefault(a => a.Id == attachmentId) ?? throw ApiException.NotFound("Anhang");
        var blob = await db.AttachmentBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.AttachmentId == attachmentId, ct)
                   ?? throw ApiException.NotFound("Anhang");
        return (meta, blob.Data);
    }

    /// <summary>Anhang hochladen (Strg+V, Ziehen und Ablegen, Dateiauswahl) – wer die Aufgabe bearbeiten darf.</summary>
    public async Task<AttachmentDto> AddAttachmentAsync(int id, string? fileName, string? contentType, byte[] data, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
        var name = AttachmentRules.CheckFile(fileName, data);
        if (task.Attachments.Count >= AttachmentRules.MaxPerTask)
        {
            throw ApiException.Invalid($"Höchstens {AttachmentRules.MaxPerTask} Anhänge pro Aufgabe – erst nicht mehr gebrauchte entfernen.");
        }
        if (task.Attachments.Sum(a => a.Size) + data.Length > AttachmentRules.MaxTotalBytes)
        {
            throw ApiException.Invalid($"Die Anhänge dieser Aufgabe wären zusammen größer als {AttachmentRules.MaxTotalBytes / 1024 / 1024} MB – erst nicht mehr gebrauchte entfernen.");
        }

        var attachment = new TaskAttachment
        {
            TaskId = task.Id,
            FileName = name,
            ContentType = AttachmentRules.ContentType(contentType, data),
            Size = data.Length,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data)),
            CreatedAt = clock.UtcNow,
        };
        await using var tx = await BeginAsync(ct);
        task.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        db.AttachmentBlobs.Add(new AttachmentBlob { AttachmentId = attachment.Id, Data = data });
        AddEvent(task.Id, "anhang", name);
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        await PublishAsync(task, ct);
        return new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size);
    }

    /// <summary>Anhang entfernen – wer die Aufgabe bearbeiten darf; der Verlauf hält es fest.</summary>
    public async Task<TaskDto> DeleteAttachmentAsync(int id, int attachmentId, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
        var meta = task.Attachments.FirstOrDefault(a => a.Id == attachmentId) ?? throw ApiException.NotFound("Anhang");
        task.Attachments.Remove(meta);
        db.TaskAttachments.Remove(meta); // der Inhalt (AttachmentBlob) folgt per ON DELETE CASCADE
        AddEvent(task.Id, "anhang-entfernt", meta.FileName);
        task.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await PublishAsync(task, ct);
        return ToDto(task);
    }

    public async Task<EventDto> CommentAsync(int id, CommentRequest req, CancellationToken ct)
    {
        var task = await LoadEditableAsync(id, ct);
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
        await PublishAsync(task, ct);
        return new EventDto(ev.Id, ev.Kind, ev.Text, UserRef.From(me.User), ev.CreatedAt);
    }

    // ---------------------------------------------------------------- Hilfen

    /// <summary>
    /// „Warte auf …“ und Zurückstellen. Beides plant die verantwortliche Person (oder ihre Vertretung
    /// mit Freigabe zum Bearbeiten) – wer nur delegiert hat, blendet anderen nichts aus.
    /// </summary>
    internal void ApplyWaitingAndSnooze(TaskItem task, string? waitingFor, bool snoozeSet, DateOnly? snooze, bool viaGrant)
    {
        if (task.AssigneeId != me.Id && !viaGrant)
        {
            throw ApiException.Invalid("Zurückstellen und „Warte auf …“ plant die verantwortliche Person selbst – diese Aufgabe liegt bei jemand anderem.");
        }
        if (task.IsDone)
        {
            throw ApiException.Invalid("Die Aufgabe ist schon erledigt.");
        }

        var today = clock.Today;
        var previousWaiting = task.WaitingFor;
        var wasSnoozed = task.SnoozedUntil is { } previous && previous > today;
        var resumeNow = false;

        if (waitingFor is not null)
        {
            if (waitingFor.Length > MaxWaitingFor)
            {
                throw ApiException.Invalid($"„Warte auf“ darf höchstens {MaxWaitingFor} Zeichen haben.");
            }
            if (waitingFor == "")
            {
                task.WaitingFor = "";
                task.WaitingSince = null;
                if (previousWaiting != "")
                {
                    AddEvent(task.Id, "antwort-da", previousWaiting);
                    if (!snoozeSet)
                    {
                        task.SnoozedUntil = null;
                        resumeNow = true;
                    }
                }
            }
            else
            {
                if (previousWaiting == "")
                {
                    task.WaitingSince = today;
                }
                task.WaitingFor = waitingFor;
                // Ohne eigenen Tag: in einer Woche nachhaken. Wird nur der Name korrigiert, bleibt der Tag.
                if (!snoozeSet && (previousWaiting == "" || task.SnoozedUntil is null))
                {
                    snoozeSet = true;
                    snooze = DefaultFollowUp(today);
                }
            }
        }

        if (snoozeSet && snooze is { } until)
        {
            if (until <= today)
            {
                throw ApiException.Invalid("Bitte einen Tag ab morgen wählen.");
            }
            if (until > today.AddYears(2))
            {
                throw ApiException.Invalid("Höchstens zwei Jahre im Voraus.");
            }
            // Eine normale Aufgabe, die schon fällig ist, käme sofort zurück – die Fälligkeit geht vor.
            if (task.WaitingFor == "" && task.Kind != "wiedervorlage" && task.DueDate is { } due && due <= today)
            {
                throw ApiException.Invalid("Die Aufgabe ist schon fällig – verschieb stattdessen die Fälligkeit.");
            }
            // Bei einer Wiedervorlage ist das Fälligkeitsdatum der Tag der Wiedervorlage – es wandert mit.
            if (task.Kind == "wiedervorlage" && task.DueDate is { } resubmission && resubmission < until)
            {
                task.DueDate = until;
            }
            task.SnoozedUntil = until;
            task.MyDayOn = until; // an dem Tag wieder oben in „Mein Tag“
            task.FollowUpNotifiedAt = null;
            if (task.WaitingFor != "")
            {
                AddEvent(task.Id, "warten", $"auf {task.WaitingFor}, nachhaken am {until:dd.MM.yyyy}");
            }
            else
            {
                AddEvent(task.Id, "zurueckgestellt", $"bis {until:dd.MM.yyyy}");
            }
        }
        else if (snoozeSet)
        {
            task.SnoozedUntil = null;
            resumeNow = true;
            if (wasSnoozed && waitingFor is null)
            {
                AddEvent(task.Id, "zurueckgeholt", "");
            }
        }
        else if (waitingFor is { Length: > 0 } && waitingFor != previousWaiting)
        {
            AddEvent(task.Id, "warten", $"auf {waitingFor}");
        }

        if (resumeNow)
        {
            // Wieder da – heute gibt es etwas zu tun.
            task.MyDayOn = today;
        }
    }

    /// <summary>Samstag und Sonntag auf den folgenden Montag schieben.</summary>
    internal static DateOnly Workday(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(2),
        DayOfWeek.Sunday => day.AddDays(1),
        _ => day,
    };

    /// <summary>„Warte auf …“ ohne eigenen Tag: in einer Woche nachhaken, immer an einem Werktag.</summary>
    internal static DateOnly DefaultFollowUp(DateOnly today) => Workday(today.AddDays(7));

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

    /// <summary>Eigene Transaktion nur, wenn nicht schon eine läuft (z. B. beim Starten einer Vorlage).</summary>
    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
        db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

    private Task<TaskItem> LoadVisibleAsync(int id, CancellationToken ct) => LoadAsync(id, false, ct);

    private Task<TaskItem> LoadEditableAsync(int id, CancellationToken ct) => LoadAsync(id, true, ct);

    private async Task<TaskItem> LoadAsync(int id, bool forEdit, CancellationToken ct)
    {
        // Bewusst 404 statt 403: private Aufgaben anderer sollen nicht einmal "existieren".
        var task = await Visible(Base(), forEdit).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            if (forEdit && await Visible(db.Tasks).AnyAsync(t => t.Id == id, ct))
            {
                throw ApiException.Forbidden("Deine Freigabe erlaubt nur das Lesen dieser Aufgaben.");
            }
            throw ApiException.NotFound("Aufgabe");
        }
        if (!await VisibleOwn(db.Tasks).AnyAsync(t => t.Id == id, ct))
        {
            await LogGrantAccessAsync(task.AssigneeId, forEdit ? "geaendert" : "geoeffnet", task.Id, ct);
        }
        return task;
    }

    private bool CanDelete(TaskItem t) => t.CreatedById == me.Id || (t.ProjectId is not null && me.IsAdmin);

    internal TaskEvent AddEvent(int taskId, string kind, string text)
    {
        var ev = new TaskEvent { TaskId = taskId, ActorId = me.Id, Kind = kind, Text = text, CreatedAt = clock.UtcNow };
        db.TaskEvents.Add(ev);
        return ev;
    }

    internal async Task PublishAsync(TaskItem task, CancellationToken ct, params int[] extraUsers)
    {
        var message = new { type = "task", id = task.Id };
        if (task.ProjectId is not null)
        {
            hub.Broadcast(message);
            return;
        }
        var targets = new List<int> { task.CreatedById, task.AssigneeId };
        targets.AddRange(extraUsers);
        if (task.ListId is int listId)
        {
            targets.AddRange(await ListAudienceAsync(listId, ct));
        }
        hub.Publish(targets, message);
    }

    /// <summary>Besitzer und Mitglieder einer (geteilten) Liste.</summary>
    private Task<List<int>> ListAudienceAsync(int listId, CancellationToken ct) =>
        db.TaskLists.Where(l => l.Id == listId).Select(l => l.OwnerId)
            .Concat(db.TaskListMembers.Where(m => m.ListId == listId).Select(m => m.UserId))
            .ToListAsync(ct);

    private async Task<User> ActiveUserAsync(int id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct)
        ?? throw ApiException.Invalid("Diese Person gibt es nicht oder sie ist nicht aktiv.");

    private async Task<int> AccessibleListAsync(int id, CancellationToken ct) =>
        await AccessibleLists().AnyAsync(l => l.Id == id, ct) ? id : throw ApiException.Invalid("Auf diese Liste hast du keinen Zugriff.");

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
            CanDelete(t),
            t.Attachments.OrderBy(a => a.Id).Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.Size)).ToList(),
            t.WaitingFor,
            t.WaitingSince?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            t.SnoozedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Gerade ausgeblendet – eine Fälligkeit holt die Aufgabe früher zurück.
            !t.IsDone && t.SnoozedUntil > today && !(t.DueDate <= today),
            t.DeletedAt,
            t.DeletedBy is null ? null : UserRef.From(t.DeletedBy));
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
