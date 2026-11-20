using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Aufgaben.Api.Services;

/// <summary>
/// Schlanker Zugriff auf Microsoft Graph mit der App-Identität (Client-Credentials).
/// Die Anmeldung übernimmt Azure.Identity (intern MSAL), bevorzugt mit Zertifikat.
///
/// Konfiguration (gleicher Abschnitt wie Microsoft.Identity.Web):
///   AzureAd:TenantId, AzureAd:ClientId
///   AzureAd:ClientCredentials:0:SourceType = Path        → CertificateDiskPath (+ CertificatePassword), PFX-Datei
///   AzureAd:ClientCredentials:0:SourceType = ClientSecret → ClientSecret
/// </summary>
public sealed class GraphClient
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];
    private readonly HttpClient _http;
    private readonly TokenCredential? _credential;
    private readonly ILogger<GraphClient> _log;

    public GraphClient(HttpClient http, IConfiguration config, ILogger<GraphClient> log)
    {
        _http = http;
        _log = log;
        _credential = BuildCredential(config.GetSection("AzureAd"), log);
    }

    public bool IsConfigured => _credential is not null;

    internal static TokenCredential? BuildCredential(IConfigurationSection azureAd, ILogger log)
    {
        var tenant = azureAd["TenantId"];
        var client = azureAd["ClientId"];
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(client))
        {
            return null;
        }
        foreach (var cred in azureAd.GetSection("ClientCredentials").GetChildren())
        {
            var type = cred["SourceType"];
            if (string.Equals(type, "Path", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cred["CertificateDiskPath"]))
            {
                var path = cred["CertificateDiskPath"]!;
                if (!File.Exists(path))
                {
                    log.LogWarning("App-Zertifikat {Path} nicht gefunden – Graph-Funktionen sind deaktiviert.", path);
                    return null;
                }
                try
                {
                    var cert = X509CertificateLoader.LoadPkcs12FromFile(path, cred["CertificatePassword"]);
                    return new ClientCertificateCredential(tenant, client, cert);
                }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
                {
                    // Falsches Kennwort oder fehlende Leserechte dürfen nicht die ganze App lahmlegen.
                    log.LogError("App-Zertifikat {Path} lässt sich nicht laden ({Reason}) – Graph-Funktionen sind deaktiviert.", path, ex.GetType().Name);
                    return null;
                }
            }
            if (string.Equals(type, "ClientSecret", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cred["ClientSecret"]))
            {
                return new ClientSecretCredential(tenant, client, cred["ClientSecret"]);
            }
        }
        return null;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        if (_credential is null)
        {
            throw new InvalidOperationException("Microsoft Graph ist nicht konfiguriert.");
        }
        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        }
        return req;
    }

    /// <summary>Sendet eine Benachrichtigung in den Teams-Aktivitätsfeed einer Person.</summary>
    public async Task<GraphResult> SendActivityNotificationAsync(string userOid, object payload, CancellationToken ct)
    {
        using var res = await SendWithRetryAsync(HttpMethod.Post, $"users/{Uri.EscapeDataString(userOid)}/teamwork/sendActivityNotification", payload, ct);
        if (res.IsSuccessStatusCode)
        {
            return GraphResult.Ok;
        }
        var error = await DescribeErrorAsync(res, ct);
        _log.LogWarning("Teams-Benachrichtigung fehlgeschlagen: {Error}", error);
        return new GraphResult(false, res.StatusCode, error);
    }

    /// <summary>
    /// Liest alle Seiten einer Graph-Liste. Folgeseiten nur auf graph.microsoft.com/v1.0,
    /// jede Seite höchstens einmal, höchstens 200 Seiten – sonst Abbruch statt halber Daten.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> ListAsync(string url, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? next = url;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (next is not null)
        {
            if (!seen.Add(next) || seen.Count > 200)
            {
                throw new InvalidOperationException("Graph-Abruf abgebrochen: zu viele oder wiederholte Folgeseiten.");
            }
            if (Uri.TryCreate(next, UriKind.Absolute, out var absolute) && !IsGraphV1(absolute))
            {
                throw new InvalidOperationException("Graph-Abruf abgebrochen: Folgeseite zeigt nicht auf Microsoft Graph.");
            }
            using var res = await SendWithRetryAsync(HttpMethod.Get, next, null, ct);
            if (!res.IsSuccessStatusCode)
            {
                var error = await DescribeErrorAsync(res, ct);
                throw new HttpRequestException($"Microsoft Graph: {error}", null, res.StatusCode);
            }
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Unerwartete Antwort von Microsoft Graph.");
            }
            foreach (var item in values.EnumerateArray())
            {
                yield return item.Clone();
            }
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }
    }

    /// <summary>
    /// Drosselung (429) und kurze Ausfälle (502–504): Wartezeit aus Retry-After respektieren
    /// (höchstens 30 s), maximal drei Versuche.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = await RequestAsync(method, url, body, ct);
            var res = await _http.SendAsync(req, ct);
            var retryable = res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!retryable || attempt >= 3)
            {
                return res;
            }
            var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            res.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 30)), ct);
        }
    }

    private static bool IsGraphV1(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.Fragment);

    /// <summary>Nur Status, Fehlercode und Request-ID – nie den Antworttext (kann personenbezogene Daten enthalten).</summary>
    private static async Task<string> DescribeErrorAsync(HttpResponseMessage res, CancellationToken ct)
    {
        string? code = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c))
            {
                code = c.GetString();
            }
        }
        catch (JsonException)
        {
            // Kein JSON – nur der Status zählt.
        }
        var requestId = res.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
        return $"HTTP {(int)res.StatusCode}{(code is null ? "" : " " + code)}{(requestId is null ? "" : " (request-id " + requestId + ")")}";
    }
}

public sealed record GraphResult(bool Success, HttpStatusCode? Status = null, string? Error = null)
{
    public static readonly GraphResult Ok = new(true);

    /// <summary>Die App ist bei der Person nicht installiert oder die Person hat kein Teams → E-Mail nehmen.</summary>
    public bool ShouldFallBackToMail => Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
}
