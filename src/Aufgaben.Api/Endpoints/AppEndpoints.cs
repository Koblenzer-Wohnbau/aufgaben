using Aufgaben.Api.Auth;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Aufgaben.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Endpoints;

public static class AppEndpoints
{
    /// <summary>Fehlertext für die Oberfläche kürzen (Ursache steht vollständig im Protokoll).</summary>
    private static string Short(string message) => message.Length > 300 ? message[..300] + " …" : message;

    public static void MapAppEndpoints(this RouteGroupBuilder api)
    {
        MapMe(api);
        MapUsers(api);
        MapLists(api);
        MapProjects(api);
        MapTemplates(api);
        MapAdmin(api);
    }

    // ------------------------------------------------------------------ Ich

    private static void MapMe(RouteGroupBuilder api)
    {
        api.MapGet("/me", (CurrentUser me, AppClock clock, TeamsNotifier teams, MailNotifier mail, MistralClient ai, IOptions<AufgabenOptions> opts) =>
            ToMe(me.User, clock, teams, mail, ai, opts.Value));

        api.MapPatch("/me", async (MeRequest req, CurrentUser me, AppDbContext db, AppClock clock, TeamsNotifier teams, MailNotifier mail, MistralClient ai, IOptions<AufgabenOptions> opts, CancellationToken ct) =>
        {
            var user = me.User;
            if (req.NotifyChannel is not null)
            {
                user.NotifyChannel = Values.NotifyChannels.Contains(req.NotifyChannel)
                    ? req.NotifyChannel
                    : throw ApiException.Invalid("Benachrichtigung: auto, teams, email oder aus.");
            }
            if (req.DigestEnabled is bool digest)
            {
                user.DigestEnabled = digest;
            }
            await db.SaveChangesAsync(ct);
            return ToMe(user, clock, teams, mail, ai, opts.Value);
        });

        api.MapPost("/me/calendar-feed", async (CurrentUser me, AppDbContext db, IOptions<AufgabenOptions> opts, CancellationToken ct) =>
        {
            var (token, hash) = CalendarFeed.NewToken();
            me.User.IcsTokenHash = hash;
            await db.SaveChangesAsync(ct);
            return new CalendarFeedDto($"{opts.Value.NormalizedBaseUrl}/kalender/{token}.ics");
        });

        api.MapDelete("/me/calendar-feed", async (CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            me.User.IcsTokenHash = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static MeDto ToMe(User u, AppClock clock, TeamsNotifier teams, MailNotifier mail, MistralClient ai, AufgabenOptions o) => new(
        u.Id, u.DisplayName, u.Email, u.IsAdmin, u.NotifyChannel, u.DigestEnabled,
        u.TeamsSeenAt is not null, u.IcsTokenHash is not null, mail.IsAvailable, teams.IsAvailable,
        clock.Today.ToString("yyyy-MM-dd"),
        o.MailInbox.Enabled && !string.IsNullOrWhiteSpace(o.MailInbox.Address) ? o.MailInbox.Address : null,
        o.Worker.DigestOnWeekends,
        ai.IsConfigured,
        o.HasInboxView(u.Email, u.Upn),
        o.AbsencesEnabled);

    // ------------------------------------------------------------------ Personen

    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet("/users", async (string? q, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Users.Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = "%" + TaskService.EscapeLike(q.Trim()) + "%";
                query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern, "\\") || (u.Email != null && EF.Functions.ILike(u.Email, pattern, "\\")));
            }
            var users = await query.OrderBy(u => u.DisplayName).Take(q is null ? 5000 : 25).ToListAsync(ct);
            return users.Select(UserRef.From);
        });
    }

    // ------------------------------------------------------------------ Listen

    private static void MapLists(RouteGroupBuilder api)
    {
        var lists = api.MapGroup("/lists");

        // Eigene und mit mir geteilte Listen
        lists.MapGet("/", async (CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var u = me.Id;
            var items = await db.TaskLists
                .Where(l => l.OwnerId == u || l.Members.Any(m => m.UserId == u))
                .Include(l => l.Members).ThenInclude(m => m.User)
                .AsSplitQuery()
                .OrderBy(l => l.OwnerId == u ? 0 : 1).ThenBy(l => l.Position).ThenBy(l => l.Name)
                .ToListAsync(ct);
            var ids = items.Select(l => l.Id).ToList();
            var ownerIds = items.Select(l => l.OwnerId).Distinct().ToList();
            var owners = await db.Users.Where(x => ownerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var counts = await db.Tasks
                .Where(t => t.ListId != null && t.Status == Values.Open && ids.Contains(t.ListId!.Value))
                .GroupBy(t => t.ListId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return items.Select(l => ToListDto(l, owners[l.OwnerId], u, counts.GetValueOrDefault(l.Id)));
        });

        lists.MapPost("/", async (ListRequest req, CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var own = await db.TaskLists.CountAsync(l => l.OwnerId == me.Id, ct);
            if (own >= 100)
            {
                throw ApiException.Invalid("Höchstens 100 eigene Listen.");
            }
            var list = new TaskList
            {
                OwnerId = me.Id,
                Name = ListName(req.Name),
                Color = ListColor(req.Color ?? "blau"),
                Position = req.Position ?? own,
                CreatedAt = clock.UtcNow,
            };
            db.TaskLists.Add(list);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/lists/{list.Id}", ToListDto(list, me.User, me.Id, 0));
        });

        lists.MapPatch("/{id:int}", async (int id, ListRequest req, CurrentUser me, AppDbContext db, RealtimeHub hub, CancellationToken ct) =>
        {
            var list = await OwnListAsync(db, id, me.Id, ct);
            if (req.Name is not null) list.Name = ListName(req.Name);
            if (req.Color is not null) list.Color = ListColor(req.Color);
            if (req.Position is int p) list.Position = p;
            await db.SaveChangesAsync(ct);
            hub.Publish(list.Members.Select(m => m.UserId).Append(me.Id), new { type = "lists" });
            return ToListDto(list, me.User, me.Id, 0);
        });

        lists.MapDelete("/{id:int}", async (int id, CurrentUser me, AppDbContext db, RealtimeHub hub, CancellationToken ct) =>
        {
            var list = await OwnListAsync(db, id, me.Id, ct);
            var audience = list.Members.Select(m => m.UserId).Append(me.Id).ToList();
            db.TaskLists.Remove(list);
            await db.SaveChangesAsync(ct);
            hub.Publish(audience, new { type = "lists" });
            return Results.NoContent();
        });

        // Teilen: nur der Besitzer fügt Mitglieder hinzu
        lists.MapPost("/{id:int}/members", async (int id, ListMemberRequest req, CurrentUser me, AppDbContext db, AppClock clock,
            NotificationOutbox outbox, RealtimeHub hub, CancellationToken ct) =>
        {
            var list = await OwnListAsync(db, id, me.Id, ct);
            if (req.UserId == me.Id)
            {
                throw ApiException.Invalid("Die Liste gehört dir bereits.");
            }
            var user = await db.Users.FirstOrDefaultAsync(x => x.Id == req.UserId && x.IsActive, ct)
                       ?? throw ApiException.Invalid("Diese Person gibt es nicht oder sie ist nicht aktiv.");
            if (list.Members.Any(m => m.UserId == user.Id))
            {
                return Results.NoContent();
            }
            if (list.Members.Count >= 50)
            {
                throw ApiException.Invalid("Höchstens 50 Personen pro Liste.");
            }
            list.Members.Add(new TaskListMember { ListId = list.Id, UserId = user.Id, AddedAt = clock.UtcNow });
            outbox.Enqueue(user.Id, NotificationOutbox.ListShared, null, list.Name, "", me.User.DisplayName);
            await db.SaveChangesAsync(ct);
            hub.Publish(list.Members.Select(m => m.UserId).Append(me.Id), new { type = "lists" });
            return Results.NoContent();
        });

        // Entfernen (Besitzer) oder selbst verlassen (Mitglied)
        lists.MapDelete("/{id:int}/members/{userId:int}", async (int id, int userId, CurrentUser me, AppDbContext db, RealtimeHub hub, CancellationToken ct) =>
        {
            var list = await db.TaskLists.Include(l => l.Members).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw ApiException.NotFound("Liste");
            var isOwner = list.OwnerId == me.Id;
            var isMember = list.Members.Any(m => m.UserId == me.Id);
            if (!isOwner && !isMember)
            {
                throw ApiException.NotFound("Liste");
            }
            if (!isOwner && userId != me.Id)
            {
                throw ApiException.Forbidden("Mitglieder entfernen kann nur die Person, der die Liste gehört.");
            }
            var member = list.Members.FirstOrDefault(m => m.UserId == userId) ?? throw ApiException.NotFound("Mitglied");
            var audience = list.Members.Select(m => m.UserId).Append(list.OwnerId).ToList();
            list.Members.Remove(member);
            await db.SaveChangesAsync(ct);
            hub.Publish(audience, new { type = "lists" });
            return Results.NoContent();
        });
    }

    private static async Task<TaskList> OwnListAsync(AppDbContext db, int id, int me, CancellationToken ct) =>
        await db.TaskLists.Include(l => l.Members).ThenInclude(m => m.User).FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == me, ct)
        ?? throw ApiException.NotFound("Liste");

    private static ListDto ToListDto(TaskList l, User owner, int me, int openCount) => new(
        l.Id, l.Name, l.Color, l.Position, openCount, l.OwnerId == me, UserRef.From(owner),
        l.Members.Where(m => m.User is not null).OrderBy(m => m.User.DisplayName).Select(m => UserRef.From(m.User)).ToList());

    private static string ListName(string? name)
    {
        var value = (name ?? "").Trim();
        return value.Length is 0 or > 120 ? throw ApiException.Invalid("Der Listenname braucht 1 bis 120 Zeichen.") : value;
    }

    private static string ListColor(string color) =>
        Values.ListColors.Contains(color) ? color : throw ApiException.Invalid("Unbekannte Farbe.");

    // ------------------------------------------------------------------ Projekte

    private static void MapProjects(RouteGroupBuilder api)
    {
        var projects = api.MapGroup("/projects");

        projects.MapGet("/", async (bool? all, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Projects.AsQueryable();
            if (all != true)
            {
                query = query.Where(p => !p.Archived);
            }
            var items = await query.OrderBy(p => p.TeamName).ThenBy(p => p.Number).ThenBy(p => p.Name).ToListAsync(ct);
            var counts = await db.Tasks
                .Where(t => t.ProjectId != null && t.Status == Values.Open)
                .GroupBy(t => t.ProjectId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return items.Select(p => ProjectDto.From(p, counts.GetValueOrDefault(p.Id)));
        });

        projects.MapPost("/", async (ProjectRequest req, CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var project = new Project
            {
                Number = Clean(req.Number, 32),
                Name = ProjectName(req.Name),
                TeamName = Clean(req.TeamName, 200),
                CreatedById = me.Id,
                CreatedAt = clock.UtcNow,
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/projects/{project.Id}", ProjectDto.From(project));
        });

        projects.MapPatch("/{id:int}", async (int id, ProjectRequest req, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FindAsync([id], ct) ?? throw ApiException.NotFound("Projekt");
            if (project.CreatedById != me.Id && !me.IsAdmin)
            {
                throw ApiException.Forbidden("Projekte ändern kann die Person, die es angelegt hat, oder die Verwaltung.");
            }
            if (req.Number is not null) project.Number = Clean(req.Number, 32);
            if (req.Name is not null) project.Name = ProjectName(req.Name);
            if (req.TeamName is not null) project.TeamName = Clean(req.TeamName, 200);
            if (req.Archived is bool archived)
            {
                if (!me.IsAdmin)
                {
                    throw ApiException.Forbidden("Archivieren darf nur die Verwaltung.");
                }
                project.Archived = archived;
            }
            await db.SaveChangesAsync(ct);
            return ProjectDto.From(project);
        });

        // Wird von der Konfigurationsseite des Kanal-Tabs in Teams aufgerufen.
        projects.MapPost("/channel", async (ChannelLinkRequest req, CurrentUser me, AppDbContext db, AppClock clock, CancellationToken ct) =>
        {
            var channelId = Clean(req.ChannelId, 200) ?? throw ApiException.Invalid("Kanal fehlt.");
            var linked = await db.Projects.FirstOrDefaultAsync(p => p.TeamsChannelId == channelId, ct);
            if (req.ProjectId is int pid)
            {
                var project = await db.Projects.FindAsync([pid], ct) ?? throw ApiException.NotFound("Projekt");
                if (linked is not null && linked.Id != project.Id)
                {
                    linked.TeamsChannelId = null;
                }
                project.TeamsChannelId = channelId;
                project.TeamsTeamId = Clean(req.TeamId, 100);
                project.TeamName ??= Clean(req.TeamName, 200);
                await db.SaveChangesAsync(ct);
                return ProjectDto.From(project);
            }
            if (linked is not null)
            {
                return ProjectDto.From(linked);
            }
            var (number, name) = SplitChannelName(req.ChannelName ?? "Projekt");
            var created = new Project
            {
                Number = number,
                Name = name,
                TeamName = Clean(req.TeamName, 200),
                TeamsTeamId = Clean(req.TeamId, 100),
                TeamsChannelId = channelId,
                CreatedById = me.Id,
                CreatedAt = clock.UtcNow,
            };
            db.Projects.Add(created);
            await db.SaveChangesAsync(ct);
            return ProjectDto.From(created);
        });

        projects.MapGet("/channel/{channelId}", async (string channelId, AppDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.TeamsChannelId == channelId, ct);
            return project is null ? Results.NotFound(new { error = "Dieser Kanal ist noch keinem Projekt zugeordnet." }) : Results.Ok(ProjectDto.From(project));
        });
    }

    /// <summary>"1001 Lindenpark" → ("1001", "Lindenpark"). Viele Organisationen beginnen Kanalnamen mit der Projektnummer.</summary>
    internal static (string? Number, string Name) SplitChannelName(string channelName)
    {
        var value = channelName.Trim();
        var space = value.IndexOf(' ');
        if (space > 0 && value[..space].All(char.IsDigit) && space <= 8)
        {
            return (value[..space], value[(space + 1)..].Trim());
        }
        return (null, value.Length > 200 ? value[..200] : value);
    }

    private static string ProjectName(string? name)
    {
        var value = (name ?? "").Trim();
        return value.Length is 0 or > 200 ? throw ApiException.Invalid("Der Projektname braucht 1 bis 200 Zeichen.") : value;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    // ------------------------------------------------------------------ Vorlagen

    private static void MapTemplates(RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/templates");
        templates.MapGet("/", (TemplateService svc, CancellationToken ct) => svc.ListAsync(ct));
        templates.MapPost("/", async (TemplateRequest req, TemplateService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(req, ct);
            return Results.Created($"/api/templates/{created.Id}", created);
        });
        templates.MapPut("/{id:int}", (int id, TemplateRequest req, TemplateService svc, CancellationToken ct) => svc.UpdateAsync(id, req, ct));
        templates.MapDelete("/{id:int}", async (int id, TemplateService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        templates.MapPost("/{id:int}/apply", (int id, ApplyTemplateRequest req, TemplateService svc, CancellationToken ct) => svc.ApplyAsync(id, req, ct));
    }

    // ------------------------------------------------------------------ Verwaltung

    private static void MapAdmin(RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var me = ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
            if (!me.IsAdmin)
            {
                throw ApiException.Forbidden("Nur für die Verwaltung (App-Rolle Aufgaben.Admin).");
            }
            return await next(ctx);
        });

        admin.MapGet("/status", async (AppDbContext db, GraphClient graph, MailNotifier mail, TeamsNotifier teams, MailInboxPoller inbox, AppClock clock,
            IOptions<AufgabenOptions> opts, CancellationToken ct) =>
        {
            var week = clock.UtcNow.AddDays(-7);
            return new AdminStatusDto(
                graph.IsConfigured,
                mail.IsAvailable,
                teams.IsAvailable,
                await db.Users.CountAsync(u => u.IsActive, ct),
                await db.Notifications.CountAsync(n => n.SentAt == null && n.Attempts < BackgroundWorker.MaxAttempts, ct),
                await db.Notifications.CountAsync(n => n.SentAt == null && n.Attempts >= BackgroundWorker.MaxAttempts, ct),
                DirectorySync.LastRun,
                DirectorySync.LastResult,
                inbox.IsConfigured,
                MailInboxPoller.LastRun,
                MailInboxPoller.LastResult,
                await db.InboundMails.CountAsync(m => m.Result == InboundResult.Rejected && m.ProcessedAt >= week, ct),
                TeamsPackage.AppVersion,
                PrivacyPage.Missing(opts.Value.Privacy, opts.Value.Mistral.Enabled));
        });

        // Teams-App-Paket aus der laufenden Konfiguration, passt immer zu Adresse, Entra-App und Version
        admin.MapGet("/teams-package", (IOptions<AufgabenOptions> opts, IConfiguration config) =>
        {
            var o = opts.Value;
            var clientId = config["AzureAd:ClientId"] ?? "";
            var problem = TeamsPackage.Problem(o.NormalizedBaseUrl, clientId, o.Teams.AppId);
            if (problem is not null)
            {
                throw ApiException.Invalid(problem);
            }
            var version = TeamsPackage.AppVersion;
            if (!string.IsNullOrWhiteSpace(o.Teams.BotId) && !o.Teams.BotEnabled)
            {
                throw ApiException.Invalid("Aufgaben__Teams__BotId ist keine GUID, bitte die Bot-ID aus dem Teams Developer Portal eintragen oder leer lassen.");
            }
            var zip = TeamsPackage.Build(o.NormalizedBaseUrl, clientId, o.Teams.AppId, o.Teams.Developer, version, o.Teams.BotId);
            return Results.File(zip, "application/zip", $"aufgaben-teams-{version}.zip");
        });

        admin.MapPost("/mail-inbox/poll", async (MailInboxPoller inbox, CancellationToken ct) =>
        {
            if (!inbox.IsConfigured)
            {
                throw ApiException.Invalid("Das Aufgaben-Postfach ist nicht konfiguriert (Aufgaben:MailInbox).");
            }
            try
            {
                var created = await inbox.PollAsync(ct);
                return new { created, result = MailInboxPoller.LastResult };
            }
            catch (Exception ex) when (ex is not ApiException and not OperationCanceledException)
            {
                throw new ApiException(502, "Postfachabruf fehlgeschlagen: " + Short(ex.Message));
            }
        });

        admin.MapPost("/directory-sync", async (DirectorySync sync, CancellationToken ct) =>
        {
            try
            {
                var (added, updated, deactivated) = await sync.RunAsync(ct);
                return new { added, updated, deactivated };
            }
            catch (Exception ex) when (ex is not ApiException and not OperationCanceledException)
            {
                throw new ApiException(502, "Verzeichnisabgleich fehlgeschlagen: " + Short(ex.Message));
            }
        });
    }
}
