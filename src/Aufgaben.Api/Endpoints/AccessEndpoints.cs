using System.Globalization;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Endpoints;

/// <summary>
/// Einsichtsrechte ("Person B darf die Aufgaben von Person A sehen").
/// Verwaltung legt an → druckt Formular → bestätigt die Unterschrift → aktiv.
/// Jede Person sieht, wer ihre Aufgaben sehen darf, und das Zugriffsprotokoll dazu.
/// </summary>
public static class AccessEndpoints
{
    public static readonly string[] Permissions = ["lesen", "bearbeiten"];

    public static void MapAccessEndpoints(this RouteGroupBuilder api)
    {
        // ----------------------------------------------------------- für alle
        api.MapGet("/access/mine", async (CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var today = clock.Today;
            var grants = await db.AccessGrants.Include(g => g.Owner)
                .Where(g => g.ViewerId == me.Id && g.Status == AccessGrant.Active && g.ValidFrom <= today && (g.ValidUntil == null || g.ValidUntil >= today))
                .OrderBy(g => g.Owner.DisplayName)
                .ToListAsync(ct);
            return grants.Select(g => new MyAccessDto(g.Id, UserRef.From(g.Owner), g.Permission, Format(g.ValidUntil)));
        });

        api.MapGet("/access/about-me", async (CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var today = clock.Today;
            var grants = await Query(db)
                .Where(g => g.OwnerId == me.Id && g.Status != AccessGrant.Revoked && (g.ValidUntil == null || g.ValidUntil >= today))
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync(ct);
            var log = await db.AccessLog.Where(l => l.OwnerId == me.Id).OrderByDescending(l => l.At).Take(50).ToListAsync(ct);
            return new AccessAboutMeDto(grants.Select(g => ToDto(g, today, 0)).ToList(), await LogDtosAsync(db, log, ct));
        });

        // ----------------------------------------------------------- Verwaltung
        var admin = api.MapGroup("/admin/access").AddEndpointFilter(async (ctx, next) =>
        {
            if (!ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>().IsAdmin)
            {
                throw ApiException.Forbidden("Nur für die Verwaltung (App-Rolle Aufgaben.Admin).");
            }
            return await next(ctx);
        });

        admin.MapGet("/", async (AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var today = clock.Today;
            var grants = await Query(db).OrderBy(g => g.Status == AccessGrant.Draft ? 0 : g.Status == AccessGrant.Active ? 1 : 2)
                .ThenByDescending(g => g.CreatedAt).Take(500).ToListAsync(ct);
            var ids = grants.Select(g => g.Id).ToList();
            var counts = await db.AccessLog.Where(l => ids.Contains(l.GrantId))
                .GroupBy(l => l.GrantId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return grants.Select(g => ToDto(g, today, counts.GetValueOrDefault(g.Id)));
        });

        admin.MapPost("/", async (AccessGrantRequest req, CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            if (req.OwnerId == req.ViewerId)
            {
                throw ApiException.Invalid("Person und einsehende Person müssen verschieden sein.");
            }
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == req.OwnerId && u.IsActive, ct) ?? throw ApiException.Invalid("Person nicht gefunden.");
            var viewer = await db.Users.FirstOrDefaultAsync(u => u.Id == req.ViewerId && u.IsActive, ct) ?? throw ApiException.Invalid("Einsehende Person nicht gefunden.");
            var from = TaskService.ParseDate(req.ValidFrom) ?? clock.Today;
            var until = TaskService.ParseDate(req.ValidUntil);
            if (until is { } u2 && u2 < from)
            {
                throw ApiException.Invalid("Das Enddatum liegt vor dem Beginn.");
            }
            var purpose = (req.Purpose ?? "").Trim();
            if (purpose.Length is < 3 or > 300)
            {
                throw ApiException.Invalid("Bitte den Zweck angeben (3 bis 300 Zeichen), z. B. „Urlaubsvertretung“.");
            }
            var grant = new AccessGrant
            {
                OwnerId = owner.Id,
                Owner = owner,
                ViewerId = viewer.Id,
                Viewer = viewer,
                Permission = Permissions.Contains(req.Permission) ? req.Permission : throw ApiException.Invalid("Umfang: lesen oder bearbeiten."),
                ValidFrom = from,
                ValidUntil = until,
                Purpose = purpose,
                Status = AccessGrant.Draft,
                CreatedById = me.Id,
                CreatedAt = clock.UtcNow,
            };
            db.AccessGrants.Add(grant);
            await db.SaveChangesAsync(ct);
            db.AccessLog.Add(new AccessLogEntry { GrantId = grant.Id, ViewerId = me.Id, OwnerId = grant.OwnerId, Action = "angelegt", At = clock.UtcNow });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/access/{grant.Id}", ToDto(grant, clock.Today, 0));
        });

        admin.MapPost("/{id:int}/confirm", async (int id, AccessDecisionRequest req, CurrentUser me, AppDbContext db, AppClock clock,
            NotificationOutbox outbox, CancellationToken ct) =>
        {
            var grant = await Query(db).FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw ApiException.NotFound("Freigabe");
            if (grant.Status != AccessGrant.Draft)
            {
                throw ApiException.Conflict("Nur Entwürfe können bestätigt werden.");
            }
            var note = (req.Note ?? "").Trim();
            if (note.Length is < 3 or > 300)
            {
                throw ApiException.Invalid("Bitte festhalten, wo das unterschriebene Original liegt (z. B. „Personalakte“).");
            }
            if (grant.ValidUntil is { } end && end < clock.Today)
            {
                throw ApiException.Invalid("Der Zeitraum dieser Freigabe ist bereits abgelaufen. Bitte eine neue Freigabe anlegen.");
            }
            grant.Status = AccessGrant.Active;
            grant.ConfirmedAt = clock.UtcNow;
            grant.ConfirmedById = me.Id;
            grant.ConsentNote = note;
            var until = grant.ValidUntil is { } d ? $" bis {d:dd.MM.yyyy}" : "";
            var when = grant.ValidFrom > clock.Today ? $"ab {grant.ValidFrom:dd.MM.yyyy}" : "ab jetzt";
            var scope = grant.Permission == "bearbeiten" ? "sehen und bearbeiten" : "sehen";
            outbox.Enqueue(grant.OwnerId, NotificationOutbox.Access, null,
                $"{grant.Viewer.DisplayName} darf {when} deine Aufgaben {scope}{until}", grant.Purpose, me.User.DisplayName);
            outbox.Enqueue(grant.ViewerId, NotificationOutbox.Access, null,
                $"Du darfst {when} die Aufgaben von {grant.Owner.DisplayName} {scope}{until}", grant.Purpose, me.User.DisplayName);
            db.AccessLog.Add(new AccessLogEntry { GrantId = grant.Id, ViewerId = me.Id, OwnerId = grant.OwnerId, Action = "bestaetigt", At = clock.UtcNow });
            await db.SaveChangesAsync(ct);
            return ToDto(grant, clock.Today, 0);
        });

        admin.MapPost("/{id:int}/revoke", async (int id, AccessDecisionRequest req, CurrentUser me, AppDbContext db, AppClock clock,
            NotificationOutbox outbox, CancellationToken ct) =>
        {
            var grant = await Query(db).FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw ApiException.NotFound("Freigabe");
            if (grant.Status == AccessGrant.Revoked)
            {
                return ToDto(grant, clock.Today, 0);
            }
            var wasActive = grant.Status == AccessGrant.Active;
            grant.Status = AccessGrant.Revoked;
            grant.RevokedAt = clock.UtcNow;
            grant.RevokedById = me.Id;
            grant.RevokeReason = (req.Note ?? "").Trim() is { Length: > 0 } r ? (r.Length > 300 ? r[..300] : r) : "ohne Angabe";
            if (wasActive)
            {
                outbox.Enqueue(grant.OwnerId, NotificationOutbox.Access, null,
                    $"{grant.Viewer.DisplayName} kann deine Aufgaben nicht mehr sehen", grant.RevokeReason, me.User.DisplayName);
                outbox.Enqueue(grant.ViewerId, NotificationOutbox.Access, null,
                    $"Deine Einsicht in die Aufgaben von {grant.Owner.DisplayName} wurde beendet", grant.RevokeReason, me.User.DisplayName);
            }
            db.AccessLog.Add(new AccessLogEntry { GrantId = grant.Id, ViewerId = me.Id, OwnerId = grant.OwnerId, Action = "widerrufen", At = clock.UtcNow });
            await db.SaveChangesAsync(ct);
            return ToDto(grant, clock.Today, 0);
        });

        admin.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var grant = await db.AccessGrants.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw ApiException.NotFound("Freigabe");
            if (grant.Status != AccessGrant.Draft)
            {
                throw ApiException.Conflict("Bestätigte Freigaben bleiben zur Nachvollziehbarkeit erhalten, bitte widerrufen.");
            }
            db.AccessGrants.Remove(grant);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapGet("/{id:int}/log", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var log = await db.AccessLog.Where(l => l.GrantId == id).OrderByDescending(l => l.At).Take(500).ToListAsync(ct);
            return await LogDtosAsync(db, log, ct);
        });
    }

    private static IQueryable<AccessGrant> Query(AppDbContext db) =>
        db.AccessGrants.Include(g => g.Owner).Include(g => g.Viewer);

    private static string? Format(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static AccessGrantDto ToDto(AccessGrant g, DateOnly today, int accessCount) => new(
        g.Id,
        UserRef.From(g.Owner),
        UserRef.From(g.Viewer),
        g.Permission,
        Format(g.ValidFrom)!,
        Format(g.ValidUntil),
        g.Purpose,
        g.Status,
        g.Status == AccessGrant.Active && g.ValidFrom <= today && (g.ValidUntil is null || g.ValidUntil >= today),
        g.CreatedAt,
        g.ConfirmedAt,
        g.ConsentNote,
        g.RevokedAt,
        g.RevokeReason,
        accessCount);

    private static async Task<List<AccessLogDto>> LogDtosAsync(AppDbContext db, List<AccessLogEntry> log, CancellationToken ct)
    {
        var ids = log.SelectMany(l => new[] { l.ViewerId, l.OwnerId }).Distinct().ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return log.Where(l => users.ContainsKey(l.ViewerId) && users.ContainsKey(l.OwnerId))
            .Select(l => new AccessLogDto(l.Id, l.Action, UserRef.From(users[l.ViewerId]), UserRef.From(users[l.OwnerId]), l.TaskId, l.At))
            .ToList();
    }
}
