using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Aufgaben.Api.Services;

public sealed record InboundResult(string Outcome, int? TaskId, string Reason)
{
    public const string Created = "angelegt";
    public const string Rejected = "abgelehnt";
    public const string Duplicate = "doppelt";
}

/// <summary>
/// Macht aus einer Mail im Aufgaben-Postfach eine Aufgabe.
///
/// Sicherheit:
/// * Nur aktive, bekannte Personen (Absenderadresse = E-Mail oder UPN im Verzeichnis).
/// * Standard: nur Mails, die Exchange als intern authentifiziert gestempelt hat
///   (X-MS-Exchange-Organization-AuthAs: Internal). Gefälschte externe Absender fallen so raus.
/// * Keine Antwort an abgelehnte Absender (kein Backscatter), keine Verarbeitung automatischer Mails.
/// * Jede Mail wird genau einmal verarbeitet (Hash der Message-ID in inbound_mails).
/// </summary>
public sealed partial class MailInboxProcessor(
    AppDbContext db,
    CurrentUser current,
    AppClock clock,
    TaskService tasks,
    MailNotifier mail,
    IOptions<AufgabenOptions> options,
    ILogger<MailInboxProcessor> log)
{
    public const string ExchangeAuthHeader = "X-MS-Exchange-Organization-AuthAs";

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();

    public async Task<InboundResult> ProcessAsync(MimeMessage message, CancellationToken ct)
    {
        var o = options.Value.MailInbox;
        var from = message.From.Mailboxes.FirstOrDefault()?.Address?.Trim() ?? "";
        var subject = message.Subject ?? "";
        var key = MessageKey(message, from);

        if (await db.InboundMails.AnyAsync(m => m.MessageKey == key, ct))
        {
            return new InboundResult(InboundResult.Duplicate, null, "Bereits verarbeitet");
        }

        var autoSubmitted = message.Headers[HeaderId.AutoSubmitted];
        if (!string.IsNullOrEmpty(autoSubmitted) && !autoSubmitted.Trim().Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return await RejectAsync(key, message, from, "Automatisch erzeugte Nachricht", ct);
        }
        if (!string.IsNullOrEmpty(o.Address) && from.Equals(o.Address, StringComparison.OrdinalIgnoreCase))
        {
            return await RejectAsync(key, message, from, "Absender ist das Aufgaben-Postfach selbst", ct);
        }
        if (o.SenderCheck != "bekannte-absender")
        {
            var authAs = message.Headers[ExchangeAuthHeader]?.Trim();
            if (!string.Equals(authAs, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                return await RejectAsync(key, message, from, "Nicht als interne Mail authentifiziert", ct);
            }
        }

        var lower = from.ToLowerInvariant();
        var user = from.Length == 0
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.IsActive && (u.Email!.ToLower() == lower || u.Upn!.ToLower() == lower), ct);
        if (user is null)
        {
            return await RejectAsync(key, message, from, "Unbekannter Absender", ct);
        }

        current.Set(user, viaTeams: false);
        var parsed = MailSubject.Parse(subject, clock.Today);
        var hints = new List<string>();

        int? projectId = null;
        if (parsed.ProjectQuery is { } pq)
        {
            projectId = await FindProjectAsync(pq, ct);
            if (projectId is null) hints.Add($"Projekt „#{pq}“ wurde nicht gefunden.");
        }
        int? assigneeId = null;
        if (parsed.PersonQuery is { } uq)
        {
            assigneeId = await FindUserAsync(uq, ct);
            if (assigneeId is null) hints.Add($"Person „@{uq}“ wurde nicht gefunden – die Aufgabe liegt bei dir.");
        }

        var notes = BuildNotes(message, user, hints);
        var created = await tasks.CreateAsync(new CreateTaskRequest(
            Title: parsed.Title,
            Notes: notes,
            Priority: parsed.High ? "hoch" : "normal",
            Kind: parsed.FollowUp ? "wiedervorlage" : "aufgabe",
            AssigneeId: assigneeId,
            ListId: null,
            ProjectId: projectId,
            DueDate: parsed.DueDate?.ToString("yyyy-MM-dd"),
            DueTime: null,
            RemindAt: null,
            MyDay: null,
            Recurrence: null,
            Checklist: null), "mail", ct);

        var skipped = await SaveAttachmentsAsync(created.Id, message, o.MaxAttachmentMb, ct);
        if (skipped.Count > 0)
        {
            var task = await db.Tasks.FirstAsync(t => t.Id == created.Id, ct);
            task.Notes += $"\n\nNicht übernommene Anhänge (zu groß): {string.Join(", ", skipped)}";
        }

        db.InboundMails.Add(new InboundMail
        {
            MessageKey = key,
            FromAddress = Truncate(from, 320),
            Subject = Truncate(subject, 500),
            ReceivedAt = message.Date == DateTimeOffset.MinValue ? clock.UtcNow : message.Date.ToUniversalTime(),
            ProcessedAt = clock.UtcNow,
            Result = InboundResult.Created,
            TaskId = created.Id,
        });
        await db.SaveChangesAsync(ct);

        if (o.SendConfirmation && mail.IsAvailable)
        {
            try
            {
                var dto = (await tasks.GetAsync(created.Id, ct)).Task;
                await mail.SendTaskCreatedAsync(user, dto, message, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Bestätigung für Aufgabe {TaskId} konnte nicht gesendet werden", created.Id);
            }
        }
        log.LogInformation("Aufgabe {TaskId} aus E-Mail von Person {UserId} angelegt", created.Id, user.Id);
        return new InboundResult(InboundResult.Created, created.Id, "");
    }

    private async Task<InboundResult> RejectAsync(string key, MimeMessage message, string from, string reason, CancellationToken ct)
    {
        db.InboundMails.Add(new InboundMail
        {
            MessageKey = key,
            FromAddress = Truncate(from, 320),
            Subject = Truncate(message.Subject ?? "", 500),
            ReceivedAt = message.Date == DateTimeOffset.MinValue ? clock.UtcNow : message.Date.ToUniversalTime(),
            ProcessedAt = clock.UtcNow,
            Result = InboundResult.Rejected,
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Mail von {From} abgelehnt: {Reason}", from, reason);
        return new InboundResult(InboundResult.Rejected, null, reason);
    }

    private async Task<int?> FindProjectAsync(string query, CancellationToken ct)
    {
        var q = query.ToLowerInvariant();
        // Feste Reihenfolge, damit bei mehreren Treffern immer derselbe gewinnt.
        var open = db.Projects.Where(p => !p.Archived).OrderBy(p => p.Number).ThenBy(p => p.Name).ThenBy(p => p.Id);
        return await open.Where(p => p.Number != null && p.Number.ToLower() == q).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct)
               ?? await open.Where(p => p.Number != null && p.Number.ToLower().StartsWith(q)).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct)
               ?? await open.Where(p => p.Name.ToLower().StartsWith(q)).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> FindUserAsync(string query, CancellationToken ct)
    {
        var q = query.ToLowerInvariant();
        var users = await db.Users.Where(u => u.IsActive).OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct);
        return users.FirstOrDefault(u => u.DisplayName.ToLowerInvariant().Split(' ').Contains(q))?.Id
               ?? users.FirstOrDefault(u => u.DisplayName.ToLowerInvariant().Split(' ').Any(p => p.StartsWith(q)))?.Id
               ?? users.FirstOrDefault(u => u.Email != null && u.Email.ToLowerInvariant().StartsWith(q))?.Id;
    }

    private string BuildNotes(MimeMessage message, User sender, List<string> hints)
    {
        var date = message.Date == DateTimeOffset.MinValue ? clock.UtcNow : message.Date;
        var sb = new StringBuilder();
        sb.Append("Per E-Mail von ").Append(sender.DisplayName).Append(" am ")
          .Append(clock.ToLocal(date.ToUniversalTime()).ToString("dd.MM.yyyy HH:mm")).Append('\n');
        foreach (var hint in hints)
        {
            sb.Append("Hinweis: ").Append(hint).Append('\n');
        }

        var body = message.TextBody;
        if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            body = HtmlToPlainText(message.HtmlBody);
        }
        body = ManyBlankLines().Replace((body ?? "").Replace("\r\n", "\n").Trim(), "\n\n");
        if (body.Length > 0)
        {
            sb.Append('\n').Append(body);
        }
        var notes = sb.ToString();
        return notes.Length > TaskService.MaxNotes - 500 ? notes[..(TaskService.MaxNotes - 501)] + "…" : notes;
    }

    private async Task<List<string>> SaveAttachmentsAsync(int taskId, MimeMessage message, int maxMb, CancellationToken ct)
    {
        var skipped = new List<string>();
        long total = 0;
        var limit = Math.Max(1, maxMb) * 1024L * 1024L;
        foreach (var entity in message.Attachments)
        {
            string name;
            byte[] data;
            string contentType;
            using (var ms = new MemoryStream())
            {
                if (entity is MessagePart { Message: { } inner })
                {
                    // Als Anhang weitergeleitete Mail → .eml
                    await inner.WriteToAsync(ms, ct);
                    name = SafeFileName((inner.Subject ?? "Nachricht") + ".eml");
                    contentType = "message/rfc822";
                }
                else if (entity is MimePart { Content: { } content } part)
                {
                    await content.DecodeToAsync(ms, ct);
                    name = SafeFileName(part.FileName ?? "anhang");
                    contentType = part.ContentType.MimeType;
                }
                else
                {
                    continue;
                }
                data = ms.ToArray();
            }
            if (total + data.Length > limit)
            {
                skipped.Add(name);
                continue;
            }
            total += data.Length;
            var attachment = new TaskAttachment
            {
                TaskId = taskId,
                FileName = name,
                ContentType = Truncate(contentType, 127),
                Size = data.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(data)),
                CreatedAt = clock.UtcNow,
            };
            db.TaskAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            db.AttachmentBlobs.Add(new AttachmentBlob { AttachmentId = attachment.Id, Data = data });
            await db.SaveChangesAsync(ct);
        }
        return skipped;
    }

    internal static string MessageKey(MimeMessage message, string from)
    {
        var raw = !string.IsNullOrWhiteSpace(message.MessageId)
            ? "id:" + message.MessageId
            : $"fallback:{from}|{message.Date:O}|{message.Subject}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    [GeneratedRegex(@"<(script|style|head)[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlBlocks();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    /// <summary>Einfache, sichere Umwandlung von HTML-Mails in Text (kein HTML landet in der Aufgabe).</summary>
    internal static string HtmlToPlainText(string html)
    {
        var text = HtmlBlocks().Replace(html, " ");
        text = HtmlBreaks().Replace(text, "\n");
        text = HtmlTags().Replace(text, "");
        text = System.Net.WebUtility.HtmlDecode(text).Replace(' ', ' ');
        return TrailingSpaces().Replace(text, "\n");
    }

    internal static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "anhang";
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Holt neue Mails per IMAP ab und verschiebt sie danach in "Verarbeitet" bzw. "Abgelehnt".</summary>
public sealed class MailInboxPoller(IServiceScopeFactory scopes, IOptions<AufgabenOptions> options, AppClock clock, ILogger<MailInboxPoller> log)
{
    public static DateTimeOffset? LastRun { get; private set; }
    public static string? LastResult { get; private set; }

    public bool IsConfigured => options.Value.MailInbox.IsConfigured;

    public async Task<int> PollAsync(CancellationToken ct)
    {
        var o = options.Value.MailInbox;
        if (!o.IsConfigured)
        {
            return 0;
        }
        using var client = new ImapClient();
        if (!string.IsNullOrWhiteSpace(o.CaCertificatePath))
        {
            var ca = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(o.CaCertificatePath);
            client.ServerCertificateValidationCallback = (_, cert, _, errors) => TlsTrust.ValidateWithCustomCa(cert, errors, ca);
        }
        var security = Enum.TryParse<SecureSocketOptions>(o.Security, true, out var s) ? s : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(o.Host, o.Port, security, ct);
        await client.AuthenticateAsync(o.Username, o.Password, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        var uids = await inbox.SearchAsync(SearchQuery.All, ct);
        int created = 0, rejected = 0;
        if (uids.Count > 0)
        {
            var processedFolder = await GetOrCreateFolderAsync(client, o.ProcessedFolder, ct);
            var rejectedFolder = await GetOrCreateFolderAsync(client, o.RejectedFolder, ct);
            foreach (var uid in uids.Take(25))
            {
                var message = await inbox.GetMessageAsync(uid, ct);
                InboundResult result;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    result = await scope.ServiceProvider.GetRequiredService<MailInboxProcessor>().ProcessAsync(message, ct);
                }
                if (result.Outcome == InboundResult.Rejected)
                {
                    rejected++;
                    await inbox.MoveToAsync(uid, rejectedFolder, ct);
                }
                else
                {
                    if (result.Outcome == InboundResult.Created) created++;
                    await inbox.MoveToAsync(uid, processedFolder, ct);
                }
            }
        }
        await client.DisconnectAsync(true, ct);
        LastRun = clock.UtcNow;
        LastResult = $"{created} angelegt, {rejected} abgelehnt";
        if (created + rejected > 0)
        {
            log.LogInformation("Aufgaben-Postfach: {Result}", LastResult);
        }
        return created;
    }

    private static async Task<IMailFolder> GetOrCreateFolderAsync(ImapClient client, string name, CancellationToken ct)
    {
        var root = client.GetFolder(client.PersonalNamespaces[0]);
        try
        {
            return await root.GetSubfolderAsync(name, ct);
        }
        catch (FolderNotFoundException)
        {
            return await root.CreateAsync(name, true, ct)
                   ?? throw new InvalidOperationException($"IMAP-Ordner „{name}“ konnte nicht angelegt werden.");
        }
    }
}
