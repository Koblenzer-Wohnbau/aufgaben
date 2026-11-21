namespace Aufgaben.Api.Options;

/// <summary>Alle anwendungsspezifischen Einstellungen (Abschnitt "Aufgaben").</summary>
public sealed class AufgabenOptions
{
    public const string Section = "Aufgaben";

    /// <summary>Öffentliche HTTPS-Adresse ohne abschließenden Schrägstrich, z. B. https://aufgaben.example.de</summary>
    public string BaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>IANA-Zeitzone für "heute", Erinnerungen und Tagesübersicht.</summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    public AuthOptions Auth { get; set; } = new();
    public TeamsOptions Teams { get; set; } = new();
    public MailOptions Mail { get; set; } = new();
    public DirectoryOptions Directory { get; set; } = new();
    public MailInboxOptions MailInbox { get; set; } = new();
    public WorkerOptions Worker { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public MistralOptions Mistral { get; set; } = new();

    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');
    public bool IsHttps => NormalizedBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

public sealed class AuthOptions
{
    /// <summary>"Entra" für den Betrieb, "Dev" nur für lokale Entwicklung.</summary>
    public string Mode { get; set; } = "Entra";

    /// <summary>Muss für den Entwicklungsmodus ausdrücklich auf true stehen.</summary>
    public bool AllowDevLogin { get; set; }

    /// <summary>Application ID URI für Teams-SSO, z. B. api://aufgaben.example.de/&lt;client-id&gt;</summary>
    public string ApiAudience { get; set; } = "";

    /// <summary>Name der App-Rolle für Verwaltungsfunktionen.</summary>
    public string AdminRole { get; set; } = "Aufgaben.Admin";

    /// <summary>Gültigkeit der Browser-Sitzung in Stunden.</summary>
    public int SessionHours { get; set; } = 10;

    /// <summary>
    /// Höchstdauer einer Browser-Sitzung ab der Anmeldung in Stunden, auch bei ständiger Nutzung.
    /// Danach ist eine neue Anmeldung nötig – so kommen in Entra geänderte Rollen spätestens dann an.
    /// </summary>
    public int MaxSessionHours { get; set; } = 24;

    public bool IsDev => string.Equals(Mode, "Dev", StringComparison.OrdinalIgnoreCase);
}

public sealed class TeamsOptions
{
    /// <summary>Die "id" aus dem Teams-App-Manifest (für Deeplinks in Benachrichtigungen).</summary>
    public string AppId { get; set; } = "";

    /// <summary>Teams-Aktivitätsfeed-Benachrichtigungen senden.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>entityId des persönlichen Tabs im Manifest.</summary>
    public string PersonalTabEntityId { get; set; } = "aufgaben";

    /// <summary>Herausgeber im Teams-Paket (höchstens 32 Zeichen), z. B. der Name der Organisation.</summary>
    public string Developer { get; set; } = "";
}

public sealed class MailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;

    /// <summary>"None", "StartTls", "StartTlsWhenAvailable" oder "SslOnConnect".</summary>
    public string Security { get; set; } = "StartTls";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "Aufgaben";

    /// <summary>Optional: PEM-Datei der internen Zertifizierungsstelle für STARTTLS.</summary>
    public string CaCertificatePath { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>"Mail an Aufgabe": eigenes Postfach, das per IMAP abgerufen wird.</summary>
public sealed class MailInboxOptions
{
    public bool Enabled { get; set; }

    /// <summary>Adresse, an die Mitarbeitende weiterleiten (wird in der App angezeigt).</summary>
    public string Address { get; set; } = "";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;

    /// <summary>"SslOnConnect" (Port 993), "StartTls" (Port 143) …</summary>
    public string Security { get; set; } = "SslOnConnect";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Optional: PEM der internen CA, falls der Mailserver ein internes Zertifikat hat.</summary>
    public string CaCertificatePath { get; set; } = "";

    public string ProcessedFolder { get; set; } = "Verarbeitet";
    public string RejectedFolder { get; set; } = "Abgelehnt";
    public int PollSeconds { get; set; } = 60;

    /// <summary>
    /// "exchange-intern": nur Mails, die Exchange als intern authentifiziert gestempelt hat
    /// (Kopfzeile X-MS-Exchange-Organization-AuthAs: Internal) – schützt vor gefälschten Absendern.
    /// "bekannte-absender": nur prüfen, ob der Absender eine bekannte Person ist (für andere Mailserver;
    /// dann unbedingt serverseitig externe Absender für das Postfach sperren).
    /// </summary>
    public string SenderCheck { get; set; } = "exchange-intern";

    public int MaxAttachmentMb { get; set; } = 15;
    public bool SendConfirmation { get; set; } = true;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username);
}

public sealed class DirectoryOptions
{
    /// <summary>Benutzer regelmäßig aus Entra übernehmen (für Zuweisungen an Personen, die sich noch nie angemeldet haben).</summary>
    public bool SyncEnabled { get; set; } = true;

    public int SyncMinutes { get; set; } = 360;

    /// <summary>Optional: nur Mitglieder dieser Entra-Gruppe übernehmen.</summary>
    public string GroupId { get; set; } = "";
}

public sealed class WorkerOptions
{
    /// <summary>Hintergrunddienst für Erinnerungen, Benachrichtigungen und Verzeichnisabgleich.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Uhrzeit (lokal) für die Tagesübersicht.</summary>
    public int DigestHour { get; set; } = 7;

    /// <summary>Tagesübersicht auch am Wochenende.</summary>
    public bool DigestOnWeekends { get; set; }
}

public sealed class DatabaseOptions
{
    public bool AutoMigrate { get; set; } = true;
    public bool SeedExampleTemplates { get; set; } = true;
}

/// <summary>
/// KI-Vorschläge über die Mistral-API (Standard: GLM 5.3). Standardmäßig aus – erst mit Enabled=true und
/// einem Schlüssel gehen Texte an Mistral. Vorher Auftragsverarbeitung mit der/dem Datenschutzbeauftragten klären.
/// </summary>
public sealed class MistralOptions
{
    public bool Enabled { get; set; }

    /// <summary>API-Schlüssel direkt in der .env. Alternativ <see cref="ApiKeyFile"/>.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Datei mit dem Schlüssel, z. B. /certs/mistral.key (Ordner secrets/ im Container).</summary>
    public string ApiKeyFile { get; set; } = "";

    public string Model { get; set; } = "zai-glm-latest";

    /// <summary>none, high oder max (max nur bei GLM); auto heißt bei GLM max, sonst high.</summary>
    public string ReasoningEffort { get; set; } = "high";

    /// <summary>Modell für die Texterkennung eingefügter Bildschirmfotos.</summary>
    public string OcrModel { get; set; } = "mistral-ocr-4-0";

    /// <summary>Höchstdauer eines Aufrufs. Mit Nachdenken dauern Antworten oft 10 bis 60 Sekunden.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Auch „Mail an Aufgabe“ mit KI auswerten (sonst nur die Erkennung im Betreff).</summary>
    public bool MailInbox { get; set; } = true;
}
