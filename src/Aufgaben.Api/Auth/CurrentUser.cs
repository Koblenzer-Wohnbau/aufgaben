using System.Security.Claims;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Auth;

/// <summary>Die angemeldete Person für die aktuelle Anfrage.</summary>
public sealed class CurrentUser
{
    private User? _user;

    public bool IsSet => _user is not null;
    public bool ViaTeams { get; private set; }

    public User User => _user ?? throw new ApiException(401, "Bitte zuerst anmelden.");
    public int Id => User.Id;
    public bool IsAdmin => User.IsAdmin;

    internal void Set(User user, bool viaTeams)
    {
        _user = user;
        ViaTeams = viaTeams;
    }
}

public static class ClaimsExtensions
{
    private const string OidUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ScopeUri = "http://schemas.microsoft.com/identity/claims/scope";

    public static string? Oid(this ClaimsPrincipal p) =>
        p.FindFirst("oid")?.Value ?? p.FindFirst(OidUri)?.Value;

    public static string? TenantId(this ClaimsPrincipal p) =>
        p.FindFirst("tid")?.Value ?? p.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

    public static string DisplayName(this ClaimsPrincipal p) =>
        p.FindFirst("name")?.Value ?? p.FindFirst(ClaimTypes.Name)?.Value ?? p.Email() ?? "Unbekannt";

    public static string? Email(this ClaimsPrincipal p) =>
        p.FindFirst("preferred_username")?.Value
        ?? p.FindFirst("email")?.Value
        ?? p.FindFirst(ClaimTypes.Email)?.Value
        ?? p.FindFirst("upn")?.Value
        ?? p.FindFirst(ClaimTypes.Upn)?.Value;

    public static bool HasAppRole(this ClaimsPrincipal p, string role) =>
        p.FindAll("roles").Concat(p.FindAll(ClaimTypes.Role)).Any(c => c.Value == role);

    public static bool HasRolesClaim(this ClaimsPrincipal p) =>
        p.HasClaim(c => c.Type == "roles" || c.Type == ClaimTypes.Role);

    public static IEnumerable<string> Scopes(this ClaimsPrincipal p) =>
        (p.FindFirst("scp")?.Value ?? p.FindFirst(ScopeUri)?.Value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Legt Personen beim ersten Login an und hält Name, E-Mail und Rolle aktuell.</summary>
public sealed class UserProvisioning(AppDbContext db, AppClock clock)
{
    public async Task<User?> ResolveAsync(ClaimsPrincipal principal, bool viaTeams, string adminRole, CancellationToken ct)
    {
        var oid = principal.Oid();
        if (string.IsNullOrWhiteSpace(oid))
        {
            return null;
        }

        var now = clock.UtcNow;
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == oid, ct);
        var changed = false;

        if (user is null)
        {
            user = new User
            {
                EntraOid = oid,
                DisplayName = principal.DisplayName(),
                Email = principal.Email(),
                Upn = principal.Email(),
                CreatedAt = now,
                LastLoginAt = now,
            };
            db.Users.Add(user);
            changed = true;
        }
        else
        {
            var name = principal.DisplayName();
            if (name != "Unbekannt" && name != user.DisplayName)
            {
                user.DisplayName = name;
                changed = true;
            }
            if (user.LastLoginAt is null || now - user.LastLoginAt > TimeSpan.FromHours(1))
            {
                user.LastLoginAt = now;
                changed = true;
            }
        }

        // Die Rolle steht in jedem Token. Fehlt sie, ist die Person keine Verwaltung (mehr) –
        // auch wenn das Token gar keinen "roles"-Eintrag hat, weil ihr keine App-Rolle mehr zugewiesen ist.
        var admin = principal.HasAppRole(adminRole);
        if (admin != user.IsAdmin)
        {
            user.IsAdmin = admin;
            changed = true;
        }

        if (viaTeams && (user.TeamsSeenAt is null || now - user.TeamsSeenAt > TimeSpan.FromHours(12)))
        {
            user.TeamsSeenAt = now;
            changed = true;
        }

        if (changed)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (user.Id == 0)
            {
                // Parallele erste Anfrage hat die Person bereits angelegt.
                db.ChangeTracker.Clear();
                user = await db.Users.FirstAsync(u => u.EntraOid == oid, ct);
            }
        }
        return user;
    }
}

/// <summary>Ermittelt nach der Authentifizierung die Person aus der Datenbank.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public const string TeamsScope = "access_as_user";

    public async Task InvokeAsync(HttpContext ctx, CurrentUser current, UserProvisioning provisioning,
        Microsoft.Extensions.Options.IOptions<Options.AufgabenOptions> options, IConfiguration config)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            // Zusätzlich zur Prüfung des Ausstellers: nur Konten aus dem eigenen Mandanten.
            var tenant = config["AzureAd:TenantId"];
            if (!options.Value.Auth.IsDev && !string.IsNullOrWhiteSpace(tenant)
                && !string.Equals(ctx.User.TenantId(), tenant, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync("{\"error\":\"Dieses Konto gehört nicht zur eigenen Organisation.\"}");
                return;
            }

            var bearer = ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (bearer && !ctx.User.Scopes().Contains(TeamsScope))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("{\"error\":\"Token ohne Berechtigung access_as_user.\"}");
                return;
            }

            var user = await provisioning.ResolveAsync(ctx.User, bearer, options.Value.Auth.AdminRole, ctx.RequestAborted);
            if (user is null || !user.IsActive)
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    await ctx.Response.WriteAsync("{\"error\":\"Dieses Konto ist für Aufgaben nicht freigeschaltet.\"}");
                    return;
                }
            }
            else
            {
                current.Set(user, bearer);
            }
        }
        await next(ctx);
    }
}
