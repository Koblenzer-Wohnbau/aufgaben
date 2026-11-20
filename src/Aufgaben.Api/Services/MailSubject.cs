using System.Globalization;
using System.Text.RegularExpressions;

namespace Aufgaben.Api.Services;

public sealed record ParsedSubject(
    string Title,
    string? ProjectQuery,
    string? PersonQuery,
    bool High,
    bool FollowUp,
    DateOnly? DueDate);

/// <summary>
/// Liest den Betreff einer weitergeleiteten Mail wie die Schnelleingabe:
/// "WG: Angebot prüfen #1001 @Klein !hoch morgen" → Titel, Projekt, Person, Priorität, Termin.
/// Bewusst kleiner als der Parser im Browser: Betreffzeilen sind kurz und meist fremd formuliert.
/// </summary>
public static partial class MailSubject
{
    [GeneratedRegex(@"^\s*(?:(?:AW|WG|RE|FW|FWD|Antw|Antwort|Weitergeleitet)\s*(?:\[\d+\])?\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex Prefixes();

    [GeneratedRegex(@"(?<=^|\s)#([\p{L}\p{N}][\p{L}\p{N}._-]*)")]
    private static partial Regex Project();

    [GeneratedRegex(@"(?<=^|\s)@([\p{L}\p{N}][\p{L}\p{N}._-]*)")]
    private static partial Regex Person();

    [GeneratedRegex(@"(?<=^|\s)(?:!hoch|!wichtig|!dringend|!!)(?=$|\s)", RegexOptions.IgnoreCase)]
    private static partial Regex High();

    [GeneratedRegex(@"(?<=^|\s)(?:wv|wiedervorlage)(?=$|[\s:])", RegexOptions.IgnoreCase)]
    private static partial Regex FollowUp();

    [GeneratedRegex(@"(?<=^|\s)(?:bis\s+|am\s+)?(\d{1,2})\.(\d{1,2})\.(\d{4}|\d{2})?(?=$|[\s,;!?)])", RegexOptions.IgnoreCase)]
    private static partial Regex Date();

    [GeneratedRegex(@"(?<=^|\s)(?:bis\s+)?(übermorgen|uebermorgen|morgen|heute)(?=$|[\s,;.!?)])", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDay();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static ParsedSubject Parse(string? subject, DateOnly today)
    {
        var text = " " + Prefixes().Replace(subject ?? "", "") + " ";
        string? project = null, person = null;
        DateOnly? due = null;
        bool high = false, followUp = false;

        text = Take(Project(), text, m => project = m.Groups[1].Value);
        text = Take(Person(), text, m => person = m.Groups[1].Value);
        text = Take(High(), text, _ => high = true);
        text = Take(FollowUp(), text, _ => followUp = true);

        var date = Date().Match(text);
        if (date.Success && TryDate(date, today, out var d))
        {
            due = d;
            text = text.Remove(date.Index, date.Length);
        }
        else
        {
            text = Take(RelativeDay(), text, m => due = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "heute" => today,
                "morgen" => today.AddDays(1),
                _ => today.AddDays(2),
            });
        }

        var title = Spaces().Replace(text, " ").Trim().Trim(',', ';', ':', '-', '–').Trim();
        if (title.Length == 0)
        {
            title = string.IsNullOrWhiteSpace(subject) ? "E-Mail ohne Betreff" : subject.Trim();
        }
        if (title.Length > TaskService.MaxTitle)
        {
            title = title[..(TaskService.MaxTitle - 1)] + "…";
        }
        return new ParsedSubject(title, project, person, high, followUp, due);
    }

    private static string Take(Regex regex, string text, Action<Match> apply)
    {
        var m = regex.Match(text);
        if (!m.Success)
        {
            return text;
        }
        apply(m);
        return text.Remove(m.Index, m.Length).Insert(m.Index, " ");
    }

    private static bool TryDate(Match m, DateOnly today, out DateOnly date)
    {
        date = default;
        var day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var hasYear = m.Groups[3].Success;
        var year = hasYear ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : today.Year;
        if (year < 100)
        {
            year += 2000;
        }
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }
        date = new DateOnly(year, month, day);
        if (!hasYear && date < today)
        {
            date = date.AddYears(1);
        }
        return true;
    }
}
