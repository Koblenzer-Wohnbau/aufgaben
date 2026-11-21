namespace Aufgaben.Api.Data;

/// <summary>Feste Werte, die in der Datenbank als Text gespeichert werden.</summary>
public static class Values
{
    public const string Open = "offen";
    public const string Done = "erledigt";

    public static readonly string[] Priorities = ["niedrig", "normal", "hoch"];
    public static readonly string[] Kinds = ["aufgabe", "wiedervorlage"];
    public static readonly string[] Recurrences = ["", "taeglich", "werktags", "woechentlich", "monatlich", "jaehrlich"];
    public static readonly string[] NotifyChannels = ["auto", "teams", "email", "aus"];
    public static readonly string[] ListColors = ["blau", "gruen", "rot", "orange", "lila", "grau"];
}

public sealed class User
{
    public int Id { get; set; }
    public required string EntraOid { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Upn { get; set; }
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; }
    public string NotifyChannel { get; set; } = "auto";
    public bool DigestEnabled { get; set; } = true;
    public DateOnly? LastDigestOn { get; set; }
    public DateTimeOffset? TeamsSeenAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? IcsTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Project
{
    public int Id { get; set; }
    public string? Number { get; set; }
    public required string Name { get; set; }
    public string? TeamName { get; set; }
    public string? TeamsTeamId { get; set; }
    public string? TeamsChannelId { get; set; }
    public bool Archived { get; set; }
    public int? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string Label => string.IsNullOrWhiteSpace(Number) ? Name : $"{Number} {Name}";
}

public sealed class TaskList
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = "blau";
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TaskListMember> Members { get; set; } = [];
}

/// <summary>Geteilte Liste: Mitglieder sehen alle Aufgaben der Liste und können Aufgaben hinzufügen.</summary>
public sealed class TaskListMember
{
    public int ListId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// Eine Aufgabe. Jede Aufgabe hat genau eine verantwortliche Person (Assignee).
/// Persönlich = Ersteller ist verantwortlich. Delegiert = jemand anderes ist verantwortlich.
/// Projektaufgaben sind für alle angemeldeten Personen sichtbar.
/// </summary>
public sealed class TaskItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Notes { get; set; } = "";
    public string Status { get; set; } = Values.Open;
    public string Kind { get; set; } = "aufgabe";
    public string Priority { get; set; } = "normal";
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public int AssigneeId { get; set; }
    public User Assignee { get; set; } = null!;
    public int? ListId { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public DateTimeOffset? RemindAt { get; set; }
    public DateTimeOffset? RemindedAt { get; set; }
    public DateOnly? MyDayOn { get; set; }

    /// <summary>
    /// Zurückgestellt: bis zu diesem Tag aus den eigenen Listen ausgeblendet, an dem Tag wieder in „Mein Tag“.
    /// Bei „Warte auf …“ ist das der Tag zum Nachhaken. Eine Fälligkeit davor holt die Aufgabe früher zurück.
    /// </summary>
    public DateOnly? SnoozedUntil { get; set; }

    /// <summary>„Warte auf …“: Person oder Firma, von der eine Antwort fehlt – frei eingegeben, auch Externe.</summary>
    public string WaitingFor { get; set; } = "";
    public DateOnly? WaitingSince { get; set; }

    /// <summary>Wann ans Nachhaken erinnert wurde – einmal je Termin.</summary>
    public DateTimeOffset? FollowUpNotifiedAt { get; set; }

    /// <summary>
    /// Im Papierkorb seit … (leer: nicht gelöscht). Ein globaler Filter blendet solche Aufgaben in allen
    /// Abfragen aus; nur der Papierkorb liest sie ausdrücklich mit <c>IgnoreQueryFilters</c>.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public User? DeletedBy { get; set; }

    public string Recurrence { get; set; } = "";
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedById { get; set; }
    public User? CompletedBy { get; set; }
    public string Source { get; set; } = "app";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public List<ChecklistItem> Checklist { get; set; } = [];

    /// <summary>Nur die Metadaten – der Inhalt liegt in <see cref="AttachmentBlob"/>.</summary>
    public List<TaskAttachment> Attachments { get; set; } = [];

    public bool IsDone => Status == Values.Done;
}

/// <summary>Anhang einer Aufgabe (z. B. aus einer weitergeleiteten E-Mail).</summary>
public sealed class TaskAttachment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public required string FileName { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Inhalt eines Anhangs – getrennt, damit Listenabfragen nie Dateidaten laden.</summary>
public sealed class AttachmentBlob
{
    public int AttachmentId { get; set; }
    public required byte[] Data { get; set; }
}

/// <summary>
/// Einsichtsrecht: Person B (Viewer) darf die Aufgaben von Person A (Owner) sehen – nur nach
/// dokumentierter, unterschriebener Freigabe. Entwurf → aktiv (nach Bestätigung) → widerrufen.
/// </summary>
public sealed class AccessGrant
{
    public const string Draft = "entwurf";
    public const string Active = "aktiv";
    public const string Revoked = "widerrufen";

    public int Id { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public int ViewerId { get; set; }
    public User Viewer { get; set; } = null!;

    /// <summary>"lesen" oder "bearbeiten".</summary>
    public string Permission { get; set; } = "lesen";

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string Purpose { get; set; } = "";
    public string Status { get; set; } = Draft;

    public int CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
    public int? ConfirmedById { get; set; }

    /// <summary>Wo das unterschriebene Original liegt (z. B. "Personalakte").</summary>
    public string ConsentNote { get; set; } = "";

    public DateTimeOffset? RevokedAt { get; set; }
    public int? RevokedById { get; set; }
    public string RevokeReason { get; set; } = "";
}

/// <summary>Protokoll: wer hat wann über ein Einsichtsrecht fremde Aufgaben angesehen oder geändert.</summary>
public sealed class AccessLogEntry
{
    public long Id { get; set; }
    public int GrantId { get; set; }
    public int ViewerId { get; set; }
    public int OwnerId { get; set; }

    /// <summary>"angesehen", "geoeffnet", "geaendert", "angelegt", "bestaetigt", "widerrufen".</summary>
    public required string Action { get; set; }

    public int? TaskId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Protokoll der Mails aus dem Aufgaben-Postfach (verhindert doppelte Verarbeitung).</summary>
public sealed class InboundMail
{
    public long Id { get; set; }

    /// <summary>SHA-256 der Message-ID (bzw. von Absender, Datum und Betreff, falls sie fehlt).</summary>
    public required string MessageKey { get; set; }

    public string FromAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>"angelegt" oder "abgelehnt".</summary>
    public required string Result { get; set; }

    public string Reason { get; set; } = "";
    public int? TaskId { get; set; }
}

public sealed class ChecklistItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public required string Title { get; set; }
    public bool Done { get; set; }
    public int Position { get; set; }
}

/// <summary>Verlauf: angelegt, zugewiesen, erledigt, Kommentar …</summary>
public sealed class TaskEvent
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public int? ActorId { get; set; }
    public User? Actor { get; set; }
    public required string Kind { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Ausgangskorb für Benachrichtigungen (Teams-Aktivitätsfeed oder E-Mail).</summary>
public sealed class Notification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? TaskId { get; set; }
    public required string Kind { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string? Channel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Vorlage für wiederkehrende Abläufe (z. B. Mieterwechsel).</summary>
public sealed class Template
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TemplateStep> Steps { get; set; } = [];
}

public sealed class TemplateStep
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int Position { get; set; }
    public required string Title { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>Fälligkeit relativ zum Stichtag in Tagen (negativ = vorher).</summary>
    public int OffsetDays { get; set; }

    public string Priority { get; set; } = "normal";

    /// <summary>Zeilen der Checkliste, durch Zeilenumbrüche getrennt.</summary>
    public string ChecklistText { get; set; } = "";

    /// <summary>Hinweis für die Zuweisung, z. B. "Hausmeister".</summary>
    public string RoleHint { get; set; } = "";
}
