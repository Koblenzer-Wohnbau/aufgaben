using System.Globalization;
using System.Text.RegularExpressions;

namespace Aufgaben.Api.Services;

public sealed record ParsedSubject(
    string Title,
    string? ProjectQuery,
    string? PersonQuery,
    bool High,
    bool FollowUp,
    DateOnly? DueDate,
    bool Low = false,
    TimeOnly? DueTime = null,
    string Recurrence = "");

/// <summary>
/// Liest Betreff und eigenen Text einer Mail an „Mail an Aufgabe“ mit denselben Regeln wie die Schnelleingabe
/// in der App (frontend/src/quickadd.ts): Termin, Uhrzeit, Projekt (#), Person (@), Priorität (!),
/// Wiedervorlage und Wiederholung. Beispiel: "WG: Angebot prüfen #1001 @Klein !hoch in 5 Tagen".
/// </summary>
public static partial class MailSubject
{
    [GeneratedRegex(@"^\s*(?:(?:AW|WG|RE|FW|FWD|Antw|Antwort|Weitergeleitet)\s*(?:\[\d+\])?\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex Prefixes();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    // Wortgrenzen wie in der Schnelleingabe, die auch mit Umlauten funktionieren.
    private const string B = @"(?<=^|[\s,;(])";
    private const string E = @"(?=$|[\s,;.!?)])";

    private static Regex Re(string body) =>
        new(B + "(?:" + body + ")" + E, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["montag"] = DayOfWeek.Monday,
        ["dienstag"] = DayOfWeek.Tuesday,
        ["mittwoch"] = DayOfWeek.Wednesday,
        ["donnerstag"] = DayOfWeek.Thursday,
        ["freitag"] = DayOfWeek.Friday,
        ["samstag"] = DayOfWeek.Saturday,
        ["sonntag"] = DayOfWeek.Sunday,
    };

    private const string WeekdayNames = "montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonntag";

    private sealed class State
    {
        public string? Project;
        public string? Person;
        public bool High;
        public bool Low;
        public bool FollowUp;
        public DateOnly? Due;
        public TimeOnly? Time;
        public string Recurrence = "";
    }

    private sealed record Rule(string Type, Regex Pattern, Func<Match, State, DateOnly, bool> Apply);

    /// <summary>Dieselbe Reihenfolge wie in der Schnelleingabe: Datum vor Uhrzeit, damit "10.11." kein 10:11 wird.</summary>
    private static readonly Rule[] Rules =
    [
        new("recurrence", Re(@"jeden\s+(" + WeekdayNames + ")"), (m, s, today) =>
        {
            s.Recurrence = "woechentlich";
            s.Due ??= NextWeekday(today, Weekdays[m.Groups[1].Value], allowToday: true);
            return true;
        }),
        new("recurrence", Re(@"jeden\s+tag|täglich|taeglich"), (_, s, _) => Set(() => s.Recurrence = "taeglich")),
        new("recurrence", Re(@"werktags|jeden\s+werktag"), (_, s, _) => Set(() => s.Recurrence = "werktags")),
        new("recurrence", Re(@"jede\s+woche|wöchentlich|woechentlich"), (_, s, _) => Set(() => s.Recurrence = "woechentlich")),
        new("recurrence", Re(@"jeden\s+monat|monatlich"), (_, s, _) => Set(() => s.Recurrence = "monatlich")),
        new("recurrence", Re(@"jedes\s+jahr|jährlich|jaehrlich"), (_, s, _) => Set(() => s.Recurrence = "jaehrlich")),
        new("kind", Re(@"wv|wiedervorlage"), (_, s, _) => Set(() => s.FollowUp = true)),
        new("priority", new Regex(@"(?<=^|\s)(?:!hoch|!wichtig|!dringend|!!)(?=$|\s)", RegexOptions.IgnoreCase), (_, s, _) => Set(() => s.High = true)),
        new("priority", new Regex(@"(?<=^|\s)!niedrig(?=$|\s)", RegexOptions.IgnoreCase), (_, s, _) => Set(() => s.Low = true)),
        new("project", new Regex(@"(?<=^|\s)#([\p{L}\p{N}][\p{L}\p{N}._-]*)"), (m, s, _) => Set(() => s.Project = m.Groups[1].Value)),
        new("person", new Regex(@"(?<=^|\s)@([\p{L}\p{N}][\p{L}\p{N}._-]*)"), (m, s, _) => Set(() => s.Person = m.Groups[1].Value)),
        new("date", Re(@"(?:bis\s+|am\s+)?(\d{1,2})\.(\d{1,2})\.(\d{4}|\d{2})?"), (m, s, today) =>
        {
            if (!TryDate(m, today, out var d))
            {
                return false;
            }
            s.Due = d;
            return true;
        }),
        new("date", Re(@"(?:bis\s+)?(?:übermorgen|uebermorgen)"), (_, s, today) => Set(() => s.Due = today.AddDays(2))),
        new("date", Re(@"(?:bis\s+)?heute"), (_, s, today) => Set(() => s.Due = today)),
        new("date", Re(@"(?:bis\s+)?morgen"), (_, s, today) => Set(() => s.Due = today.AddDays(1))),
        new("date", Re(@"(?:in\s+|innerhalb\s+von\s+|binnen\s+)?(\d{1,3})\s+(tag|tage|tagen|woche|wochen|monat|monate|monaten)"), (m, s, today) =>
        {
            s.Due = InCount(today, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value);
            return true;
        }),
        new("date", Re(@"nächste\s+woche|naechste\s+woche"), (_, s, today) => Set(() => s.Due = NextWeekday(today, DayOfWeek.Monday))),
        new("date", Re(@"ende\s+der\s+woche|ende\s+woche|wochenende"), (_, s, today) => Set(() => s.Due = NextWeekday(today, DayOfWeek.Friday, allowToday: true))),
        new("date", Re(@"ende\s+des\s+monats|monatsende|ende\s+monat"), (_, s, today) => Set(() => s.Due = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)))),
        new("date", Re(@"(?:bis\s+|am\s+|nächsten\s+|naechsten\s+|kommenden\s+)?(" + WeekdayNames + ")"), (m, s, today) => Set(() => s.Due = NextWeekday(today, Weekdays[m.Groups[1].Value]))),
        new("time", Re(@"(?:um\s+)?([01]?\d|2[0-3])[:.]([0-5]\d)(?:\s*uhr)?"), (m, s, _) => Set(() => s.Time = Time(m.Groups[1].Value, m.Groups[2].Value))),
        new("time", Re(@"(?:um\s+)?([01]?\d|2[0-3])\s*(?:uhr|h)"), (m, s, _) => Set(() => s.Time = Time(m.Groups[1].Value, "0"))),
        new("time", Re(@"um\s+([01]?\d|2[0-3])"), (m, s, _) => Set(() => s.Time = Time(m.Groups[1].Value, "0"))),
    ];

    /// <summary>Betreff einer Mail: „WG:“, „AW:“ usw. entfernen, dann wie die Schnelleingabe lesen.</summary>
    public static ParsedSubject Parse(string? subject, DateOnly today, TimeOnly? now = null)
    {
        var parsed = ParseText(Prefixes().Replace(subject ?? "", ""), today, now);
        if (parsed.Title.Length == 0)
        {
            parsed = parsed with { Title = string.IsNullOrWhiteSpace(subject) ? "E-Mail ohne Betreff" : subject.Trim() };
        }
        if (parsed.Title.Length > TaskService.MaxTitle)
        {
            parsed = parsed with { Title = parsed.Title[..(TaskService.MaxTitle - 1)] + "…" };
        }
        return parsed;
    }

    /// <summary>Freier Text wie in der Schnelleingabe. Jede Art (Termin, Person …) zählt nur einmal, das Erste gewinnt.</summary>
    public static ParsedSubject ParseText(string text, DateOnly today, TimeOnly? now = null)
    {
        var rest = " " + text + " ";
        var state = new State();
        var seen = new HashSet<string>();
        foreach (var rule in Rules)
        {
            if (seen.Contains(rule.Type) && rule.Type != "recurrence")
            {
                continue;
            }
            var m = rule.Pattern.Match(rest);
            if (!m.Success || (rule.Type == "recurrence" && state.Recurrence != "") || !rule.Apply(m, state, today))
            {
                continue;
            }
            seen.Add(rule.Type);
            rest = rest[..m.Index] + " " + rest[(m.Index + m.Length)..];
        }

        // Uhrzeit ohne Datum: heute, oder morgen, wenn die Uhrzeit schon vorbei ist.
        if (state.Time is { } time && state.Due is null)
        {
            state.Due = now is { } n && n >= time ? today.AddDays(1) : today;
        }
        if (state.Recurrence != "" && state.Due is null)
        {
            state.Due = today;
        }

        var title = Spaces().Replace(rest, " ").Trim().Trim(',', ';', ':', '-').Trim();
        return new ParsedSubject(title, state.Project, state.Person, state.High, state.FollowUp, state.Due, state.Low, state.Time, state.Recurrence);
    }

    /// <summary>
    /// Ergänzt den Betreff um Angaben aus dem eigenen Text. Was im Betreff steht, gilt immer zuerst;
    /// der Titel kommt nur aus dem Betreff.
    /// </summary>
    public static ParsedSubject Merge(ParsedSubject subject, ParsedSubject? text)
    {
        if (text is null)
        {
            return subject;
        }
        var hasPriority = subject.High || subject.Low;
        var due = subject.DueDate ?? text.DueDate;
        return subject with
        {
            ProjectQuery = subject.ProjectQuery ?? text.ProjectQuery,
            PersonQuery = subject.PersonQuery ?? text.PersonQuery,
            High = hasPriority ? subject.High : text.High,
            Low = hasPriority ? subject.Low : text.Low,
            FollowUp = subject.FollowUp || text.FollowUp,
            DueDate = due,
            // Eine Uhrzeit aus dem Text nur, wenn der Termin nicht allein aus dem Betreff stammt.
            DueTime = subject.DueTime ?? (subject.DueDate is null ? text.DueTime : null),
            Recurrence = subject.Recurrence != "" ? subject.Recurrence : text.Recurrence,
        };
    }

    [GeneratedRegex(
        @"^[ \t>]*(?:-{2,}\s*(?:Ursprüngliche Nachricht|Original Message|Weitergeleitete Nachricht|Forwarded message)\s*-{2,}.*|_{10,}|(?:Von|From)\s*:\s*\S.*|Am\s.+schrieb.*:|On\s.+wrote:|Anfang der weitergeleiteten Nachricht:|Begin forwarded message:)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ForwardedPart();

    [GeneratedRegex(
        @"^[ \t]*(?:--\s*|Mit freundlichen Grüßen|Freundliche Grüße|Viele Grüße|Beste Grüße|Liebe Grüße|Herzliche Grüße|Schöne Grüße|Gruß|Grüße|MfG|VG|LG|Best regards|Kind regards|Regards)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Signature();

    /// <summary>
    /// Der eigene Text einer Mail: alles vor der weitergeleiteten Nachricht („Von: …“, „-----Ursprüngliche Nachricht-----“
    /// usw.) und vor dem Gruß bzw. der Signatur. Datumsangaben aus der weitergeleiteten Mail zählen so nicht als Frist.
    /// </summary>
    public static string OwnText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }
        var text = body.Replace("\r\n", "\n");
        var forwarded = ForwardedPart().Match(text);
        if (forwarded.Success)
        {
            text = text[..forwarded.Index];
        }
        var signature = Signature().Match(text);
        if (signature.Success)
        {
            text = text[..signature.Index];
        }
        text = string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith('>'))).Trim();
        return text.Length > 1000 ? text[..1000] : text;
    }

    private static bool Set(Action apply)
    {
        apply();
        return true;
    }

    private static DateOnly NextWeekday(DateOnly today, DayOfWeek weekday, bool allowToday = false)
    {
        var diff = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
        if (diff == 0 && !allowToday)
        {
            diff = 7;
        }
        return today.AddDays(diff);
    }

    /// <summary>„in 5 Tagen“, „2 Wochen“, „1 Monat“. Fällt das auf ein Wochenende, gilt der Montag danach.</summary>
    internal static DateOnly InCount(DateOnly today, int n, string unit)
    {
        var u = unit.ToLowerInvariant();
        var d = u.StartsWith("tag", StringComparison.Ordinal) ? today.AddDays(n)
            : u.StartsWith("woche", StringComparison.Ordinal) ? today.AddDays(n * 7)
            : today.AddMonths(n);
        return TaskService.Workday(d);
    }

    private static TimeOnly Time(string hour, string minute) =>
        new(int.Parse(hour, CultureInfo.InvariantCulture), int.Parse(minute, CultureInfo.InvariantCulture));

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
