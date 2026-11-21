using System.IO.Compression;
using System.Threading.RateLimiting;
using Aufgaben.Api.Auth;
using Aufgaben.Api.Data;
using Aufgaben.Api.Endpoints;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Container-Healthcheck ohne curl (das Laufzeit-Image enthält keine Shell-Werkzeuge).
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        var res = await http.GetAsync($"http://127.0.0.1:{port}/healthz");
        return res.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- Einstellungen
builder.Services.AddOptions<AufgabenOptions>().Bind(builder.Configuration.GetSection(AufgabenOptions.Section));
var opts = builder.Configuration.GetSection(AufgabenOptions.Section).Get<AufgabenOptions>() ?? new AufgabenOptions();

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 1024 * 1024; // 1 MB – die App lädt keine Dateien hoch
});

// ---------------------------------------------------------------- Datenbank
var connectionString = builder.Configuration.GetConnectionString("Aufgaben")
    ?? throw new InvalidOperationException("ConnectionStrings:Aufgaben fehlt.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("aufgaben");
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("datenbank", tags: ["ready"]);

// ---------------------------------------------------------------- Anmeldung
builder.AddAufgabenAuth(opts);
builder.Services.AddAuthorization();

// ---------------------------------------------------------------- Schutz
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Kestrel ist nur über 127.0.0.1 bzw. das Docker-Netz erreichbar; davor sitzt Nginx.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = false;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        return new ValueTask(ctx.HttpContext.Response.WriteAsync(
            "{\"error\":\"Zu viele Anfragen in kurzer Zeit. Bitte einen Moment warten.\"}", token));
    };
    // Angemeldet: Limit pro Person. Anonym: pro IP – großzügiger, weil sich in Firmen
    // oft alle Mitarbeitenden eine öffentliche Adresse (NAT) teilen.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var oid = ctx.User.Oid();
        var key = oid is not null ? "u:" + oid : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unbekannt");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = oid is not null ? 600 : 1500,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
    // Kalender-Feed: Limit pro Feed-Link, nicht pro IP (alle Outlook-Clients kommen von der Firmen-IP).
    o.AddPolicy("kalender", ctx => RateLimitPartition.GetFixedWindowLimiter(
        "feed:" + ctx.Request.Path,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------------------------------------------------------------- Kompression
// Brotli/Gzip für JSON und Oberfläche. Live-Updates (text/event-stream) bleiben unkomprimiert,
// damit Nachrichten sofort durchgereicht werden.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/manifest+json", "image/svg+xml", "text/calendar"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// ---------------------------------------------------------------- Dienste
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<RealtimeHub>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<UserProvisioning>();
builder.Services.AddScoped<NotificationOutbox>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<DirectorySync>();
builder.Services.AddScoped<TeamsNotifier>();
builder.Services.AddScoped<MailNotifier>();
builder.Services.AddScoped<MailInboxProcessor>();
builder.Services.AddSingleton<MailInboxPoller>();
builder.Services.AddHttpClient<GraphClient>(c =>
{
    c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
if (opts.Worker.Enabled)
{
    builder.Services.AddHostedService<BackgroundWorker>();
}

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);

var app = builder.Build();

// ---------------------------------------------------------------- Datenbank vorbereiten
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<AppClock>();
    if (opts.Database.AutoMigrate)
    {
        await db.Database.MigrateAsync();
    }
    if (opts.Auth.IsDev)
    {
        await Seed.DevDataAsync(db, clock, CancellationToken.None);
    }
    if (opts.Database.SeedExampleTemplates)
    {
        await Seed.ExampleTemplatesAsync(db, clock, CancellationToken.None);
    }
}

// ---------------------------------------------------------------- Pipeline
app.UseForwardedHeaders();
// Im Betrieb: Schema und Host fest aus Aufgaben__BaseUrl – die Anmeldung funktioniert dann auch,
// wenn der Reverse Proxy Host oder X-Forwarded-Proto nicht (richtig) weiterreicht.
if (!opts.Auth.IsDev && CanonicalUrl.TryParse(opts.NormalizedBaseUrl, out var publicUri))
{
    app.Use((context, next) =>
    {
        CanonicalUrl.Apply(context.Request, publicUri);
        return next(context);
    });
}
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();
if (opts.IsHttps && !app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Gebaute Dateien haben einen Hash im Namen und dürfen lange gecacht werden, index.html nicht.
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl = path.StartsWith("/assets/")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<CsrfMiddleware>();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapCalendarFeed();

// Datenschutzhinweise mit den Angaben aus der .env (Aufgaben__Privacy__…), öffentlich wie bisher die statische Seite
app.MapMethods("/datenschutz.html", [HttpMethods.Get, HttpMethods.Head], (HttpContext ctx, IOptions<AufgabenOptions> o) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    return Results.Content(PrivacyPage.Render(o.Value.Privacy), "text/html; charset=utf-8");
}).AllowAnonymous();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapTaskEndpoints();
api.MapAppEndpoints();
api.MapAccessEndpoints();
api.MapEventStream();

app.MapFallback("/api/{**rest}", () => Results.NotFound(new { error = "Unbekannter Endpunkt." }));
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

var log = app.Services.GetRequiredService<ILogger<Program>>();
log.LogInformation("Aufgaben startet: {BaseUrl}, Anmeldung {Mode}, Zeitzone {Zone}", opts.NormalizedBaseUrl, opts.Auth.Mode, opts.TimeZone);
if (opts.Auth.IsDev)
{
    log.LogWarning("ENTWICKLUNGS-LOGIN AKTIV – nur für lokale Tests, niemals im Betrieb verwenden.");
}

await app.RunAsync();
return 0;

public partial class Program;
