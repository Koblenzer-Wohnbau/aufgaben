using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Aufgaben.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Aufgaben.Tests;

public class TeamsBotTests
{
    private const string Tenant = "10000000-0000-0000-0000-000000000001";
    private const string ServiceUrl = "https://smba.trafficmanager.net/emea/";

    /// <summary>So schickt Teams „Als Aufgabe“ an einer Nachricht (gekürzt, Werte ausgedacht).</summary>
    private static JsonElement Activity(string channel = "msteams", string tenant = Tenant, string commandId = "alsAufgabe") =>
        JsonDocument.Parse($$"""
        {
          "type": "invoke",
          "name": "composeExtension/fetchTask",
          "channelId": "{{channel}}",
          "serviceUrl": "{{ServiceUrl}}",
          "from": { "id": "29:abc", "aadObjectId": "oid-anna" },
          "conversation": { "conversationType": "personal", "tenantId": "{{tenant}}", "id": "a:1" },
          "channelData": { "tenant": { "id": "{{tenant}}" }, "source": { "name": "message" } },
          "value": {
            "commandId": "{{commandId}}",
            "commandContext": "message",
            "messagePayload": {
              "id": "1700000000000",
              "createdDateTime": "2026-11-21T09:15:00.000Z",
              "body": {
                "contentType": "html",
                "content": "<p>Hallo <at id=\"0\">Anna</at>,</p><p>kannst du bis <strong>Freitag</strong> das Angebot &amp; die Pläne prüfen? <emoji id=\"smile\" alt=\"😊\" title=\"Lächeln\"></emoji></p><attachment id=\"x\"></attachment>"
              },
              "from": { "user": { "id": "29:def", "displayName": "Thomas Becker" } },
              "linkToMessage": "https://teams.microsoft.com/l/message/19:abc@thread.v2/1700000000000?context=%7B%7D"
            }
          }
        }
        """).RootElement;

    [Fact]
    public void Message_from_teams_is_read_as_plain_text_with_sender_time_and_link()
    {
        var m = TeamsBot.ReadMessage(Activity(), 40_000);

        Assert.Equal("alsAufgabe", m.CommandId);
        Assert.Equal("oid-anna", m.UserOid);
        Assert.Equal("Hallo Anna,\nkannst du bis Freitag das Angebot & die Pläne prüfen? 😊", m.Text);
        Assert.Equal("Thomas Becker", m.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 9, 15, 0, TimeSpan.Zero), m.SentAt);
        Assert.StartsWith("https://teams.microsoft.com/l/message/", m.Link);
        Assert.Equal(10, TeamsBot.ReadMessage(Activity(), 10).Text.Length);
    }

    [Theory]
    [InlineData("Zeile 1<br>Zeile 2<br/><br><br>Zeile 3", "html", "Zeile 1\nZeile 2\n\nZeile 3")]
    [InlineData("<div>a &lt;b&gt;</div><script>alert(1)</script>", "html", "a <b>")]
    [InlineData("  schon Text <b>bleibt</b>  ", "text", "schon Text <b>bleibt</b>")]
    [InlineData(null, "html", "")]
    public void Html_becomes_readable_text(string? content, string type, string expected) =>
        Assert.Equal(expected, TeamsBot.MessageText(content, type));

    [Fact]
    public void Calls_from_other_channels_tenants_or_with_other_service_url_are_rejected()
    {
        Assert.Null(TeamsBot.Reject(Activity(), ServiceUrl, Tenant));
        Assert.Null(TeamsBot.Reject(Activity(), ServiceUrl.TrimEnd('/'), Tenant.ToUpperInvariant()));
        Assert.NotNull(TeamsBot.Reject(Activity(channel: "webchat"), ServiceUrl, Tenant));
        Assert.NotNull(TeamsBot.Reject(Activity(tenant: "20000000-0000-0000-0000-000000000002"), ServiceUrl, Tenant));
        Assert.NotNull(TeamsBot.Reject(Activity(), "https://evil.example/", Tenant));
        Assert.NotNull(TeamsBot.Reject(Activity(), null, Tenant));
        Assert.NotNull(TeamsBot.Reject(Activity(), ServiceUrl, ""));
    }

    [Theory]
    [InlineData("https://teams.microsoft.com/l/message/19:a/1", true)]
    [InlineData("https://teams.cloud.microsoft/l/message/19:a/1", true)]
    [InlineData("http://teams.microsoft.com/l/message/19:a/1", false)]
    [InlineData("https://teams.microsoft.com.evil.example/l/1", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Only_real_teams_links_are_kept(string link, bool kept) =>
        Assert.Equal(kept, TeamsBot.SafeLink(link) is not null);

    [Fact]
    public void Only_keys_endorsed_for_teams_are_accepted()
    {
        // Aufbau wie das Schlüsselverzeichnis des Bot Framework (login.botframework.com)
        var set = new JsonWebKeySet("""
        { "keys": [
          { "kty": "RSA", "use": "sig", "kid": "teams", "n": "sXch", "e": "AQAB", "endorsements": ["msteams", "emulator"] },
          { "kty": "RSA", "use": "sig", "kid": "andere", "n": "sXch", "e": "AQAB", "endorsements": ["skype"] },
          { "kty": "RSA", "use": "sig", "kid": "ohne", "n": "sXch", "e": "AQAB" }
        ] }
        """);
        Assert.True(TeamsBot.Endorses(set.Keys.Single(k => k.Kid == "teams"), TeamsBot.Channel));
        Assert.False(TeamsBot.Endorses(set.Keys.Single(k => k.Kid == "andere"), TeamsBot.Channel));
        Assert.False(TeamsBot.Endorses(set.Keys.Single(k => k.Kid == "ohne"), TeamsBot.Channel));
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Drafts_belong_to_one_person_and_expire()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var store = new TeamsDraftStore(time);
        var id = store.Add("oid-anna", "Text", "Thomas", null, null)!;

        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal("Text", store.Get(id, "OID-ANNA")?.Text);
        Assert.Null(store.Get(id, "oid-thomas"));
        Assert.Null(store.Get("gibt-es-nicht", "oid-anna"));

        time.Now += TeamsDraftStore.Lifetime;
        Assert.Null(store.Get(id, "oid-anna"));
    }

    [Fact]
    public void Package_gets_the_message_action_only_with_a_bot_id()
    {
        const string botId = "99999999-8888-7777-6666-555555555555";
        static JsonElement Manifest(byte[] zip)
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        }
        const string baseUrl = "https://aufgaben.example.test";
        const string clientId = "11111111-2222-3333-4444-555555555555";
        const string appId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

        var without = Manifest(TeamsPackage.Build(baseUrl, clientId, appId, "Müller GmbH", "1.0.9"));
        Assert.False(without.TryGetProperty("composeExtensions", out _));

        var with = Manifest(TeamsPackage.Build(baseUrl, clientId, appId, "Müller GmbH", "1.0.9", botId));
        var extension = with.GetProperty("composeExtensions")[0];
        Assert.Equal(botId, extension.GetProperty("botId").GetString());
        var command = extension.GetProperty("commands")[0];
        Assert.Equal(TeamsBot.CommandId, command.GetProperty("id").GetString());
        Assert.Equal("action", command.GetProperty("type").GetString());
        Assert.Equal("message", command.GetProperty("context")[0].GetString());
        Assert.True(command.GetProperty("fetchTask").GetBoolean());
        Assert.True(command.GetProperty("title").GetString()!.Length <= 32);
        // alles andere bleibt wie ohne Bot
        Assert.Equal("Müller GmbH", with.GetProperty("developer").GetProperty("name").GetString());
        Assert.Equal(
            without.GetProperty("staticTabs")[0].GetProperty("contentUrl").GetString(),
            with.GetProperty("staticTabs")[0].GetProperty("contentUrl").GetString());
        Assert.Equal(
            without.GetProperty("activities").GetProperty("activityTypes").GetArrayLength(),
            with.GetProperty("activities").GetProperty("activityTypes").GetArrayLength());
    }
}

[Collection("api")]
public sealed class TeamsDraftApiTests(AppFixture app)
{
    [Fact]
    public async Task Only_the_person_who_chose_the_message_gets_the_draft()
    {
        var store = app.Factory.Services.GetRequiredService<TeamsDraftStore>();
        var id = store.Add("dev-anna", "Bitte Angebot prüfen", "Thomas Becker", DateTimeOffset.UtcNow, "https://teams.microsoft.com/l/message/19:a/1")!;

        var anna = await app.LoginAsync("dev-anna");
        var draft = JsonDocument.Parse(await anna.GetStringAsync($"/api/teams/entwurf/{id}")).RootElement;
        Assert.Equal("Bitte Angebot prüfen", draft.GetProperty("text").GetString());
        Assert.Equal("Thomas Becker", draft.GetProperty("from").GetString());

        var thomas = await app.LoginAsync("dev-thomas");
        var res = await thomas.GetAsync($"/api/teams/entwurf/{id}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("abgelaufen", await res.Content.ReadAsStringAsync());

        // Ohne Bot-ID gibt es den Endpunkt für Teams nicht
        Assert.Equal(HttpStatusCode.NotFound, (await anna.PostAsync("/api/teams/bot", new StringContent("{}"))).StatusCode);
    }
}
