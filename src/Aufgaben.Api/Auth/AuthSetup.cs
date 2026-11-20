using System.Security.Claims;
using Aufgaben.Api.Data;
using Aufgaben.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace Aufgaben.Api.Auth;

/// <summary>
/// Anmeldung:
/// * Browser: OpenID Connect gegen Entra (Microsoft.Identity.Web) mit Autorisierungscode-Flow,
///   danach ein verschlüsseltes Sitzungs-Cookie.
/// * Teams: Die App holt per Teams-SSO ein Token für "access_as_user" und sendet es als Bearer.
/// * Entwicklung: Login ohne Entra, ausschließlich mit localhost-Adresse und ausdrücklicher Freigabe.
/// </summary>
public static class AuthSetup
{
    public const string SmartScheme = "smart";
    public const string CookieName = "aufgaben.sitzung";

    public static void AddAufgabenAuth(this WebApplicationBuilder builder, AufgabenOptions opts)
    {
        var isDev = opts.Auth.IsDev;
        if (isDev)
        {
            EnsureDevAllowed(opts);
        }

        var auth = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = SmartScheme;
            o.DefaultChallengeScheme = SmartScheme;
        });

        auth.AddPolicyScheme(SmartScheme, "Cookie oder Teams-Token", o =>
        {
            o.ForwardDefaultSelector = ctx =>
                !isDev && ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : CookieAuthenticationDefaults.AuthenticationScheme;
        });

        if (isDev)
        {
            auth.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o => ConfigureCookie(o, opts));
            return;
        }

        var azureAd = builder.Configuration.GetSection("AzureAd");
        var tenantId = azureAd["TenantId"];
        var clientId = azureAd["ClientId"];
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("AzureAd:TenantId und AzureAd:ClientId müssen gesetzt sein (oder Aufgaben:Auth:Mode=Dev für lokale Entwicklung).");
        }

        auth.AddMicrosoftIdentityWebApp(azureAd, OpenIdConnectDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme)
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => ConfigureCookie(o, opts));

        auth.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            o.MapInboundClaims = false;
            var audiences = new List<string> { clientId, $"api://{clientId}" };
            if (!string.IsNullOrWhiteSpace(opts.Auth.ApiAudience))
            {
                audiences.Add(opts.Auth.ApiAudience);
            }
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudiences = audiences,
                ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/",
                ],
                NameClaimType = "name",
                RoleClaimType = "roles",
            };
        });
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o, AufgabenOptions opts)
    {
        o.Cookie.Name = CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = opts.IsHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(opts.Auth.SessionHours, 1, 24 * 14));
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void EnsureDevAllowed(AufgabenOptions opts)
    {
        if (!opts.Auth.AllowDevLogin)
        {
            throw new InvalidOperationException("Aufgaben:Auth:Mode=Dev erfordert Aufgaben:Auth:AllowDevLogin=true.");
        }
        var host = new Uri(opts.NormalizedBaseUrl).Host;
        if (host is not ("localhost" or "127.0.0.1" or "[::1]"))
        {
            throw new InvalidOperationException("Der Entwicklungs-Login ist nur mit einer localhost-Adresse erlaubt.");
        }
    }

    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var opts = app.Services.GetRequiredService<IOptions<AufgabenOptions>>().Value;

        app.MapGet("/api/auth/config", async (AppDbContext db, CancellationToken ct) =>
        {
            if (!opts.Auth.IsDev)
            {
                return Results.Ok(new AuthConfigDto("entra", null));
            }
            var users = await db.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new DevUserDto(u.EntraOid, u.DisplayName, u.JobTitle, u.IsAdmin))
                .Take(50)
                .ToListAsync(ct);
            return Results.Ok(new AuthConfigDto("dev", users));
        }).AllowAnonymous();

        app.MapGet("/auth/login", (string? returnUrl) =>
        {
            if (opts.Auth.IsDev)
            {
                return Results.Redirect("/");
            }
            // Kontoauswahl anzeigen: wichtig an gemeinsam genutzten PCs mit mehreren Firmenkonten.
            var props = new OpenIdConnectChallengeProperties { RedirectUri = SafeReturnUrl(returnUrl), Prompt = "select_account" };
            return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        if (!opts.Auth.IsDev)
        {
            return;
        }

        app.MapGet("/auth/dev-login", async (string oid, string? returnUrl, HttpContext ctx, AppDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == oid && u.IsActive);
            if (user is null)
            {
                return Results.NotFound(new { error = "Unbekannte Testperson." });
            }
            var claims = new List<Claim>
            {
                new("oid", user.EntraOid),
                new("name", user.DisplayName),
            };
            if (!string.IsNullOrEmpty(user.Email))
            {
                claims.Add(new Claim("preferred_username", user.Email));
            }
            if (user.IsAdmin)
            {
                claims.Add(new Claim("roles", opts.Auth.AdminRole));
            }
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "name", "roles");
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect(SafeReturnUrl(returnUrl));
        }).AllowAnonymous();
    }
}

public sealed record AuthConfigDto(string Mode, List<DevUserDto>? DevUsers);

public sealed record DevUserDto(string Oid, string Name, string? JobTitle, bool IsAdmin);
