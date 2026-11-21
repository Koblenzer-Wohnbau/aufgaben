using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Aufgaben.Api.Data;
using Aufgaben.Api.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Aufgaben.Api.Services;

/// <summary>Texte für Benachrichtigungen – einmal definiert, für Teams und E-Mail genutzt.</summary>
public static class NotificationTexts
{
    public static (string ActivityType, string Subject, string Preview) For(Notification n) => n.Kind switch
    {
        NotificationOutbox.Assigned => ("taskAssigned", $"{n.ActorName} hat dir eine Aufgabe zugewiesen: {n.Title}", $"{n.ActorName} hat dir „{n.Title}“ zugewiesen"),
        NotificationOutbox.Completed => ("taskCompleted", $"Erledigt: {n.Title}", $"{n.ActorName} hat „{n.Title}“ erledigt"),
        NotificationOutbox.Commented => ("taskCommented", $"Neuer Kommentar: {n.Title}", $"{n.ActorName}: {Shorten(n.Body, 120)}"),
        NotificationOutbox.Reminder => ("taskReminder", $"Erinnerung: {n.Title}", $"Erinnerung: {n.Title}"),
        // Nutzt die vorhandene Aktivität "taskReminder" – so braucht es kein neues Teams-Paket.
        NotificationOutbox.FollowUp => ("taskReminder", $"Nachhaken: {n.Title}", $"Noch keine Antwort von {n.ActorName} – nachhaken?"),
        // Nutzt die vorhandene Aktivität "taskAssigned": „{person} hat dir „12 Aufgaben für die Urlaubsvertretung“ zugewiesen“.
        NotificationOutbox.Handover => ("taskAssigned", $"{n.ActorName} hat dir {n.Title} übergeben", $"{n.ActorName} hat dir {n.Title} übergeben"),
        NotificationOutbox.Digest => ("dailyDigest", n.Title, n.Title),
        NotificationOutbox.Access => ("accessChanged", n.Title, n.Title),
        NotificationOutbox.ListShared => ("listShared", $"{n.ActorName} hat die Liste „{n.Title}“ mit dir geteilt", $"{n.ActorName} hat die Liste „{n.Title}“ mit dir geteilt"),
        _ => ("taskAssigned", n.Title, n.Title),
    };

    public static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

/// <summary>Teams-Aktivitätsfeed über Graph (TeamsActivity.Send, Anwendungsberechtigung).</summary>
public sealed class TeamsNotifier(GraphClient graph, IOptions<AufgabenOptions> options)
{
    public bool IsAvailable => options.Value.Teams.NotificationsEnabled && graph.IsConfigured && !string.IsNullOrWhiteSpace(options.Value.Teams.AppId);

    /// <summary>
    /// Deeplink in den persönlichen Tab, der direkt die Aufgabe öffnet.
    /// Format: https://teams.microsoft.com/l/entity/{appId}/{entityId}?context={"subEntityId":"task-42"}
    /// </summary>
    public string DeepLink(int? taskId)
    {
        var o = options.Value.Teams;
        var url = $"https://teams.microsoft.com/l/entity/{Uri.EscapeDataString(o.AppId)}/{Uri.EscapeDataString(o.PersonalTabEntityId)}";
        if (taskId is int id)
        {
            var context = JsonSerializer.Serialize(new { subEntityId = $"task-{id}" });
            url += "?context=" + Uri.EscapeDataString(context);
        }
        return url;
    }

    public Task<GraphResult> SendAsync(Notification n, User user, CancellationToken ct)
    {
        var (type, _, preview) = NotificationTexts.For(n);
        var parameters = new List<object>
        {
            new { name = "taskTitle", value = NotificationTexts.Shorten(n.Title, 100) },
        };
        if (type is "taskAssigned" or "taskCompleted" or "taskCommented" or "listShared")
        {
            parameters.Add(new { name = "person", value = n.ActorName });
        }
        if (type is "dailyDigest" or "accessChanged")
        {
            parameters.Clear();
            parameters.Add(new { name = "summary", value = NotificationTexts.Shorten(n.Title, 100) });
        }

        var payload = new
        {
            topic = new { source = "text", value = "Aufgaben", webUrl = DeepLink(n.TaskId) },
            activityType = type,
            previewText = new { content = NotificationTexts.Shorten(preview, 150) },
            templateParameters = parameters,
        };
        return graph.SendActivityNotificationAsync(user.EntraOid, payload, ct);
    }
}

/// <summary>E-Mail über SMTP, z. B. über einen internen Exchange-Relay.</summary>
public sealed class MailNotifier(IOptions<AufgabenOptions> options, ILogger<MailNotifier> log)
{
    public bool IsAvailable => options.Value.Mail.Enabled;

    public async Task SendAsync(Notification n, User user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Für diese Person ist keine E-Mail-Adresse bekannt.");
        }
        var o = options.Value;
        var (_, subject, preview) = NotificationTexts.For(n);
        var link = n.TaskId is int id ? $"{o.NormalizedBaseUrl}/#/aufgabe/{id}" : o.NormalizedBaseUrl + "/";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.Mail.FromName, o.Mail.From));
        message.To.Add(new MailboxAddress(user.DisplayName, user.Email));
        message.Subject = subject;
        message.Headers.Add("Auto-Submitted", "auto-generated");

        var text = new StringBuilder();
        text.AppendLine(preview);
        if (!string.IsNullOrWhiteSpace(n.Body))
        {
            text.AppendLine().AppendLine(n.Body);
        }
        text.AppendLine().AppendLine("Öffnen: " + link);

        var body = new BodyBuilder
        {
            TextBody = text.ToString(),
            HtmlBody = Html(preview, n.Body, link),
        };
        message.Body = body.ToMessageBody();

        await SendMessageAsync(message, ct);
        log.LogInformation("E-Mail ({Kind}) an Person {UserId} gesendet", n.Kind, user.Id);
    }

    /// <summary>Kurze Bestätigung an die Person, die eine Mail ans Aufgaben-Postfach geschickt hat.</summary>
    public async Task SendTaskCreatedAsync(User user, Contracts.TaskDto task, MimeMessage original, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }
        var o = options.Value;
        var link = $"{o.NormalizedBaseUrl}/#/aufgabe/{task.Id}";
        var details = new List<string>();
        if (task.DueDate is not null) details.Add($"Fällig: {DateOnly.Parse(task.DueDate):dd.MM.yyyy}");
        if (task.Project is not null) details.Add($"Projekt: {task.Project.Label}");
        if (task.IsDelegated) details.Add($"Verantwortlich: {task.Assignee.Name} (wurde benachrichtigt)");
        if (task.Priority == "hoch") details.Add("Priorität: hoch");
        if (task.Attachments.Count > 0) details.Add($"Anhänge: {task.Attachments.Count}");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.Mail.FromName, o.Mail.From));
        message.To.Add(new MailboxAddress(user.DisplayName, user.Email));
        message.Subject = $"Aufgabe angelegt: {task.Title}";
        message.Headers.Add("Auto-Submitted", "auto-replied");
        if (!string.IsNullOrEmpty(original.MessageId))
        {
            message.InReplyTo = original.MessageId;
            message.References.Add(original.MessageId);
        }
        var headline = $"„{task.Title}“ ist jetzt eine Aufgabe.";
        var detailText = string.Join("\n", details);
        message.Body = new BodyBuilder
        {
            TextBody = $"{headline}\n\n{detailText}\n\nÖffnen: {link}\n",
            HtmlBody = Html(headline, detailText, link),
        }.ToMessageBody();
        await SendMessageAsync(message, ct);
    }

    private async Task SendMessageAsync(MimeMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient();
        // Standard: öffentlich vertrauenswürdiges Zertifikat. Sonst eigene CA oder fester Fingerabdruck.
        client.ServerCertificateValidationCallback = TlsTrust.Callback(o.Mail.CaCertificatePath, o.Mail.CertificateThumbprint);
        var security = Enum.TryParse<SecureSocketOptions>(o.Mail.Security, true, out var s) ? s : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.Mail.Host, o.Mail.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(o.Mail.Username))
        {
            await client.AuthenticateAsync(o.Mail.Username, o.Mail.Password, ct);
        }
        await client.SendAsync(new SmtpSendRequest(message), ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// HTML der Benachrichtigungen. Bewusst mit Tabellen, festen Breiten und echten Zeilenumbrüchen:
    /// Outlook am PC stellt Mails mit Word dar und kennt weder max-width noch white-space:pre-wrap –
    /// mit div und pre-wrap schrumpfte der Text dort auf eine schmale Spalte bzw. eine einzige Zeile.
    /// </summary>
    internal static string Html(string headline, string? body, string link)
    {
        static string enc(string value) => WebUtility.HtmlEncode(value);
        const string font = "font-family:'Segoe UI',Arial,sans-serif";
        var lines = string.IsNullOrWhiteSpace(body)
            ? []
            : NotificationTexts.Shorten(body, 2000).ReplaceLineEndings("\n").Split('\n');
        var bodyHtml = lines.Length == 0
            ? ""
            : $"""<tr><td style="padding:0 24px 20px;{font};font-size:14px;line-height:22px;color:#424242">{string.Join("<br>", lines.Select(enc))}</td></tr>""";
        return $"""
            <!doctype html>
            <html lang="de"><head><meta charset="utf-8"><meta name="color-scheme" content="light only"></head>
            <body style="margin:0;padding:0;background:#f5f5f5">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f5f5f5" style="background:#f5f5f5">
                <tr><td align="center" style="padding:24px 12px">
                  <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:560px;max-width:100%;background:#ffffff;border:1px solid #e0e0e0;border-radius:8px">
                    <tr><td style="padding:24px 24px 4px;{font};font-size:12px;line-height:16px;color:#616161">Aufgaben</td></tr>
                    <tr><td style="padding:4px 24px 16px;{font};font-size:18px;line-height:24px;color:#242424">{enc(headline)}</td></tr>
                    {bodyHtml}
                    <tr><td style="padding:0 24px 24px">
                      <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                        <td bgcolor="#5b5fc7" style="background:#5b5fc7;border-radius:4px;padding:8px 16px;{font};font-size:14px;line-height:20px">
                          <a href="{enc(link)}" style="color:#ffffff;text-decoration:none;font-weight:600">Aufgabe öffnen</a>
                        </td>
                      </tr></table>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
    }
}

/// <summary>Prüft Serverzertifikate gegen eine interne Zertifizierungsstelle (für SMTP und IMAP).</summary>
public static class TlsTrust
{
    /// <summary>
    /// Prüfregel für TLS zum Mailserver. Ohne Angaben: Standardprüfung (öffentlich vertrauenswürdig, Name passt).
    /// Mit Fingerabdruck: genau dieses eine Serverzertifikat wird akzeptiert – auch selbst ausgestellt und
    /// unter anderem Namen (z. B. das Standardzertifikat von Exchange); jedes andere wird abgelehnt.
    /// Mit CA-Datei: Zertifikate dieser Zertifizierungsstelle, der Name muss passen.
    /// </summary>
    public static RemoteCertificateValidationCallback? Callback(string? caPath, string? thumbprint)
    {
        var pinned = NormalizeThumbprint(thumbprint);
        if (pinned.Length > 0)
        {
            return (_, cert, _, _) => cert is not null && Matches(cert, pinned);
        }
        if (!string.IsNullOrWhiteSpace(caPath))
        {
            var ca = LoadCa(caPath);
            return (_, cert, _, errors) => ValidateWithCustomCa(cert, errors, ca);
        }
        return null;
    }

    /// <summary>Fingerabdruck als Hex in Großbuchstaben, Doppelpunkte und Leerzeichen entfernt. SHA-256 (64) oder SHA-1 (40).</summary>
    public static string NormalizeThumbprint(string? value)
    {
        var text = (value ?? "").Replace(":", "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        if (text.Length == 0)
        {
            return "";
        }
        if (text.Length is not (40 or 64) || !text.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Zertifikat-Fingerabdruck: SHA-256 (64 Zeichen) oder SHA-1 (40 Zeichen) in Hex, Doppelpunkte erlaubt.");
        }
        return text;
    }

    /// <summary>Stimmt das Zertifikat mit dem hinterlegten Fingerabdruck überein (SHA-256 oder SHA-1, je nach Länge)?</summary>
    public static bool Matches(X509Certificate certificate, string pinned)
    {
        using var cert = new X509Certificate2(certificate);
        var actual = pinned.Length == 64 ? cert.GetCertHashString(HashAlgorithmName.SHA256) : cert.GetCertHashString(HashAlgorithmName.SHA1);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(pinned));
    }

    /// <summary>
    /// Lädt die CA-Datei. Eine PEM-Datei darf mehrere Zertifikate enthalten (Stamm- und
    /// Zwischenzertifikat einer zweistufigen PKI); DER-Dateien (.cer) enthalten genau eines.
    /// </summary>
    public static X509Certificate2Collection LoadCa(string path)
    {
        var all = new X509Certificate2Collection();
        if (File.ReadAllText(path).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            all.ImportFromPemFile(path);
        }
        else
        {
            all.Add(X509CertificateLoader.LoadCertificateFromFile(path));
        }
        return all;
    }

    public static bool ValidateWithCustomCa(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2Collection ca)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return false;
        }
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        // Selbstsignierte Zertifikate der Datei sind Vertrauensanker, die übrigen helfen beim Kettenbau.
        foreach (var c in ca)
        {
            if (c.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData))
            {
                chain.ChainPolicy.CustomTrustStore.Add(c);
            }
            else
            {
                chain.ChainPolicy.ExtraStore.Add(c);
            }
        }
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(new X509Certificate2(certificate));
    }
}
