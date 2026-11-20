using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Infrastructure;

/// <summary>Fachlicher Fehler mit HTTP-Status und deutscher Meldung für die Oberfläche.</summary>
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static ApiException NotFound(string what = "Eintrag") => new(404, $"{what} nicht gefunden.");
    public static ApiException Forbidden(string message = "Dafür fehlt dir die Berechtigung.") => new(403, message);
    public static ApiException Invalid(string message) => new(400, message);
    public static ApiException Conflict(string message) => new(409, message);
}

/// <summary>Wandelt Fehler in einheitliche JSON-Antworten { "error": "..." } um.</summary>
public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, ex.Status, ex.Message);
        }
        catch (DbUpdateConcurrencyException) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, 409, "Die Aufgabe wurde inzwischen geändert. Bitte neu laden.");
        }
        catch (BadHttpRequestException ex) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, 400, "Ungültige Anfrage: " + ex.Message);
        }
        catch (JsonException) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, 400, "Ungültige Daten im Anfrageinhalt.");
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Browser hat die Verbindung geschlossen – kein Fehler.
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            log.LogError(ex, "Unerwarteter Fehler bei {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await Write(ctx, 500, "Unerwarteter Serverfehler. Bitte später erneut versuchen.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
    }
}

/// <summary>
/// Schutz gegen Cross-Site-Request-Forgery für Cookie-Sitzungen:
/// Ändernde API-Aufrufe brauchen den Kopf "X-Aufgaben-Request". Fremde Webseiten
/// können diesen Kopf ohne CORS-Freigabe nicht setzen. Bearer-Aufrufe aus Teams
/// sind ohnehin nicht betroffen.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Aufgaben-Request";

    public Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
        var protectedPath = ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/auth/logout");
        var bearer = ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

        if (unsafeMethod && protectedPath && !bearer && !ctx.Request.Headers.ContainsKey(HeaderName))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            return ctx.Response.WriteAsync("{\"error\":\"Anfrage ohne Schutzkennung abgelehnt.\"}");
        }
        return next(ctx);
    }
}

/// <summary>Sicherheitsrelevante Antwort-Header. Teams darf die App einbetten, andere Seiten nicht.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string FrameAncestors =
        "'self' https://teams.microsoft.com https://*.teams.microsoft.com https://*.cloud.microsoft " +
        "https://*.office.com https://outlook.office.com https://*.skype.com";

    // connect-src: TeamsJS lädt beim Start die Liste gültiger Teams-Domains von res.cdn.office.net.
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self' https://res.cdn.office.net; object-src 'none'; base-uri 'self'; " +
        "form-action 'self' https://login.microsoftonline.com; frame-ancestors " + FrameAncestors;

    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            // Eine strengere Regel eines Endpunkts (z. B. Sandbox für Anhänge) bleibt erhalten.
            if (!h.ContainsKey("Content-Security-Policy"))
            {
                h["Content-Security-Policy"] = Csp;
            }
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}
