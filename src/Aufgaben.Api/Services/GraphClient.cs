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
                var cert = X509CertificateLoader.LoadPkcs12FromFile(path, cred["CertificatePassword"]);
                return new ClientCertificateCredential(tenant, client, cert);
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
        using var req = await RequestAsync(HttpMethod.Post, $"users/{Uri.EscapeDataString(userOid)}/teamwork/sendActivityNotification", payload, ct);
        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
        {
            return GraphResult.Ok;
        }
        var text = await res.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Teams-Benachrichtigung fehlgeschlagen: {Status} {Body}", (int)res.StatusCode, Shorten(text));
        return new GraphResult(false, res.StatusCode, Shorten(text));
    }

    /// <summary>Liest alle Seiten einer Graph-Liste (Benutzer, Gruppenmitglieder …).</summary>
    public async IAsyncEnumerable<JsonElement> ListAsync(string url, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? next = url;
        var pages = 0;
        while (next is not null && pages++ < 200)
        {
            using var req = await RequestAsync(HttpMethod.Get, next, null, ct);
            req.Headers.Add("ConsistencyLevel", "eventual");
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Graph {(int)res.StatusCode}: {Shorten(text)}", null, res.StatusCode);
            }
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    yield return item.Clone();
                }
            }
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }
    }

    private static string Shorten(string text) => text.Length > 400 ? text[..400] : text;
}

public sealed record GraphResult(bool Success, HttpStatusCode? Status = null, string? Error = null)
{
    public static readonly GraphResult Ok = new(true);

    /// <summary>Die App ist bei der Person nicht installiert oder die Person hat kein Teams → E-Mail nehmen.</summary>
    public bool ShouldFallBackToMail => Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
}
