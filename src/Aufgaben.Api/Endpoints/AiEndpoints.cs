using System.Text.RegularExpressions;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Aufgaben.Api.Services.Ai;

namespace Aufgaben.Api.Endpoints;

/// <summary>
/// KI-Vorschläge: Text oder Bildschirmfoto rein, Aufgaben-Entwürfe raus. Angelegt wird hier nichts:
/// die Oberfläche zeigt die Entwürfe, die Person bestätigt, dann legt <c>POST /api/tasks</c> an.
/// </summary>
public static partial class AiEndpoints
{
    /// <summary>
    /// Bild nach dem Verkleinern im Browser. Als Data-URL wird es um ein Drittel größer und muss mit dem Text
    /// unter die 1 MB je Anfrage passen (App und Nginx).
    /// </summary>
    public const int MaxImageBytes = 600 * 1024;

    [GeneratedRegex(@"^data:(?<type>image/(?:png|jpeg|webp));base64,(?<data>[A-Za-z0-9+/=\s]+)$")]
    private static partial Regex DataUrl();

    public static void MapAiEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/ki/vorschlag", async (AiSuggestRequest req, TaskAssistant assistant, MistralClient mistral, CurrentUser me, CancellationToken ct) =>
        {
            if (!assistant.IsAvailable)
            {
                throw ApiException.Invalid("KI-Vorschläge sind auf diesem Server nicht eingeschaltet.");
            }
            var text = (req.Text ?? "").Trim();
            if (text.Length > TaskAssistant.MaxInputChars)
            {
                throw ApiException.Invalid($"Höchstens {TaskAssistant.MaxInputChars:N0} Zeichen, bitte den Text aufteilen.");
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(req.Image))
                {
                    var (bytes, type) = ParseImage(req.Image);
                    var seen = await mistral.OcrImageAsync(bytes, type, ct);
                    if (seen.Length == 0 && text.Length == 0)
                    {
                        throw ApiException.Invalid("Auf dem Bild war kein Text zu erkennen.");
                    }
                    text = text.Length == 0 ? seen : $"{text}\n\n--- Text aus dem Bildschirmfoto ---\n{seen}";
                }
                if (text.Length == 0)
                {
                    throw ApiException.Invalid("Bitte Text einfügen oder ein Bildschirmfoto mit Strg+V.");
                }
                // Im Dialog wartet jemand: höchstens ein zweiter Versuch bei Überlastung.
                return await assistant.SuggestAsync(text, me.User, null, attempts: 2, ct);
            }
            catch (AiException ex)
            {
                throw new ApiException(503, ex.Message);
            }
        }).RequireRateLimiting("ki");
    }

    /// <summary>Data-URL prüfen: nur PNG, JPEG oder WebP, erkannt an den ersten Bytes, und nicht zu groß.</summary>
    internal static (byte[] Bytes, string Type) ParseImage(string dataUrl)
    {
        var m = DataUrl().Match(dataUrl.Trim());
        if (!m.Success)
        {
            throw ApiException.Invalid("Das Bild muss ein PNG, JPEG oder WebP sein.");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(m.Groups["data"].Value);
        }
        catch (FormatException)
        {
            throw ApiException.Invalid("Das Bild ist beschädigt.");
        }
        if (bytes.Length > MaxImageBytes)
        {
            throw ApiException.Invalid("Das Bild ist zu groß, bitte einen kleineren Ausschnitt einfügen.");
        }
        var detected = AttachmentRules.ImageType(bytes);
        if (detected is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            throw ApiException.Invalid("Das Bild muss ein PNG, JPEG oder WebP sein.");
        }
        return (bytes, detected);
    }
}
