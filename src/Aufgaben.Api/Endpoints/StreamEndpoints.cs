using Aufgaben.Api.Auth;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Endpoints;

public static class StreamEndpoints
{
    /// <summary>Live-Updates per Server-Sent Events.</summary>
    public static void MapEventStream(this RouteGroupBuilder api)
    {
        api.MapGet("/events", async (HttpContext ctx, RealtimeHub hub, CurrentUser me) =>
        {
            var ct = ctx.RequestAborted;
            ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache, no-transform";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var (id, reader) = hub.Subscribe(me.Id);
            try
            {
                await ctx.Response.WriteAsync("retry: 5000\n: verbunden\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wait.CancelAfter(TimeSpan.FromSeconds(25));
                    try
                    {
                        var message = await reader.ReadAsync(wait.Token);
                        await ctx.Response.WriteAsync($"data: {message}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Verbindung geschlossen.
            }
            finally
            {
                hub.Unsubscribe(id);
            }
        });
    }

    /// <summary>Persönlicher Kalender-Feed (ohne Login, geschützt durch geheimen Link).</summary>
    public static void MapCalendarFeed(this WebApplication app)
    {
        app.MapGet("/kalender/{token}.ics", async (string token, AppDbContext db, AppClock clock, IOptions<AufgabenOptions> opts, CancellationToken ct) =>
        {
            if (token.Length is < 20 or > 100)
            {
                return Results.NotFound();
            }
            var hash = CalendarFeed.Hash(token);
            var user = await db.Users.FirstOrDefaultAsync(u => u.IcsTokenHash == hash && u.IsActive, ct);
            if (user is null)
            {
                return Results.NotFound();
            }
            var from = clock.Today.AddDays(-30);
            var tasks = await db.Tasks
                .Include(t => t.Project)
                .Include(t => t.CreatedBy)
                .Where(t => t.AssigneeId == user.Id && t.Status == Values.Open && t.DueDate != null && t.DueDate >= from)
                .OrderBy(t => t.DueDate)
                .Take(1000)
                .ToListAsync(ct);
            var ics = CalendarFeed.Build(tasks, clock, opts.Value.NormalizedBaseUrl);
            return Results.Text(ics, "text/calendar; charset=utf-8");
        }).AllowAnonymous().RequireRateLimiting("kalender");
    }
}
