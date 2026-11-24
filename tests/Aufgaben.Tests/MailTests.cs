using System.Net.Http.Json;
using System.Text;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Aufgaben.Tests;

public class MailSubjectTests
{
    private static readonly DateOnly Today = new(2026, 11, 20); // Freitag

    [Fact]
    public void Removes_reply_and_forward_prefixes()
    {
        Assert.Equal("Angebot Dachdecker", MailSubject.Parse("WG: AW: Fwd: Angebot Dachdecker", Today).Title);
        Assert.Equal("Termin", MailSubject.Parse("RE[2]: Termin", Today).Title);
    }

    [Fact]
    public void Reads_project_person_priority_and_follow_up()
    {
        var p = MailSubject.Parse("WG: Angebot prüfen #1001 @Klein !hoch wv", Today);
        Assert.Equal("Angebot prüfen", p.Title);
        Assert.Equal("1001", p.ProjectQuery);
        Assert.Equal("Klein", p.PersonQuery);
        Assert.True(p.High);
        Assert.True(p.FollowUp);
    }

    [Theory]
    [InlineData("Rechnung prüfen morgen", "2026-11-21")]
    [InlineData("Rechnung prüfen bis 30.11.", "2026-11-30")]
    [InlineData("Frist 15.01.2027", "2027-01-15")]
    [InlineData("Jahresgespräch 10.2.", "2027-02-10")]
    public void Reads_due_dates(string subject, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), MailSubject.Parse(subject, Today).DueDate);
    }

    [Theory]
    [InlineData("Fw: Komplett PDF bitte Aufgabe erstellen. 5 tage", "2026-11-25", "Komplett PDF bitte Aufgabe erstellen.")]
    [InlineData("Nachfassen in 3 Tagen", "2026-11-23", "Nachfassen")]
    [InlineData("Rückruf in 1 Tag", "2026-11-23", "Rückruf")] // Samstag → Montag
    [InlineData("WV Gewährleistung Dach in 2 Wochen", "2026-12-04", "Gewährleistung Dach")]
    [InlineData("Mietspiegel prüfen 1 Monat", "2026-12-21", "Mietspiegel prüfen")] // Sonntag → Montag
    [InlineData("Angebot prüfen bis Freitag", "2026-11-27", "Angebot prüfen")]
    [InlineData("Montag Zähler ablesen", "2026-11-23", "Zähler ablesen")]
    [InlineData("Bericht nächste Woche", "2026-11-23", "Bericht")]
    [InlineData("Unterlagen prüfen Monatsende", "2026-11-30", "Unterlagen prüfen")]
    [InlineData("Abrechnung Ende der Woche", "2026-11-20", "Abrechnung")]
    public void Understands_the_same_dates_as_the_quick_add(string subject, string due, string title)
    {
        var p = MailSubject.Parse(subject, Today);
        Assert.Equal(DateOnly.Parse(due), p.DueDate);
        Assert.Equal(title, p.Title);
    }

    [Fact]
    public void Reads_time_recurrence_and_low_priority_like_the_quick_add()
    {
        var walk = MailSubject.Parse("Baustellenrundgang jeden Montag 8 Uhr", Today);
        Assert.Equal("Baustellenrundgang", walk.Title);
        Assert.Equal("woechentlich", walk.Recurrence);
        Assert.Equal(new DateOnly(2026, 11, 23), walk.DueDate);
        Assert.Equal(new TimeOnly(8, 0), walk.DueTime);

        var visit = MailSubject.Parse("Begehung 10.12. um 14:30 !niedrig", Today);
        Assert.Equal(new DateOnly(2026, 12, 10), visit.DueDate);
        Assert.Equal(new TimeOnly(14, 30), visit.DueTime);
        Assert.True(visit.Low);

        // Uhrzeit ohne Datum: heute, oder morgen, wenn sie schon vorbei ist
        Assert.Equal(Today, MailSubject.Parse("Rückruf 15 Uhr", Today, new TimeOnly(9, 0)).DueDate);
        Assert.Equal(Today.AddDays(1), MailSubject.Parse("Rückruf 15 Uhr", Today, new TimeOnly(16, 0)).DueDate);
    }

    [Theory]
    [InlineData("@Becker bitte bis Freitag.\n\nViele Grüße\nAnna\n\n________________________________\nVon: Fa. Müller <info@example.test>\nGesendet: Donnerstag, 19. November 2026 10:00\nBetreff: Angebot\n\nBitte bis 30.12. antworten.")]
    [InlineData("@Becker bitte bis Freitag.\r\n\r\n-----Ursprüngliche Nachricht-----\r\nVon: Fa. Müller\r\nBitte bis 30.12. antworten.")]
    [InlineData("@Becker bitte bis Freitag.\n\n-----Original Message-----\nFrom: Fa. Müller\nPlease reply by 30.12.")]
    [InlineData("@Becker bitte bis Freitag.\n\n---------- Forwarded message ---------\nVon: Fa. Müller\nBitte bis 30.12.")]
    [InlineData("@Becker bitte bis Freitag.\n\nAm 19.11.2026 um 10:00 schrieb Fa. Müller:\n> Bitte bis 30.12. antworten.")]
    [InlineData("@Becker bitte bis Freitag.\n--\nAnna Schneider | Tel. 0261 123 30.12.")]
    public void Own_text_above_the_forwarded_mail_counts_but_the_forwarded_mail_does_not(string body)
    {
        var own = MailSubject.OwnText(body);
        Assert.Equal("@Becker bitte bis Freitag.", own);

        var p = MailSubject.Merge(MailSubject.Parse("WG: Angebot prüfen", Today), MailSubject.ParseText(own, Today));
        Assert.Equal("Angebot prüfen", p.Title);
        Assert.Equal("Becker", p.PersonQuery);
        Assert.Equal(new DateOnly(2026, 11, 27), p.DueDate);
    }

    [Fact]
    public void Subject_wins_over_own_text()
    {
        var p = MailSubject.Merge(
            MailSubject.Parse("WG: Angebot @Müller morgen !hoch", Today),
            MailSubject.ParseText("@Becker bitte Freitag !niedrig #1001", Today));
        Assert.Equal("Müller", p.PersonQuery);
        Assert.Equal(Today.AddDays(1), p.DueDate);
        Assert.True(p.High);
        Assert.False(p.Low);
        Assert.Equal("1001", p.ProjectQuery); // fehlte im Betreff
        Assert.Equal("", MailSubject.OwnText("   \n"));
    }

    [Fact]
    public void Leaves_ordinary_words_and_invalid_dates_alone()
    {
        var p = MailSubject.Parse("Morgenrunde Version 31.02. besprechen", Today);
        Assert.Null(p.DueDate);
        Assert.Equal("Morgenrunde Version 31.02. besprechen", p.Title);
    }

    [Fact]
    public void Empty_subject_gets_a_fallback_title()
    {
        Assert.Equal("E-Mail ohne Betreff", MailSubject.Parse("", Today).Title);
        Assert.Equal("E-Mail ohne Betreff", MailSubject.Parse(null, Today).Title);
    }

    [Fact]
    public void Html_mails_become_plain_text_without_scripts()
    {
        var text = MailInboxProcessor.HtmlToPlainText("<html><head><style>p{}</style></head><body><p>Hallo&nbsp;Team</p><script>alert(1)</script><p>Zeile&nbsp;2<br>Zeile 3</p></body></html>");
        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain("<", text);
        Assert.Contains("Hallo Team", text);
        Assert.Contains("Zeile 3", text);
    }

    [Fact]
    public void Notification_html_uses_real_line_breaks_for_outlook()
    {
        // Outlook am PC ignoriert white-space:pre-wrap und max-width, jede Zeile braucht ein <br>.
        var html = MailNotifier.Html("Neue Aufgabe", "Projekt: 1001 Lindenpark\r\nAnhaenge: 1\n<b>fett</b>", "https://aufgaben.example.test/#/aufgabe/7");
        Assert.Contains("Projekt: 1001 Lindenpark<br>Anhaenge: 1<br>&lt;b&gt;fett&lt;/b&gt;", html);
        Assert.DoesNotContain("pre-wrap", html);
        Assert.DoesNotContain("<div", html);
        Assert.Contains("width=\"560\"", html);
        Assert.Contains("href=\"https://aufgaben.example.test/#/aufgabe/7\"", html);
        Assert.DoesNotContain("<br>", MailNotifier.Html("Nur Kopf", null, "https://x.test/"));
    }

    [Fact]
    public void File_names_are_sanitized()
    {
        Assert.Equal("_.._secret.txt", MailInboxProcessor.SafeFileName("/../secret.txt").Replace('\\', '_'));
        Assert.Equal("anhang", MailInboxProcessor.SafeFileName("   "));
    }
}

[Collection("api")]
public sealed class MailInboxTests(AppFixture app)
{
    private static MimeMessage Mail(string from, string subject, string body = "Bitte erledigen.", bool internalStamp = true, string? messageId = null)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("aufgaben@example.test"));
        message.Subject = subject;
        message.MessageId = messageId ?? $"{Guid.NewGuid():N}@example.test";
        message.Date = DateTimeOffset.UtcNow;
        if (internalStamp)
        {
            message.Headers.Add(MailInboxProcessor.ExchangeAuthHeader, "Internal");
        }
        var builder = new BodyBuilder { TextBody = body };
        builder.Attachments.Add("Angebot.txt", Encoding.UTF8.GetBytes("Angebot über 1.000 €"), ContentType.Parse("text/plain"));
        message.Body = builder.ToMessageBody();
        return message;
    }

    private async Task<InboundResult> Process(MimeMessage message)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MailInboxProcessor>().ProcessAsync(message, CancellationToken.None);
    }

    [Fact]
    public async Task Internal_mail_from_known_sender_becomes_a_task_with_attachment()
    {
        var result = await Process(Mail("anna.schneider@example.test", "WG: Angebot Fensterbau prüfen #1001 !hoch morgen", "Text der Mail"));
        Assert.Equal(InboundResult.Created, result.Outcome);

        var task = await app.WithDbAsync(db => db.Tasks.Include(t => t.Attachments).Include(t => t.Project).SingleAsync(t => t.Id == result.TaskId));
        Assert.Equal("Angebot Fensterbau prüfen", task.Title);
        Assert.Equal("mail", task.Source);
        Assert.Equal("hoch", task.Priority);
        Assert.Equal("1001", task.Project?.Number);
        Assert.NotNull(task.DueDate);
        Assert.Contains("Text der Mail", task.Notes);
        var attachment = Assert.Single(task.Attachments);
        Assert.Equal("Angebot.txt", attachment.FileName);

        var blob = await app.WithDbAsync(db => db.AttachmentBlobs.SingleAsync(b => b.AttachmentId == attachment.Id));
        Assert.Equal("Angebot über 1.000 €", Encoding.UTF8.GetString(blob.Data));
    }

    [Fact]
    public async Task Person_in_subject_delegates_and_notifies()
    {
        var result = await Process(Mail("anna.schneider@example.test", "Schlüssel bestellen @Wagner"));
        var task = await app.WithDbAsync(db => db.Tasks.Include(t => t.Assignee).SingleAsync(t => t.Id == result.TaskId));
        Assert.Equal("Jonas Wagner", task.Assignee.DisplayName);
        var notified = await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TaskId == task.Id && n.UserId == task.AssigneeId && n.Kind == NotificationOutbox.Assigned));
        Assert.True(notified);
    }

    [Theory]
    [InlineData("dev-anna", "anna.schneider@example.test", true, "geplant")]
    [InlineData("dev-thomas", "thomas.becker@example.test", false, "aufgaben")]
    public async Task Forwarded_mail_without_a_date_is_visible_for_both_inbox_settings(
        string oid, string sender, bool hasDefaultDueDate, string view)
    {
        var result = await Process(Mail(sender, "Interne Unterlagen prüfen"));
        var task = await app.WithDbAsync(db => db.Tasks.SingleAsync(t => t.Id == result.TaskId));
        var today = app.Factory.Services.GetRequiredService<AppClock>().Today;
        Assert.Equal(hasDefaultDueDate ? today : (DateOnly?)null, task.DueDate);
        Assert.Null(task.DueTime);

        using var client = await app.LoginAsync(oid);
        var tasks = await client.GetFromJsonAsync<List<TaskDto>>($"/api/tasks?view={view}");
        Assert.Contains(tasks!, t => t.Id == result.TaskId);
    }

    [Fact]
    public async Task Own_text_above_a_forwarded_mail_sets_person_and_due_date()
    {
        const string body = "@Wagner bitte bis Freitag erledigen.\n\nViele Grüße\nAnna\n\n-----Ursprüngliche Nachricht-----\nVon: Fa. Müller\nBitte bis 30.12. antworten.";
        var result = await Process(Mail("anna.schneider@example.test", "WG: Angebot Fensterbau prüfen", body));
        var task = await app.WithDbAsync(db => db.Tasks.Include(t => t.Assignee).SingleAsync(t => t.Id == result.TaskId));

        Assert.Equal("Angebot Fensterbau prüfen", task.Title);
        Assert.Equal("Jonas Wagner", task.Assignee.DisplayName);
        Assert.Equal(DayOfWeek.Friday, task.DueDate!.Value.DayOfWeek);
        Assert.False(task.DueDate.Value is { Month: 12, Day: 30 }); // nicht der 30.12. aus der weitergeleiteten Mail
        Assert.Contains("Bitte bis 30.12. antworten.", task.Notes); // der ganze Text bleibt in den Notizen
    }

    [Fact]
    public async Task Unknown_sender_is_rejected()
    {
        var result = await Process(Mail("fremd@example.org", "Bitte zahlen Sie sofort"));
        Assert.Equal(InboundResult.Rejected, result.Outcome);
        Assert.Equal("Unbekannter Absender", result.Reason);
    }

    [Fact]
    public async Task Spoofed_sender_without_exchange_stamp_is_rejected()
    {
        var result = await Process(Mail("anna.schneider@example.test", "Gefälschter Absender", internalStamp: false));
        Assert.Equal(InboundResult.Rejected, result.Outcome);
        var created = await app.WithDbAsync(db => db.Tasks.AnyAsync(t => t.Title == "Gefälschter Absender"));
        Assert.False(created);
    }

    [Fact]
    public async Task Automatic_replies_are_ignored()
    {
        var message = Mail("anna.schneider@example.test", "Abwesenheitsnotiz: Urlaub");
        message.Headers.Add(HeaderId.AutoSubmitted, "auto-replied");
        Assert.Equal(InboundResult.Rejected, (await Process(message)).Outcome);
    }

    [Fact]
    public async Task The_same_mail_is_processed_only_once()
    {
        var id = $"{Guid.NewGuid():N}@example.test";
        var first = await Process(Mail("thomas.becker@example.test", "Doppelt gesendet", messageId: id));
        var second = await Process(Mail("thomas.becker@example.test", "Doppelt gesendet", messageId: id));
        Assert.Equal(InboundResult.Created, first.Outcome);
        Assert.Equal(InboundResult.Duplicate, second.Outcome);
        var count = await app.WithDbAsync(db => db.Tasks.CountAsync(t => t.Title == "Doppelt gesendet"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Attachments_are_only_downloadable_for_people_who_can_see_the_task()
    {
        var result = await Process(Mail("maria.klein@example.test", "Privates Angebot"));
        var attachmentId = await app.WithDbAsync(db => db.TaskAttachments.Where(a => a.TaskId == result.TaskId).Select(a => a.Id).SingleAsync());

        var maria = await app.LoginAsync("dev-maria");
        var ok = await maria.GetAsync($"/api/tasks/{result.TaskId}/attachments/{attachmentId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/octet-stream", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", ok.Content.Headers.ContentDisposition?.DispositionType);
        // Die strenge Regel des Endpunkts darf nicht von der allgemeinen CSP überschrieben werden.
        Assert.Equal("default-src 'none'; sandbox", Assert.Single(ok.Headers.GetValues("Content-Security-Policy")));

        var thomas = await app.LoginAsync("dev-thomas");
        var denied = await thomas.GetAsync($"/api/tasks/{result.TaskId}/attachments/{attachmentId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, denied.StatusCode);
    }
}
