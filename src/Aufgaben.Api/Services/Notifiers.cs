using System.Net;
using System.Net.Security;
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
        NotificationOutbox.Digest => ("dailyDigest", n.Title, n.Title),
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
        if (type is "taskAssigned" or "taskCompleted" or "taskCommented")
        {
            parameters.Add(new { name = "person", value = n.ActorName });
        }
        if (type == "dailyDigest")
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

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(o.Mail.CaCertificatePath))
        {
            var ca = X509CertificateLoader.LoadCertificateFromFile(o.Mail.CaCertificatePath);
            client.ServerCertificateValidationCallback = (_, cert, _, errors) => ValidateWithCustomCa(cert, errors, ca);
        }
        var security = Enum.TryParse<SecureSocketOptions>(o.Mail.Security, true, out var s) ? s : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(o.Mail.Host, o.Mail.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(o.Mail.Username))
        {
            await client.AuthenticateAsync(o.Mail.Username, o.Mail.Password, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        log.LogInformation("E-Mail ({Kind}) an Person {UserId} gesendet", n.Kind, user.Id);
    }

    private static bool ValidateWithCustomCa(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2 ca)
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
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(new X509Certificate2(certificate));
    }

    private static string Html(string headline, string body, string link)
    {
        static string enc(string value) => WebUtility.HtmlEncode(value);
        var bodyHtml = string.IsNullOrWhiteSpace(body)
            ? ""
            : $"<p style=\"margin:0 0 16px;color:#424242;white-space:pre-wrap\">{enc(NotificationTexts.Shorten(body, 2000))}</p>";
        return $"""
            <!doctype html>
            <html lang="de"><body style="margin:0;padding:24px;background:#f5f5f5;font-family:Segoe UI,Arial,sans-serif">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:24px;border:1px solid #e0e0e0">
                <p style="margin:0 0 8px;font-size:12px;color:#616161">Aufgaben</p>
                <p style="margin:0 0 16px;font-size:18px;color:#242424">{enc(headline)}</p>
                {bodyHtml}
                <a href="{enc(link)}" style="display:inline-block;background:#5b5fc7;color:#ffffff;text-decoration:none;padding:8px 16px;border-radius:4px">Aufgabe öffnen</a>
              </div>
            </body></html>
            """;
    }
}
