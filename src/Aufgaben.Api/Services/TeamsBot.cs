using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace Aufgaben.Api.Services;

/// <summary>
/// Nachrichtenerweiterung „Als Aufgabe“: Im „…“-Menü einer Teams-Nachricht ruft Teams über den Bot Framework
/// den Endpunkt <see cref="Path"/> auf. Die App merkt sich den Text kurz als Entwurf und öffnet in Teams ein
/// Fenster mit dem KI-Dialog. Angelegt wird, wie sonst auch, erst nach Bestätigung, mit der Anmeldung der Person.
/// Der Bot schickt selbst nichts an Teams und braucht deshalb kein eigenes Geheimnis.
/// </summary>
public static partial class TeamsBot
{
    public const string Scheme = "BotFramework";
    public const string Path = "/api/teams/bot";
    public const string CommandId = "alsAufgabe";
    public const string Channel = "msteams";

    /// <summary>Aussteller und Schlüssel der Tokens, mit denen Teams den Bot aufruft.</summary>
    public const string Issuer = "https://api.botframework.com";
    public const string MetadataAddress = "https://login.botframework.com/v1/.well-known/openidconfiguration";

    [GeneratedRegex(@"<br\s*/?>|</p\s*>|</div\s*>|</li\s*>|</h\d\s*>|</tr\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<emoji\b[^>]*\balt=""([^""]*)""[^>]*>(?:\s*</emoji>)?", RegexOptions.IgnoreCase)]
    private static partial Regex Emoji();

    [GeneratedRegex(@"<attachment\b[^>]*>.*?</attachment\s*>|<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex SpacesAroundBreaks();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBreaks();

    /// <summary>Nachrichtentext aus Teams (meist HTML) als lesbarer Text, Erwähnungen und Emojis bleiben als Text.</summary>
    public static string MessageText(string? content, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }
        var text = content.Replace("\r\n", "\n");
        if (!string.Equals(contentType, "text", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Replace("\n", " ");
            text = Dropped().Replace(text, "");
            text = Emoji().Replace(text, m => WebUtility.HtmlDecode(m.Groups[1].Value));
            text = LineBreaks().Replace(text, "\n");
            text = Tags().Replace(text, "");
            text = WebUtility.HtmlDecode(text);
        }
        text = SpacesAroundBreaks().Replace(text.Replace(' ', ' '), "\n");
        return ManyBreaks().Replace(text, "\n\n").Trim();
    }

    /// <summary>Nur echte Teams-Links übernehmen, sie landen als Quelle in den Notizen der Aufgabe.</summary>
    public static string? SafeLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("teams.cloud.microsoft", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri
            : null;

    /// <summary>serviceUrl im Token und in der Nachricht müssen übereinstimmen (Vorgabe des Bot Framework).</summary>
    public static bool SameServiceUrl(string? fromActivity, string? fromToken) =>
        !string.IsNullOrWhiteSpace(fromActivity) && !string.IsNullOrWhiteSpace(fromToken)
        && string.Equals(fromActivity.TrimEnd('/'), fromToken.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pflichtprüfungen für jeden Aufruf: Kanal Teams, serviceUrl wie im Token, eigener Mandant.
    /// Gibt den Grund der Ablehnung zurück oder null, wenn alles passt.
    /// </summary>
    public static string? Reject(JsonElement activity, string? tokenServiceUrl, string? tenantId)
    {
        if (activity.ValueKind != JsonValueKind.Object)
        {
            return "kein Activity-Objekt";
        }
        if (Text(activity, "channelId") != Channel)
        {
            return "falscher Kanal";
        }
        if (!SameServiceUrl(Text(activity, "serviceUrl"), tokenServiceUrl))
        {
            return "serviceUrl passt nicht zum Token";
        }
        var tenant = Text(At(activity, "channelData", "tenant"), "id") ?? Text(At(activity, "conversation"), "tenantId");
        if (string.IsNullOrWhiteSpace(tenantId) || !string.Equals(tenant, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return "fremder Mandant";
        }
        return null;
    }

    public sealed record IncomingMessage(string? CommandId, string? UserOid, string Text, string? From, DateTimeOffset? SentAt, string? Link);

    /// <summary>Was „Als Aufgabe“ an der Nachricht mitschickt: wer gewählt hat, Text, Absender, Zeit und Link.</summary>
    public static IncomingMessage ReadMessage(JsonElement activity, int maxChars)
    {
        var value = At(activity, "value");
        var payload = At(value, "messagePayload");
        var body = At(payload, "body");
        var text = MessageText(Text(body, "content"), Text(body, "contentType"));
        if (text.Length > maxChars)
        {
            text = text[..maxChars];
        }
        var sender = Text(At(payload, "from", "user"), "displayName") ?? Text(At(payload, "from", "application"), "displayName");
        DateTimeOffset? sentAt = DateTimeOffset.TryParse(Text(payload, "createdDateTime"), out var sent) ? sent : null;
        return new IncomingMessage(
            Text(value, "commandId"),
            Text(At(activity, "from"), "aadObjectId"),
            text,
            sender,
            sentAt,
            SafeLink(Text(payload, "linkToMessage")));
    }

    internal static JsonElement At(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return default;
            }
        }
        return element;
    }

    internal static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Der Signaturschlüssel muss für Teams freigegeben sein („endorsements“ im Schlüsselverzeichnis des
    /// Bot Framework). So taugt ein Token, das für einen anderen Kanal ausgestellt wurde, hier nicht.
    /// </summary>
    public static bool Endorses(JsonWebKey key, string channel)
    {
        if (!key.AdditionalData.TryGetValue("endorsements", out var raw))
        {
            return false;
        }
        IEnumerable<string?> values = raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } e => e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null),
            JsonElement { ValueKind: JsonValueKind.String } e => [e.GetString()],
            string s => [s],
            System.Collections.IEnumerable list => list.Cast<object?>().Select(x => x is JsonElement j && j.ValueKind == JsonValueKind.String ? j.GetString() : x?.ToString()),
            _ => [],
        };
        return values.Any(v => string.Equals(v, channel, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record TeamsDraft(string UserOid, string Text, string? From, DateTimeOffset? SentAt, string? Link, DateTimeOffset ExpiresAt);

/// <summary>
/// Entwürfe aus „Als Aufgabe“: nur im Speicher, eine halbe Stunde lang und nur für die Person, die die
/// Nachricht gewählt hat. Der Text steht so nicht in der Adresse des Fensters (und damit in keinem Protokoll).
/// </summary>
public sealed class TeamsDraftStore(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public const int MaxEntries = 2000;

    private readonly ConcurrentDictionary<string, TeamsDraft> drafts = new();

    /// <summary>Legt einen Entwurf an und gibt die Kennung zurück, oder null, wenn gerade zu viele offen sind.</summary>
    public string? Add(string userOid, string text, string? from, DateTimeOffset? sentAt, string? link)
    {
        var now = time.GetUtcNow();
        foreach (var (key, draft) in drafts)
        {
            if (draft.ExpiresAt <= now)
            {
                drafts.TryRemove(key, out _);
            }
        }
        if (drafts.Count >= MaxEntries)
        {
            return null;
        }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        drafts[id] = new TeamsDraft(userOid, text, from, sentAt, link, now + Lifetime);
        return id;
    }

    /// <summary>Nur für dieselbe Person und nur solange er gilt.</summary>
    public TeamsDraft? Get(string id, string userOid) =>
        drafts.TryGetValue(id, out var draft)
        && draft.ExpiresAt > time.GetUtcNow()
        && string.Equals(draft.UserOid, userOid, StringComparison.OrdinalIgnoreCase)
            ? draft
            : null;
}
