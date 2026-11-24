using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Data;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Aufgaben.Tests;

/// <summary>Startet PostgreSQL in Docker und die App einmal für alle API-Tests.</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public WebApplicationFactory<Program> WithAbsences { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // Umgebungsvariablen überschreiben appsettings.json und sind schon beim Start sichtbar.
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__Aufgaben"] = _db.GetConnectionString(),
            ["Aufgaben__BaseUrl"] = "http://localhost",
            ["Aufgaben__Auth__Mode"] = "Dev",
            ["Aufgaben__Auth__AllowDevLogin"] = "true",
            ["Aufgaben__Worker__Enabled"] = "false",
            ["Aufgaben__Directory__SyncEnabled"] = "false",
            ["Aufgaben__InboxViewUsers"] = " THOMAS.BECKER@example.test, jonas.wagner@example.test ",
            // Datenschutzhinweise: Speicherdauer fehlt absichtlich (siehe PrivacyPageApiTests)
            ["Aufgaben__Privacy__Controller"] = "Beispiel GmbH | Musterstraße 1 | 12345 Musterstadt",
            ["Aufgaben__Privacy__DpoContact"] = "<script>alert(1)</script> | datenschutz@example.test",
        };
        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        _ = Factory.Server; // App starten (Migration + Testdaten)
        WithAbsences = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.PostConfigure<AufgabenOptions>(o => o.AbsencesEnabled = true)));
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>HTTP-Client, der als Testperson angemeldet ist.</summary>
    public async Task<HttpClient> LoginAsync(string oid, WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var res = await client.GetAsync($"/auth/dev-login?oid={oid}");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        client.DefaultRequestHeaders.Add("X-Aufgaben-Request", "1");
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<AppFixture>;

[Collection("api")]
public sealed class ApiTests(AppFixture app)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private async Task<int> UserId(string oid) =>
        await app.WithDbAsync(db => db.Users.Where(u => u.EntraOid == oid).Select(u => u.Id).SingleAsync());

    [Fact]
    public async Task Unauthenticated_api_calls_get_401()
    {
        var client = app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Only_configured_accounts_can_open_the_inbox_and_others_find_unfiled_tasks_in_planned()
    {
        using var anna = await app.LoginAsync("dev-anna");
        using var thomas = await app.LoginAsync("dev-thomas");
        Assert.False((await Read<MeDto>(await anna.GetAsync("/api/me"))).ShowInbox);
        Assert.True((await Read<MeDto>(await thomas.GetAsync("/api/me"))).ShowInbox);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync("/api/tasks?view=aufgaben")).StatusCode);

        var countBefore = (await Read<CountsDto>(await anna.GetAsync("/api/counts"))).Planned;
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Ohne Termin planen" }));
        Assert.Null(task.DueDate);
        Assert.Contains(await Read<List<TaskDto>>(await anna.GetAsync("/api/tasks?view=geplant")), t => t.Id == task.Id);
        Assert.Equal(countBefore + 1, (await Read<CountsDto>(await anna.GetAsync("/api/counts"))).Planned);
        Assert.DoesNotContain(await Read<List<TaskDto>>(await thomas.GetAsync("/api/tasks?view=geplant")), t => t.Id == task.Id);

        var inboxTask = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Ohne Termin im Posteingang" }));
        Assert.Contains(await Read<List<TaskDto>>(await thomas.GetAsync("/api/tasks?view=aufgaben")), t => t.Id == inboxTask.Id);
        Assert.DoesNotContain(await Read<List<TaskDto>>(await thomas.GetAsync("/api/tasks?view=geplant")), t => t.Id == inboxTask.Id);
    }

    [Fact]
    public async Task Changing_requests_without_csrf_header_are_rejected()
    {
        var client = await app.LoginAsync("dev-anna");
        client.DefaultRequestHeaders.Remove("X-Aufgaben-Request");
        var res = await client.PostAsJsonAsync("/api/tasks", new { title = "Ohne Schutzkennung" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Personal_task_appears_in_my_day_and_is_private()
    {
        var anna = await app.LoginAsync("dev-anna");
        var created = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Privat: Zahnarzt verschieben", myDay = true }));
        Assert.True(created.MyDay);
        Assert.False(created.IsDelegated);

        var myDay = await Read<List<TaskDto>>(await anna.GetAsync("/api/tasks?view=mein-tag"));
        Assert.Contains(myDay, t => t.Id == created.Id);

        var thomas = await app.LoginAsync("dev-thomas");
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{created.Id}")).StatusCode);
        var search = await Read<List<TaskDto>>(await thomas.GetAsync("/api/tasks?view=suche&q=Zahnarzt"));
        Assert.DoesNotContain(search, t => t.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.PatchAsJsonAsync($"/api/tasks/{created.Id}", new { title = "gehackt" })).StatusCode);
    }

    [Fact]
    public async Task Delegation_notifies_assignee_and_completion_notifies_creator()
    {
        var anna = await app.LoginAsync("dev-anna");
        var mariaId = await UserId("dev-maria");
        var annaId = await UserId("dev-anna");

        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Mietvertrag Whg. 2.1 vorbereiten", assigneeId = mariaId, dueDate = "2026-12-01", myDay = true }));
        Assert.True(task.IsDelegated);
        Assert.Equal(mariaId, task.Assignee.Id);
        Assert.False(task.MyDay); // "Mein Tag" der anlegenden Person wird nicht an andere weitergegeben

        var assigned = await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == mariaId && n.TaskId == task.Id && n.Kind == NotificationOutbox.Assigned));
        Assert.True(assigned);

        var maria = await app.LoginAsync("dev-maria");
        var inbox = await Read<List<TaskDto>>(await maria.GetAsync("/api/tasks?view=zugewiesen"));
        Assert.Contains(inbox, t => t.Id == task.Id);

        await Read<TaskDto>(await maria.PostAsync($"/api/tasks/{task.Id}/complete", null));
        var completed = await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == annaId && n.TaskId == task.Id && n.Kind == NotificationOutbox.Completed));
        Assert.True(completed);

        var waiting = await Read<List<TaskDto>>(await anna.GetAsync("/api/tasks?view=delegiert"));
        var item = Assert.Single(waiting, t => t.Id == task.Id);
        Assert.Equal("erledigt", item.Status);
        Assert.Equal("Maria Klein", item.CompletedBy?.Name);
    }

    [Fact]
    public async Task Project_tasks_are_visible_to_everyone_but_only_creator_can_delete()
    {
        var anna = await app.LoginAsync("dev-anna");
        var projectId = await app.WithDbAsync(db => db.Projects.Where(p => p.Number == "1001").Select(p => p.Id).SingleAsync());
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Gerüst abbauen lassen", projectId }));

        var thomas = await app.LoginAsync("dev-thomas");
        var board = await Read<List<TaskDto>>(await thomas.GetAsync($"/api/tasks?view=projekt&id={projectId}"));
        Assert.Contains(board, t => t.Id == task.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await thomas.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Completing_a_recurring_task_creates_the_next_occurrence()
    {
        var anna = await app.LoginAsync("dev-anna");
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new
        {
            title = "Baustellenrundgang Wiederholungstest",
            recurrence = "woechentlich",
            dueDate = "2030-01-07",
            checklist = new[] { "Fotos", "Protokoll" },
        }));
        await Read<TaskDto>(await anna.PostAsync($"/api/tasks/{task.Id}/complete", null));

        var next = await app.WithDbAsync(db => db.Tasks.Include(t => t.Checklist)
            .SingleAsync(t => t.Title == "Baustellenrundgang Wiederholungstest" && t.Status == Values.Open));
        Assert.Equal(new DateOnly(2030, 1, 14), next.DueDate);
        Assert.Equal("wiederholung", next.Source);
        Assert.Equal(2, next.Checklist.Count);
        Assert.All(next.Checklist, c => Assert.False(c.Done));
    }

    [Fact]
    public async Task Patch_validates_fields_and_rejects_unknown_ones()
    {
        var anna = await app.LoginAsync("dev-anna");
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Validierung" }));

        Assert.Equal(HttpStatusCode.BadRequest, (await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { priority = "extrem" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { createdById = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { dueTime = "10:00" })).StatusCode);

        var updated = await Read<TaskDto>(await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { dueDate = "2026-12-24", dueTime = "09:30", priority = "hoch" }));
        Assert.Equal("2026-12-24", updated.DueDate);
        Assert.Equal("09:30", updated.DueTime);
        Assert.Equal("hoch", updated.Priority);

        var conflict = await anna.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Alt", version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Comments_notify_the_other_person()
    {
        var anna = await app.LoginAsync("dev-anna");
        var thomasId = await UserId("dev-thomas");
        var task = await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Kommentartest", assigneeId = thomasId }));
        var ev = await Read<EventDto>(await anna.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { text = "Bitte bis Freitag" }));
        Assert.Equal("kommentar", ev.Kind);

        var notified = await app.WithDbAsync(db => db.Notifications.AnyAsync(n => n.UserId == thomasId && n.TaskId == task.Id && n.Kind == NotificationOutbox.Commented));
        Assert.True(notified);
    }

    [Fact]
    public async Task Calendar_feed_requires_the_secret_link()
    {
        var anna = await app.LoginAsync("dev-anna");
        await Read<TaskDto>(await anna.PostAsJsonAsync("/api/tasks", new { title = "Termin im Kalender", dueDate = "2030-03-01", dueTime = "10:00" }));
        var feed = await Read<CalendarFeedDto>(await anna.PostAsync("/api/me/calendar-feed", null));
        var path = new Uri(feed.Url).AbsolutePath;

        var anonymous = app.Factory.CreateClient();
        var res = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/calendar", res.Content.Headers.ContentType?.MediaType);
        var ics = await res.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("SUMMARY:Aufgabe: Termin im Kalender", ics);
        Assert.Contains("DTSTART:20300301T090000Z", ics); // 10:00 Berlin = 09:00 UTC im Winter

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/kalender/falsches-token-falsches-token-123.ics")).StatusCode);

        await anna.DeleteAsync("/api/me/calendar-feed");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Templates_create_all_steps_relative_to_the_start_date()
    {
        var anna = await app.LoginAsync("dev-anna");
        var templates = await Read<List<TemplateDto>>(await anna.GetAsync("/api/templates"));
        var template = Assert.Single(templates, t => t.Name == "Mieterwechsel");
        var maria = await UserId("dev-maria");

        var result = await Read<ApplyTemplateResult>(await anna.PostAsJsonAsync($"/api/templates/{template.Id}/apply", new
        {
            startDate = "2027-03-31",
            defaultAssigneeId = maria,
            titleSuffix = "Whg. 9.9 Teststraße",
        }));
        Assert.Equal(template.Steps.Count, result.Created);

        var first = await Read<TaskDetailDto>(await anna.GetAsync($"/api/tasks/{result.TaskIds[0]}"));
        Assert.Equal("2027-03-01", first.Task.DueDate); // -30 Tage
        Assert.EndsWith(" · Whg. 9.9 Teststraße", first.Task.Title);
        Assert.Equal(maria, first.Task.Assignee.Id);
        Assert.NotEmpty(first.Task.Checklist);
    }

    [Fact]
    public async Task Lists_belong_to_their_owner()
    {
        var anna = await app.LoginAsync("dev-anna");
        var list = await Read<ListDto>(await anna.PostAsJsonAsync("/api/lists", new { name = "Privatliste", color = "gruen" }));

        var thomas = await app.LoginAsync("dev-thomas");
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks?view=liste&id={list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await thomas.PostAsJsonAsync("/api/tasks", new { title = "Fremde Liste", listId = list.Id })).StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_require_admin_role()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        Assert.Equal(HttpStatusCode.Forbidden, (await thomas.GetAsync("/api/admin/status")).StatusCode);
        var anna = await app.LoginAsync("dev-anna");
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync("/api/admin/status")).StatusCode);
    }

    [Fact]
    public async Task Security_headers_allow_teams_framing_only()
    {
        var client = app.Factory.CreateClient();
        var res = await client.GetAsync("/healthz");
        var csp = string.Join(";", res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'self' https://teams.microsoft.com", csp);
        Assert.Contains("nosniff", string.Join(",", res.Headers.GetValues("X-Content-Type-Options")));
    }
}
