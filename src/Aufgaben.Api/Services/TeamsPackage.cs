using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aufgaben.Api.Services;

/// <summary>
/// Baut das Teams-App-Paket (ZIP mit manifest.json und den beiden Symbolen) aus der laufenden
/// Konfiguration. So passt das Paket immer zur Adresse, zur Entra-App und zur Version des Servers –
/// ohne lokales Skript und ohne dass Werte der Organisation in einem öffentlichen Build landen.
/// Vorlage und Symbole stammen aus teams/ und sind in die Anwendung eingebettet.
/// </summary>
public static partial class TeamsPackage
{
    /// <summary>Teams erlaubt höchstens 32 Zeichen für den Namen des Herausgebers.</summary>
    public const int MaxDeveloperLength = 32;

    [GeneratedRegex(@"^\d+\.\d+\.\d+")]
    private static partial Regex SemVer();

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}$")]
    private static partial Regex IpAddress();

    /// <summary>Version der Anwendung (aus dem Release-Build), passend für das Manifest: x.y.z.</summary>
    public static string AppVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(TeamsPackage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var m = SemVer().Match(info);
        return m.Success && m.Value != "0.0.0" ? m.Value : "1.0.0";
    }

    /// <summary>Prüft die Werte. Gibt eine verständliche Meldung zurück, wenn etwas fehlt, sonst null.</summary>
    public static string? Problem(string baseUrl, string clientId, string appId)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "Aufgaben__BaseUrl muss mit https:// beginnen – Teams lädt nur Seiten über HTTPS.";
        }
        if (IpAddress().IsMatch(uri.Host))
        {
            return "Aufgaben__BaseUrl braucht einen DNS-Namen, keine IP-Adresse – Teams akzeptiert keine IP-Adressen.";
        }
        if (!Guid.TryParse(clientId, out _))
        {
            return "AzureAd__ClientId fehlt in der .env oder ist keine GUID.";
        }
        if (!Guid.TryParse(appId, out _))
        {
            return "Aufgaben__Teams__AppId fehlt in der .env. Einmalig eine neue GUID eintragen (PowerShell: [guid]::NewGuid()) " +
                   "und danach nie mehr ändern – sonst hält Teams das Paket für eine andere App.";
        }
        return null;
    }

    /// <summary>Erzeugt das ZIP. Vorher <see cref="Problem"/> prüfen. Mit Bot-ID kommt „Als Aufgabe“ ins „…“-Menü von Nachrichten.</summary>
    public static byte[] Build(string baseUrl, string clientId, string appId, string developer, string version, string botId = "")
    {
        baseUrl = baseUrl.TrimEnd('/');
        var domain = new Uri(baseUrl).Host;
        developer = string.IsNullOrWhiteSpace(developer) ? "Aufgaben" : developer.Trim();
        if (developer.Length > MaxDeveloperLength)
        {
            developer = developer[..MaxDeveloperLength].TrimEnd();
        }

        var manifest = Encoding.UTF8.GetString(Resource("manifest.template.json"))
            .Replace("{{APP_ID}}", Json(appId))
            .Replace("{{CLIENT_ID}}", Json(clientId))
            .Replace("{{BASE_URL}}", Json(baseUrl))
            .Replace("{{DOMAIN}}", Json(domain))
            .Replace("{{COMPANY}}", Json(developer))
            .Replace("{{VERSION}}", Json(version));
        var root = JsonNode.Parse(manifest)!.AsObject(); // prüft zugleich, ob gültiges JSON entstanden ist
        if (Guid.TryParse(botId, out var bot))
        {
            root["composeExtensions"] = new JsonArray(new JsonObject
            {
                ["botId"] = bot.ToString(),
                ["commands"] = new JsonArray(new JsonObject
                {
                    ["id"] = TeamsBot.CommandId,
                    ["type"] = "action",
                    ["title"] = "Als Aufgabe",
                    ["description"] = "Aus dieser Nachricht eine Aufgabe machen – mit KI-Vorschlag",
                    ["context"] = new JsonArray("message"),
                    ["fetchTask"] = true,
                }),
            });
            manifest = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "manifest.json", Encoding.UTF8.GetBytes(manifest));
            Add(zip, "color.png", Resource("color.png"));
            Add(zip, "outline.png", Resource("outline.png"));
        }
        return buffer.ToArray();
    }

    /// <summary>Wert für die Stelle innerhalb eines JSON-Strings (ohne die Anführungszeichen).</summary>
    private static string Json(string value) => JsonSerializer.Serialize(value)[1..^1];

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(TeamsPackage).Assembly.GetManifestResourceStream("Teams." + name)
            ?? throw new InvalidOperationException($"Eingebettete Datei Teams.{name} fehlt im Build.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
