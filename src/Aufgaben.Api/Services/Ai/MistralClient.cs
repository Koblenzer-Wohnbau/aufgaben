using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aufgaben.Api.Options;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Services.Ai;

/// <summary>Fehler beim KI-Dienst, die Meldung enthält nie Inhalte aus der Antwort (die können Mailtext enthalten).</summary>
public sealed class AiException(string message) : Exception(message);

/// <summary>
/// Schlanker Zugang zur Mistral-API (Chat mit JSON-Antwort und Texterkennung), ohne SDK.
/// Vorbild ist die Anbindung im ProjektCockpit: bei 429 warten und wiederholen, Fehler ohne Antworttext melden,
/// Nachdenk-Teile der Antwort („thinking“) nicht als Antwort werten.
/// </summary>
public sealed class MistralClient(HttpClient http, IOptions<AufgabenOptions> options, ILogger<MistralClient> log)
{
    private MistralOptions O => options.Value.Mistral;

    /// <summary>Warten vor einem neuen Versuch, in Tests austauschbar.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Wait { get; set; } = Task.Delay;

    /// <summary>Eingeschaltet und ein Schlüssel vorhanden.</summary>
    public bool IsConfigured => O.Enabled && ApiKey().Length > 0;

    public string Model => O.Model;

    private string ApiKey()
    {
        if (!string.IsNullOrWhiteSpace(O.ApiKey))
        {
            return O.ApiKey.Trim();
        }
        if (string.IsNullOrWhiteSpace(O.ApiKeyFile))
        {
            return "";
        }
        try
        {
            return File.Exists(O.ApiKeyFile) ? File.ReadAllText(O.ApiKeyFile).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>Basisadresse je Region (Aufgaben__Mistral__Region): global, EU oder USA.</summary>
    public static Uri BaseAddress(string? region) => (region ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "global" => new Uri("https://api.mistral.ai/"),
        "eu" => new Uri("https://api.eu.mistral.ai/"),
        "us" => new Uri("https://api.us.mistral.ai/"),
        var other => throw new InvalidOperationException($"Aufgaben__Mistral__Region „{other}“: erlaubt sind leer (global), eu oder us."),
    };

    public static bool IsGlm(string model) => model.Trim().ToLowerInvariant() is "zai-glm-5-3" or "zai-glm-latest";

    /// <summary>Wie im ProjektCockpit: auto = max bei GLM, sonst high; max ist nur bei GLM erlaubt.</summary>
    public static string ReasoningEffort(string model, string configured)
    {
        var effort = (configured ?? "auto").Trim().ToLowerInvariant();
        return effort switch
        {
            "auto" => IsGlm(model) ? "max" : "high",
            "none" or "high" => effort,
            "max" when IsGlm(model) => "max",
            _ => throw new InvalidOperationException(
                $"Aufgaben__Mistral__ReasoningEffort für {model}: erlaubt sind auto, none, high{(IsGlm(model) ? ", max" : "")}."),
        };
    }

    /// <summary>Chat mit Antwort im JSON-Modus. Gibt den Antworttext zurück (ohne Nachdenk-Teile).</summary>
    public async Task<string> ChatJsonAsync(string system, string user, int attempts, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = O.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            ["response_format"] = new { type = "json_object" },
            ["reasoning_effort"] = ReasoningEffort(O.Model, O.ReasoningEffort),
        };
        using var doc = await SendAsync("v1/chat/completions", payload, O.Model, attempts, ct);
        var choices = doc.RootElement.TryGetProperty("choices", out var c) && c.ValueKind == JsonValueKind.Array ? c : default;
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content))
        {
            throw new AiException("Die KI hat keine verwertbare Antwort geliefert.");
        }
        return ResponseText(content);
    }

    /// <summary>Texterkennung für ein Bild (PNG, JPEG, WebP): Ergebnis als Markdown je Seite.</summary>
    public async Task<string> OcrImageAsync(byte[] image, string mediaType, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = O.OcrModel,
            ["document"] = new Dictionary<string, string>
            {
                ["type"] = "image_url",
                ["image_url"] = $"data:{mediaType};base64,{Convert.ToBase64String(image)}",
            },
        };
        using var doc = await SendAsync("v1/ocr", payload, O.OcrModel, 2, ct);
        if (!doc.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
        {
            return "";
        }
        var sb = new StringBuilder();
        foreach (var page in pages.EnumerateArray())
        {
            if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine(md.GetString());
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Antworttext aus „content“: Text direkt oder die Teile vom Typ „text“ (ohne „thinking“).</summary>
    internal static string ResponseText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }
        if (content.ValueKind != JsonValueKind.Array)
        {
            return "";
        }
        var sb = new StringBuilder();
        foreach (var chunk in content.EnumerateArray())
        {
            if (chunk.ValueKind == JsonValueKind.Object
                && chunk.TryGetProperty("type", out var type) && type.GetString() == "text"
                && chunk.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }
        return sb.ToString();
    }

    private async Task<JsonDocument> SendAsync(string path, object payload, string model, int attempts, CancellationToken ct)
    {
        var key = ApiKey();
        if (!O.Enabled || key.Length == 0)
        {
            throw new AiException("KI-Vorschläge sind nicht eingeschaltet.");
        }
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AiException("Die KI hat zu lange gebraucht. Bitte noch einmal versuchen oder den Text kürzen.");
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning("Mistral nicht erreichbar: {Error}", ex.Message);
                throw new AiException("Der KI-Dienst ist gerade nicht erreichbar.");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt + 1 < attempts)
                {
                    // Wie im ProjektCockpit: 5, 10, 20 … Sekunden oder so lange, wie der Dienst verlangt
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0;
                    var delay = Math.Min(60, Math.Max(5 * Math.Pow(2, attempt), retryAfter));
                    await Wait(TimeSpan.FromSeconds(delay), ct);
                    continue;
                }
                // Modell in dieser Region nicht im Angebot (z. B. am EU-Endpunkt): eigene, verständliche Meldung.
                if (status is 400 or 404 && await IsUnknownModelAsync(response, ct))
                {
                    var host = http.BaseAddress?.Host ?? "Mistral";
                    log.LogWarning("Mistral-Modell {Model} wird an {Host} nicht angeboten", model, host);
                    throw new AiException($"Das KI-Modell „{model}“ wird an {host} nicht angeboten, bitte Aufgaben__Mistral__Model bzw. Aufgaben__Mistral__Region prüfen.");
                }
                // Der Antworttext kann Teile des gesendeten Inhalts enthalten, nicht protokollieren.
                log.LogWarning("Mistral-Aufruf {Path} fehlgeschlagen: HTTP {Status}", path, status);
                throw new AiException(status switch
                {
                    401 or 403 => "Der KI-Schlüssel wird nicht angenommen, bitte die Einrichtung prüfen.",
                    429 => "Die KI ist gerade ausgelastet. Bitte gleich noch einmal versuchen.",
                    _ => $"Der KI-Dienst hat einen Fehler gemeldet (HTTP {status}).",
                });
            }
        }
    }

    /// <summary>
    /// Fehlerart „unbekanntes Modell“ erkennen. Gelesen wird nur das Feld „type“ bzw. der Anfang der Meldung:
    /// der übrige Antworttext kann Inhalte der Anfrage enthalten und wird nicht weitergegeben.
    /// </summary>
    internal static async Task<bool> IsUnknownModelAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            return type.Equals("invalid_model", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Invalid model", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
