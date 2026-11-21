using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;

namespace Aufgaben.Api.Services;

/// <summary>
/// Legt Benachrichtigungen im Ausgangskorb ab. Der Versand passiert im Hintergrund
/// (siehe <see cref="BackgroundWorker"/>), damit eine langsame Mail- oder Graph-Verbindung
/// keine Aufgabe blockiert und fehlgeschlagene Sendungen wiederholt werden.
/// </summary>
public sealed class NotificationOutbox(AppDbContext db, AppClock clock)
{
    public const string Assigned = "zugewiesen";
    public const string Completed = "erledigt";
    public const string Commented = "kommentar";
    public const string Reminder = "erinnerung";
    /// <summary>„Warte auf …“: Der Tag zum Nachhaken ist da. ActorName = auf wen gewartet wird.</summary>
    public const string FollowUp = "nachhaken";
    public const string Digest = "tagesuebersicht";
    /// <summary>Mehrere Aufgaben auf einmal an die Urlaubsvertretung übergeben – eine Nachricht statt vieler.</summary>
    public const string Handover = "vertretung";
    public const string ListShared = "liste-geteilt";
    public const string Access = "einsicht";

    public void Enqueue(int userId, string kind, int? taskId, string title, string body, string actorName)
    {
        var now = clock.UtcNow;
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            TaskId = taskId,
            Kind = kind,
            Title = Truncate(title, 500),
            Body = body,
            ActorName = Truncate(actorName, 200),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
