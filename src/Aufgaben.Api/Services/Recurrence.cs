namespace Aufgaben.Api.Services;

/// <summary>Berechnet den nächsten Termin einer wiederkehrenden Aufgabe.</summary>
public static class Recurrence
{
    public static DateOnly Next(DateOnly from, string rule) => rule switch
    {
        "taeglich" => from.AddDays(1),
        "werktags" => NextWorkday(from),
        "woechentlich" => from.AddDays(7),
        "monatlich" => from.AddMonths(1),
        "jaehrlich" => from.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unbekannte Wiederholung"),
    };

    /// <summary>Nächster Termin, der nicht vor <paramref name="notBefore"/> liegt.</summary>
    public static DateOnly NextOnOrAfter(DateOnly from, string rule, DateOnly notBefore)
    {
        var next = Next(from, rule);
        var guard = 0;
        while (next < notBefore && guard++ < 5000)
        {
            next = Next(next, rule);
        }
        return next;
    }

    private static DateOnly NextWorkday(DateOnly from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }
        return next;
    }

    public static string Label(string rule) => rule switch
    {
        "taeglich" => "täglich",
        "werktags" => "werktags",
        "woechentlich" => "wöchentlich",
        "monatlich" => "monatlich",
        "jaehrlich" => "jährlich",
        _ => "",
    };
}
