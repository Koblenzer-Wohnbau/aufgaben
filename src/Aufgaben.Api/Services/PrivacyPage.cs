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
    public const string AiSetting = "Aufgaben__Privacy__Ai";

    internal const string ControllerPlaceholder = "[Name, Anschrift und Kontakt des Verantwortlichen]";
    internal const string DpoContactPlaceholder = "[Name und Kontakt der bzw. des Datenschutzbeauftragten]";
    internal const string RetentionPlaceholder =
        "[Durch den Verantwortlichen festzulegen, z. B. Löschung erledigter Aufgaben nach X Monaten; Protokolle nach 30 Tagen.]";
    internal const string DefaultHosting = "Die Daten werden auf einem eigenen Server im Unternehmen gespeichert.";
    internal const string AiPlaceholder =
        "[Durch den Verantwortlichen zu ergänzen: Vertrag zur Auftragsverarbeitung mit dem Anbieter und Speicherdauer beim Anbieter.]";

    /// <summary>Nur mit eingeschalteten KI-Vorschlägen: was an die Mistral-API geht und was nicht.</summary>
    private const string AiSection = """
        <h2>KI-Vorschläge</h2>
        <p>Wer „Mit KI erfassen“ nutzt oder „Als Aufgabe“ an einer Teams-Nachricht wählt, kann sich Aufgaben vorschlagen lassen. Dafür übermittelt der Server an die Mistral-API (Mistral AI, Frankreich):</p>
        <ul>
          <li>den eingefügten Text bzw. die gewählte Teams-Nachricht, bei einem eingefügten Bildschirmfoto das Bild zur Texterkennung,</li>
          <li>Name und interne Kennung der eingebenden Person sowie das heutige Datum,</li>
          <li>damit Personen und Projekte zugeordnet werden können: Name, Funktion und interne Kennung aller aktiven Nutzerinnen und Nutzer sowie die Bezeichnungen der Projekte.</li>
        </ul>
        <p>E-Mail-Adressen, gespeicherte Aufgaben und Notizen werden nicht übermittelt. Als Aufgabe gespeichert wird ein Vorschlag erst, wenn die Person ihn bestätigt; vorher speichert die App weder Text noch Vorschlag. Die Funktion ist freiwillig – Aufgaben lassen sich jederzeit ohne KI anlegen.</p>
        <p>{{KISTANDORT}}</p>
        <p>{{KIANBIETER}}</p>

        """;

    private const string TeamsMessageItem =
        "<li>Bei „Als Aufgabe“ an einer Teams-Nachricht: Text, Absender, Zeitpunkt und Link der gewählten Nachricht – " +
        "höchstens 30 Minuten im Arbeitsspeicher und nur für die Person, die die Nachricht gewählt hat, bis sie die Aufgabe anlegt.</li>";

    internal const string TemplateNotice =
        "<p class=\"hinweis\"><strong>Vorlage:</strong> Diese Seite muss vor dem Betrieb von der bzw. dem " +
        "Datenschutzbeauftragten geprüft und ergänzt werden (Verantwortlicher, Kontaktdaten, Speicherort, Auftragsverarbeiter).</p>";

    // Umlaute und andere Buchstaben bleiben lesbar; <, >, &, Anführungszeichen usw. werden kodiert.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static readonly Lazy<string> Template = new(LoadTemplate);

    [GeneratedRegex(@"\{\{([A-Z]+)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Pflichtangaben, die in der .env noch fehlen (Namen der Umgebungsvariablen). Leer = vollständig.
    /// Mit eingeschalteter KI gehört die Angabe zum KI-Anbieter dazu.
    /// </summary>
    public static IReadOnlyList<string> Missing(PrivacyOptions o, bool ai = false)
    {
        var missing = new List<string>();
        if (Lines(o.Controller).Count == 0) missing.Add(ControllerSetting);
        if (Lines(o.DpoContact).Count == 0) missing.Add(DpoContactSetting);
        if (Lines(o.Retention).Count == 0) missing.Add(RetentionSetting);
        if (ai && Lines(o.Ai).Count == 0) missing.Add(AiSetting);
        return missing;
    }

    /// <summary>
    /// Fertige Seite. Fehlende Angaben behalten den Platzhaltertext; der Vorlagen-Hinweis bleibt, bis alle Pflichtangaben
    /// gesetzt sind. Die Abschnitte zur KI und zu „Als Aufgabe“ erscheinen nur, wenn die Funktion eingeschaltet ist.
    /// </summary>
    public static string Render(PrivacyOptions o, bool ai = false, bool teamsMessages = false, string? aiRegion = null)
    {
        var values = new Dictionary<string, string>
        {
            ["VORLAGE"] = Missing(o, ai).Count == 0 ? "" : TemplateNotice,
            ["VERANTWORTLICHER"] = Text(o.Controller, ControllerPlaceholder),
            ["SPEICHERORT"] = Text(o.Hosting, DefaultHosting),
            ["SPEICHERDAUER"] = Text(o.Retention, RetentionPlaceholder),
            ["DATENSCHUTZBEAUFTRAGTE"] = Text(o.DpoContact, DpoContactPlaceholder),
            ["TEAMSNACHRICHT"] = teamsMessages ? TeamsMessageItem : "",
            // Der KI-Abschnitt ist feste Vorlage; nur die Angabe zum Anbieter kommt (kodiert) aus der .env.
            ["KI"] = ai
                ? AiSection.Replace("{{KISTANDORT}}", AiLocation(aiRegion)).Replace("{{KIANBIETER}}", Text(o.Ai, AiPlaceholder))
                : "",
        };
        // Ein Durchgang: Eingesetzte Werte werden nicht noch einmal nach Platzhaltern durchsucht.
        return Placeholder().Replace(Template.Value, m => values.TryGetValue(m.Groups[1].Value, out var v)
            ? v
            : throw new InvalidOperationException($"Unbekannter Platzhalter {m.Value} in der Datenschutz-Vorlage."));
    }

    /// <summary>Wo Mistral rechnet – passend zu Aufgaben__Mistral__Region (siehe MistralClient.BaseAddress).</summary>
    internal static string AiLocation(string? region) => (region ?? "").Trim().ToLowerInvariant() switch
    {
        "eu" => "Die Verarbeitung erfolgt über den EU-Endpunkt von Mistral in Rechenzentren in der EU bzw. im EWR.",
        "us" => "Die Verarbeitung erfolgt über den US-Endpunkt von Mistral in Rechenzentren in den USA.",
        _ => "Die App nutzt den globalen Endpunkt von Mistral; für ihn sagt Mistral keinen bestimmten Ort der Verarbeitung zu.",
    };

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
