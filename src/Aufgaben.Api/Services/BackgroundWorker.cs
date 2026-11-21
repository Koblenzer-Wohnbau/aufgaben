using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Services;

/// <summary>
/// Läuft im Hintergrund (Standard: alle 30 Sekunden):
/// 1. fällige Erinnerungen in den Ausgangskorb legen,
/// 2. einmal täglich die Tagesübersicht erzeugen,
/// 3. bei „Warte auf …“ am Tag zum Nachhaken erinnern,
/// 4. den Ausgangskorb versenden (Teams oder E-Mail, mit Wiederholungen),
/// 5. regelmäßig Personen aus Entra übernehmen.
///
/// Betrieb mit genau einer Instanz vorgesehen (wie auf der Ubuntu-VM).
/// </summary>
public sealed class BackgroundWorker(IServiceScopeFactory scopes, IOptions<AufgabenOptions> options, ILogger<BackgroundWorker> log) : BackgroundService
{
    /// <summary>Danach gilt eine Benachrichtigung als endgültig fehlgeschlagen.</summary>
    public const int MaxAttempts = 6;

    private DateTimeOffset _nextDirectorySync = DateTimeOffset.MinValue;
    private DateTimeOffset _nextInboxPoll = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.Worker.IntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        // Kurz warten, damit Migrationen und Start abgeschlossen sind.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await Safe("Erinnerungen", ct, (sp, c) => QueueRemindersAsync(sp, c));
        await Safe("Tagesübersicht", ct, (sp, c) => QueueDigestsAsync(sp, c));
        await Safe("Nachhaken", ct, (sp, c) => IsOfficeTime(sp) ? QueueFollowUpsAsync(sp, c) : Task.CompletedTask);
        await Safe("Aufgaben-Postfach", ct, (sp, c) => PollInboxAsync(sp, c));
        await Safe("Versand", ct, (sp, c) => DispatchAsync(sp, c));
        await Safe("Verzeichnisabgleich", ct, (sp, c) => SyncDirectoryAsync(sp, c));
    }

    private async Task Safe(string name, CancellationToken ct, Func<IServiceProvider, CancellationToken, Task> work)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Hintergrundaufgabe {Name} fehlgeschlagen", name);
        }
    }

    internal static async Task QueueRemindersAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<AppClock>();
        var outbox = sp.GetRequiredService<NotificationOutbox>();
        var now = clock.UtcNow;

        var due = await db.Tasks
            .Where(t => t.Status == Values.Open && t.RemindAt != null && t.RemindAt <= now && t.RemindedAt == null)
            .OrderBy(t => t.RemindAt)
            .Take(200)
            .ToListAsync(ct);
        foreach (var task in due)
        {
            task.RemindedAt = now;
            // Erinnerungen, die älter als ein Tag sind (z. B. nach langem Stillstand), nicht mehr senden.
            if (now - task.RemindAt!.Value < TimeSpan.FromDays(1))
            {
                outbox.Enqueue(task.AssigneeId, NotificationOutbox.Reminder, task.Id, task.Title, task.Notes, "");
            }
        }
        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    internal static async Task QueueDigestsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<AppClock>();
        var outbox = sp.GetRequiredService<NotificationOutbox>();
        var opts = sp.GetRequiredService<IOptions<AufgabenOptions>>().Value.Worker;

        var local = clock.LocalNow;
        var today = clock.Today;
        if (local.Hour < opts.DigestHour || local.Hour > opts.DigestHour + 3)
        {
            return;
        }
        if (!opts.DigestOnWeekends && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return;
        }

        var users = await db.Users
            .Where(u => u.IsActive && u.DigestEnabled && u.NotifyChannel != "aus" && (u.LastDigestOn == null || u.LastDigestOn < today))
            .Take(500)
            .ToListAsync(ct);
        foreach (var user in users)
        {
            user.LastDigestOn = today;
            var open = db.Tasks.Where(t => t.AssigneeId == user.Id && t.Status == Values.Open);
            var dueToday = await open.CountAsync(t => t.DueDate == today, ct);
            var overdue = await open.CountAsync(t => t.DueDate != null && t.DueDate < today, ct);
            var followUp = await open.CountAsync(t => t.WaitingFor != "" && t.SnoozedUntil != null && t.SnoozedUntil <= today, ct);
            var myDay = await open.CountAsync(t => t.MyDayOn == today && (t.DueDate == null || t.DueDate > today) && t.WaitingFor == "", ct);
            if (dueToday + overdue + myDay + followUp == 0)
            {
                continue;
            }
            var parts = new List<string>();
            if (dueToday > 0) parts.Add($"{dueToday} heute fällig");
            if (overdue > 0) parts.Add($"{overdue} überfällig");
            if (myDay > 0) parts.Add($"{myDay} für deinen Tag geplant");
            if (followUp > 0) parts.Add($"{followUp} zum Nachhaken");
            var titles = await open
                .Where(t => t.DueDate != null && t.DueDate <= today)
                .OrderBy(t => t.DueDate).ThenBy(t => t.DueTime)
                .Select(t => t.Title)
                .Take(10)
                .ToListAsync(ct);
            outbox.Enqueue(user.Id, NotificationOutbox.Digest, null, "Dein Tag: " + string.Join(", ", parts),
                string.Join("\n", titles.Select(t => "• " + t)), "");
        }
        if (users.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Ab der Uhrzeit der Tagesübersicht, am Wochenende nur, wenn die Tagesübersicht dann auch kommt.</summary>
    private static bool IsOfficeTime(IServiceProvider sp)
    {
        var local = sp.GetRequiredService<AppClock>().LocalNow;
        var opts = sp.GetRequiredService<IOptions<AufgabenOptions>>().Value.Worker;
        return local.Hour >= opts.DigestHour
               && (opts.DigestOnWeekends || local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    /// <summary>
    /// „Warte auf …“: Ist der Tag zum Nachhaken da, einmal erinnern. Die Aufgabe steht dann ohnehin
    /// wieder in „Mein Tag“ – die Nachricht sorgt dafür, dass es auch auffällt.
    /// </summary>
    internal static async Task QueueFollowUpsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<AppClock>();
        var outbox = sp.GetRequiredService<NotificationOutbox>();
        var today = clock.Today;
        var now = clock.UtcNow;

        var due = await db.Tasks
            .Where(t => t.Status == Values.Open && t.WaitingFor != "" && t.SnoozedUntil != null && t.SnoozedUntil <= today
                        && t.FollowUpNotifiedAt == null)
            .OrderBy(t => t.SnoozedUntil)
            .Take(200)
            .ToListAsync(ct);
        foreach (var task in due)
        {
            task.FollowUpNotifiedAt = now;
            // Nach längerem Stillstand keine Flut alter Erinnerungen – die Aufgaben stehen ja wieder in den Listen.
            if (task.SnoozedUntil!.Value < today.AddDays(-3))
            {
                continue;
            }
            var body = task.WaitingSince is { } since
                ? $"Du wartest seit dem {since:dd.MM.yyyy} auf {task.WaitingFor}."
                : $"Du wartest auf {task.WaitingFor}.";
            outbox.Enqueue(task.AssigneeId, NotificationOutbox.FollowUp, task.Id, task.Title, body, task.WaitingFor);
        }
        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    internal static async Task DispatchAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<AppClock>();
        var teams = sp.GetRequiredService<TeamsNotifier>();
        var mail = sp.GetRequiredService<MailNotifier>();
        var log = sp.GetRequiredService<ILogger<BackgroundWorker>>();
        var now = clock.UtcNow;

        var pending = await db.Notifications
            .Include(n => n.User)
            .Where(n => n.SentAt == null && n.NextAttemptAt <= now && n.Attempts < MaxAttempts)
            .OrderBy(n => n.Id)
            .Take(100)
            .ToListAsync(ct);

        foreach (var n in pending)
        {
            n.Attempts++;
            try
            {
                var channel = ResolveChannel(n.User, teams.IsAvailable, mail.IsAvailable);
                if (channel == "teams")
                {
                    var result = await teams.SendAsync(n, n.User, ct);
                    if (!result.Success)
                    {
                        if (result.ShouldFallBackToMail && mail.IsAvailable && !string.IsNullOrEmpty(n.User.Email))
                        {
                            await mail.SendAsync(n, n.User, ct);
                            channel = "email";
                        }
                        else
                        {
                            if (result.ShouldFallBackToMail)
                            {
                                // 400/403/404: meist ist die App bei der Person nicht installiert. Ein neuer
                                // Versuch ändert daran nichts – sofort als endgültig fehlgeschlagen verbuchen.
                                n.Attempts = MaxAttempts;
                            }
                            throw new InvalidOperationException($"Teams: {(int?)result.Status} {result.Error}");
                        }
                    }
                }
                else if (channel == "email")
                {
                    await mail.SendAsync(n, n.User, ct);
                }
                n.Channel = channel;
                n.SentAt = clock.UtcNow;
                n.LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.Message.Length > 480 ? ex.Message[..480] : ex.Message;
                n.LastError = message;
                // Exponentielles Warten: 1, 2, 4, 8, 16 Minuten
                n.NextAttemptAt = clock.UtcNow.AddMinutes(Math.Pow(2, n.Attempts - 1));
                log.LogWarning("Benachrichtigung {Id} fehlgeschlagen (Versuch {Attempt}): {Error}", n.Id, n.Attempts, message);
            }
        }
        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// "auto": Teams, sobald die Person die App einmal in Teams geöffnet hat (dann ist sie installiert),
    /// sonst E-Mail. "aus" oder kein Weg verfügbar: als "keiner" verbuchen.
    /// </summary>
    internal static string ResolveChannel(User user, bool teamsAvailable, bool mailAvailable)
    {
        var hasMail = mailAvailable && !string.IsNullOrWhiteSpace(user.Email);
        return user.NotifyChannel switch
        {
            "aus" => "keiner",
            "teams" when teamsAvailable => "teams",
            "email" when hasMail => "email",
            _ when teamsAvailable && user.TeamsSeenAt is not null => "teams",
            _ when hasMail => "email",
            _ when teamsAvailable => "teams",
            _ => "keiner",
        };
    }

    private async Task PollInboxAsync(IServiceProvider sp, CancellationToken ct)
    {
        var poller = sp.GetRequiredService<MailInboxPoller>();
        var clock = sp.GetRequiredService<AppClock>();
        if (!poller.IsConfigured || clock.UtcNow < _nextInboxPoll)
        {
            return;
        }
        _nextInboxPoll = clock.UtcNow.AddSeconds(Math.Clamp(options.Value.MailInbox.PollSeconds, 15, 3600));
        await poller.PollAsync(ct);
    }

    private async Task SyncDirectoryAsync(IServiceProvider sp, CancellationToken ct)
    {
        var opts = options.Value.Directory;
        var clock = sp.GetRequiredService<AppClock>();
        var graph = sp.GetRequiredService<GraphClient>();
        if (!opts.SyncEnabled || !graph.IsConfigured || clock.UtcNow < _nextDirectorySync)
        {
            return;
        }
        _nextDirectorySync = clock.UtcNow.AddMinutes(Math.Max(15, opts.SyncMinutes));
        await sp.GetRequiredService<DirectorySync>().RunAsync(ct);
    }
}
