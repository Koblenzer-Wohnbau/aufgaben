using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Aufgaben.Api.Options;

namespace Aufgaben.Api.Services;

/// <summary>
/// Datenschutzhinweise unter /datenschutz.html: eingebettete Vorlage, in die der Server die Angaben aus der
/// .env (Aufgaben__Privacy__…) einsetzt. So füllt jeder Betreiber die Seite aus, ohne Dateien im Image zu ändern.
/// Alle Werte werden HTML-kodiert – HTML oder Skripte aus der .env kommen nie als Code auf die Seite.
/// </summary>
public static partial class PrivacyPage
{
    public const string ControllerSetting = "Aufgaben__Privacy__Controller";
    public const string DpoContactSetting = "Aufgaben__Privacy__DpoContact";
    public const string RetentionSetting = "Aufgaben__Privacy__Retention";

    internal const string ControllerPlaceholder = "[Name, Anschrift und Kontakt des Verantwortlichen]";
    internal const string DpoContactPlaceholder = "[Name und Kontakt der bzw. des Datenschutzbeauftragten]";
    internal const string RetentionPlaceholder =
        "[Durch den Verantwortlichen festzulegen, z. B. Löschung erledigter Aufgaben nach X Monaten; Protokolle nach 30 Tagen.]";
    internal const string DefaultHosting = "Die Daten werden auf einem eigenen Server im Unternehmen gespeichert.";

    internal const string TemplateNotice =
        "<p class=\"hinweis\"><strong>Vorlage:</strong> Diese Seite muss vor dem Betrieb von der bzw. dem " +
        "Datenschutzbeauftragten geprüft und ergänzt werden (Verantwortlicher, Kontaktdaten, Speicherort, Auftragsverarbeiter).</p>";

    // Umlaute und andere Buchstaben bleiben lesbar; <, >, &, Anführungszeichen usw. werden kodiert.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static readonly Lazy<string> Template = new(LoadTemplate);

    [GeneratedRegex(@"\{\{([A-Z]+)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Pflichtangaben, die in der .env noch fehlen (Namen der Umgebungsvariablen). Leer = vollständig.</summary>
    public static IReadOnlyList<string> Missing(PrivacyOptions o)
    {
        var missing = new List<string>();
        if (Lines(o.Controller).Count == 0) missing.Add(ControllerSetting);
        if (Lines(o.DpoContact).Count == 0) missing.Add(DpoContactSetting);
        if (Lines(o.Retention).Count == 0) missing.Add(RetentionSetting);
        return missing;
    }

    /// <summary>Fertige Seite. Fehlende Angaben behalten den Platzhaltertext; der Vorlagen-Hinweis bleibt, bis alle Pflichtangaben gesetzt sind.</summary>
    public static string Render(PrivacyOptions o)
    {
        var values = new Dictionary<string, string>
        {
            ["VORLAGE"] = Missing(o).Count == 0 ? "" : TemplateNotice,
            ["VERANTWORTLICHER"] = Text(o.Controller, ControllerPlaceholder),
            ["SPEICHERORT"] = Text(o.Hosting, DefaultHosting),
            ["SPEICHERDAUER"] = Text(o.Retention, RetentionPlaceholder),
            ["DATENSCHUTZBEAUFTRAGTE"] = Text(o.DpoContact, DpoContactPlaceholder),
        };
        // Ein Durchgang: Eingesetzte Werte werden nicht noch einmal nach Platzhaltern durchsucht.
        return Placeholder().Replace(Template.Value, m => values.TryGetValue(m.Groups[1].Value, out var v)
            ? v
            : throw new InvalidOperationException($"Unbekannter Platzhalter {m.Value} in der Datenschutz-Vorlage."));
    }

    /// <summary>Wert aus der .env in Zeilen zerlegen: echte Zeilenumbrüche, "\n" als Zeichenfolge oder "|".</summary>
    internal static List<string> Lines(string? value) =>
        [.. (value ?? "").Replace("\\n", "\n").Split(['\r', '\n', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>HTML-kodierter Text mit &lt;br&gt; zwischen den Zeilen; ohne Wert der Ersatztext.</summary>
    internal static string Text(string? value, string fallback)
    {
        var lines = Lines(value);
        return lines.Count == 0 ? Encoder.Encode(fallback) : string.Join("<br>", lines.Select(Encoder.Encode));
    }

    private static string LoadTemplate()
    {
        using var stream = typeof(PrivacyPage).Assembly.GetManifestResourceStream("Pages.datenschutz.html")
            ?? throw new InvalidOperationException("Eingebettete Datei Pages.datenschutz.html fehlt im Build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
