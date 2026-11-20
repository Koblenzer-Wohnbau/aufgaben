using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;

namespace Aufgaben.Api.Services;

/// <summary>
/// Persönlicher Kalender-Feed (iCalendar). Outlook kann ihn als zusätzlichen Kalender
/// abonnieren – Aufgaben mit Termin erscheinen dann neben den echten Terminen, ohne
/// den eigenen Kalender zu füllen. Der Link enthält ein zufälliges Geheimnis; in der
/// Datenbank liegt nur dessen SHA-256-Hash.
/// </summary>
public static class CalendarFeed
{
    public static (string Token, string Hash) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Build(IEnumerable<TaskItem> tasks, AppClock clock, string baseUrl)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//Aufgaben//Kalender-Feed//DE");
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "METHOD:PUBLISH");
        Line(sb, "X-WR-CALNAME:Aufgaben");
        Line(sb, "X-PUBLISHED-TTL:PT1H");
        Line(sb, "REFRESH-INTERVAL;VALUE=DURATION:PT1H");

        var stamp = clock.UtcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var t in tasks)
        {
            if (t.DueDate is not { } due)
            {
                continue;
            }
            Line(sb, "BEGIN:VEVENT");
            Line(sb, $"UID:aufgabe-{t.Id}@aufgaben");
            Line(sb, $"DTSTAMP:{stamp}");
            if (t.DueTime is { } time)
            {
                var start = clock.ToUtc(due, time).UtcDateTime;
                Line(sb, "DTSTART:" + start.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
                Line(sb, "DTEND:" + start.AddMinutes(30).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
            }
            else
            {
                Line(sb, "DTSTART;VALUE=DATE:" + due.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                Line(sb, "DTEND;VALUE=DATE:" + due.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            }
            var prefix = t.Kind == "wiedervorlage" ? "Wiedervorlage: " : "Aufgabe: ";
            Line(sb, "SUMMARY:" + Escape(prefix + t.Title));
            var description = new StringBuilder();
            if (t.Project is not null)
            {
                description.Append("Projekt: ").Append(t.Project.Label).Append('\n');
            }
            if (t.CreatedById != t.AssigneeId)
            {
                description.Append("Von: ").Append(t.CreatedBy.DisplayName).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(t.Notes))
            {
                description.Append('\n').Append(t.Notes.Length > 1000 ? t.Notes[..1000] + "…" : t.Notes).Append('\n');
            }
            description.Append('\n').Append(baseUrl).Append("/#/aufgabe/").Append(t.Id);
            Line(sb, "DESCRIPTION:" + Escape(description.ToString()));
            Line(sb, $"URL:{baseUrl}/#/aufgabe/{t.Id}");
            Line(sb, "TRANSP:TRANSPARENT");
            Line(sb, "STATUS:CONFIRMED");
            if (t.Priority == "hoch")
            {
                Line(sb, "PRIORITY:1");
            }
            Line(sb, "END:VEVENT");
        }
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    internal static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "");

    /// <summary>Schreibt eine Zeile und faltet sie nach RFC 5545 bei 75 Oktetten.</summary>
    internal static void Line(StringBuilder sb, string line)
    {
        var bytes = 0;
        var first = true;
        var current = new StringBuilder();
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            var limit = first ? 75 : 74;
            if (bytes + size > limit)
            {
                sb.Append(current).Append("\r\n ");
                current.Clear();
                bytes = 0;
                first = false;
            }
            current.Append(rune.ToString());
            bytes += size;
        }
        sb.Append(current).Append("\r\n");
    }
}
