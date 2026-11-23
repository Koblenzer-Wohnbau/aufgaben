using System.Text.Json;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Aufgaben.Api.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Endpoints;

public sealed record TeamsDraftDto(string Text, string? From, DateTimeOffset? SentAt, string? Link);

/// <summary>„Als Aufgabe“ aus dem „…“-Menü einer Teams-Nachricht (siehe <see cref="TeamsBot"/>).</summary>
public static class TeamsBotEndpoints
{
    /// <summary>Endpunkt für Teams. Nur aktiv, wenn Aufgaben__Teams__BotId gesetzt ist.</summary>
    public static void MapTeamsBot(this WebApplication app, AufgabenOptions opts)
    {
        if (!opts.Teams.BotEnabled || opts.Auth.IsDev)
        {
            return;
        }
        var onlyBotFramework = new AuthorizationPolicyBuilder(TeamsBot.Scheme).RequireAuthenticatedUser().Build();
        app.MapPost(TeamsBot.Path, HandleAsync).RequireAuthorization(onlyBotFramework);
    }

    /// <summary>Entwurf für das Fenster in Teams, nur für die Person, die die Nachricht gewählt hat.</summary>
    public static void MapTeamsDrafts(this RouteGroupBuilder api)
    {
        api.MapGet("/teams/entwurf/{id}", (string id, TeamsDraftStore drafts, CurrentUser me) =>
        {
            var draft = drafts.Get(id, me.User.EntraOid)
                ?? throw new ApiException(404, "Der Entwurf aus Teams ist abgelaufen. Bitte an der Nachricht „Als Aufgabe“ erneut wählen.");
            return new TeamsDraftDto(draft.Text, draft.From, draft.SentAt, draft.Link);
        });
    }

    private static async Task<IResult> HandleAsync(
        JsonElement activity,
        HttpContext ctx,
        TeamsDraftStore drafts,
        AppDbContext db,
        IOptions<AufgabenOptions> options,
        IConfiguration config,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var log = logs.CreateLogger("Aufgaben.Api.TeamsBot");
        var reason = TeamsBot.Reject(activity, ctx.User.FindFirst("serviceurl")?.Value, config["AzureAd:TenantId"]);
        if (reason is not null)
        {
            log.LogWarning("Aufruf an den Teams-Bot abgelehnt: {Reason}", reason);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (TeamsBot.Text(activity, "type") != "invoke" || TeamsBot.Text(activity, "name") != "composeExtension/fetchTask")
        {
            // Installation, Unterhaltungen, submitAction beim Schließen des Fensters: nichts zu tun.
            return Results.Ok();
        }

        var message = TeamsBot.ReadMessage(activity, TaskAssistant.MaxInputChars);
        if (message.CommandId != TeamsBot.CommandId)
        {
            return Message("Diese Aktion kennt die Aufgaben-App nicht. Bitte das Teams-Paket aktualisieren.");
        }
        var user = string.IsNullOrEmpty(message.UserOid)
            ? null
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.EntraOid == message.UserOid, ct);
        if (user is null || !user.IsActive)
        {
            return Message("Dein Konto ist für Aufgaben nicht freigeschaltet. Bitte die App einmal öffnen oder die Verwaltung fragen.");
        }

        var id = drafts.Add(user.EntraOid, message.Text, message.From, message.SentAt, message.Link);
        if (id is null)
        {
            return Message("Gerade sind sehr viele Entwürfe offen. Bitte in ein paar Minuten noch einmal versuchen.");
        }
        log.LogInformation("Entwurf aus einer Teams-Nachricht für Person {UserId}", user.Id);

        var url = $"{options.Value.NormalizedBaseUrl}/?inTeams=1#/teams/ki/{id}";
        return Results.Json(new
        {
            task = new
            {
                type = "continue",
                value = new { title = "Als Aufgabe", height = "large", width = "large", url, fallbackUrl = url },
            },
        });
    }

    /// <summary>Kurze Meldung im Teams-Fenster statt des Formulars.</summary>
    private static IResult Message(string text) => Results.Json(new { task = new { type = "message", value = text } });
}
