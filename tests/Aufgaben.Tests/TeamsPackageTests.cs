using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Aufgaben.Tests;

public class TeamsPackageTests
{
    private const string BaseUrl = "https://aufgaben.example.test";
    private const string ClientId = "11111111-2222-3333-4444-555555555555";
    private const string AppId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static (JsonElement Manifest, ZipArchive Zip) Open(byte[] bytes)
    {
        var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        return (JsonDocument.Parse(reader.ReadToEnd()).RootElement, zip);
    }

    [Fact]
    public void Package_contains_manifest_with_server_values_and_both_icons()
    {
        var (m, zip) = Open(TeamsPackage.Build(BaseUrl + "/", ClientId, AppId, "Beispiel GmbH", "1.2.3"));

        Assert.Equal(AppId, m.GetProperty("id").GetString());
        Assert.Equal("1.2.3", m.GetProperty("version").GetString());
        Assert.Equal("Beispiel GmbH", m.GetProperty("developer").GetProperty("name").GetString());
        Assert.Equal(BaseUrl, m.GetProperty("developer").GetProperty("websiteUrl").GetString());
        Assert.Equal(ClientId, m.GetProperty("webApplicationInfo").GetProperty("id").GetString());
        Assert.Equal($"api://aufgaben.example.test/{ClientId}", m.GetProperty("webApplicationInfo").GetProperty("resource").GetString());
        Assert.Equal("aufgaben.example.test", m.GetProperty("validDomains")[0].GetString());
        Assert.StartsWith(BaseUrl + "/?inTeams=1", m.GetProperty("staticTabs")[0].GetProperty("contentUrl").GetString());
        Assert.DoesNotContain("{{", m.GetRawText());
        foreach (var icon in new[] { "color.png", "outline.png" })
        {
            var entry = zip.GetEntry(icon);
            Assert.NotNull(entry);
            Assert.True(entry.Length > 100, $"{icon} ist leer");
        }
    }

    [Fact]
    public void Developer_name_is_escaped_shortened_and_has_a_default()
    {
        var (quoted, _) = Open(TeamsPackage.Build(BaseUrl, ClientId, AppId, "Müller \"&\" Söhne", "1.0.0"));
        Assert.Equal("Müller \"&\" Söhne", quoted.GetProperty("developer").GetProperty("name").GetString());

        var (longName, _) = Open(TeamsPackage.Build(BaseUrl, ClientId, AppId, new string('x', 50), "1.0.0"));
        Assert.Equal(TeamsPackage.MaxDeveloperLength, longName.GetProperty("developer").GetProperty("name").GetString()!.Length);

        var (empty, _) = Open(TeamsPackage.Build(BaseUrl, ClientId, AppId, " ", "1.0.0"));
        Assert.Equal("Aufgaben", empty.GetProperty("developer").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("http://aufgaben.example.test", ClientId, AppId, "https://")]
    [InlineData("https://192.0.2.10", ClientId, AppId, "keine IP-Adresse")]
    [InlineData(BaseUrl, "", AppId, "AzureAd__ClientId")]
    [InlineData(BaseUrl, ClientId, "", "Aufgaben__Teams__AppId")]
    public void Missing_or_wrong_values_are_named(string baseUrl, string clientId, string appId, string expected)
    {
        Assert.Contains(expected, TeamsPackage.Problem(baseUrl, clientId, appId));
    }

    [Fact]
    public void Complete_values_have_no_problem_and_version_is_semver()
    {
        Assert.Null(TeamsPackage.Problem(BaseUrl, ClientId, AppId));
        Assert.Matches(@"^\d+\.\d+\.\d+$", TeamsPackage.AppVersion);
    }
}

public class CanonicalUrlTests
{
    [Fact]
    public void Request_gets_public_scheme_and_host_even_if_proxy_sent_internal_ones()
    {
        // So kam die Anfrage an, als Nginx Host und X-Forwarded-Proto nicht weiterreichte
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1", 8090);

        Assert.True(CanonicalUrl.TryParse("https://aufgaben.example.test", out var publicUri));
        CanonicalUrl.Apply(context.Request, publicUri);

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal("aufgaben.example.test", context.Request.Host.Value);
        Assert.True(context.Request.IsHttps);
    }

    [Fact]
    public void Non_default_port_is_kept_and_invalid_addresses_are_ignored()
    {
        var context = new DefaultHttpContext();
        Assert.True(CanonicalUrl.TryParse("https://app.example.test:8443", out var withPort));
        CanonicalUrl.Apply(context.Request, withPort);
        Assert.Equal("app.example.test:8443", context.Request.Host.Value);

        Assert.False(CanonicalUrl.TryParse("", out _));
        Assert.False(CanonicalUrl.TryParse("aufgaben.example.test", out _));
        Assert.False(CanonicalUrl.TryParse("ftp://aufgaben.example.test", out _));
    }
}

[Collection("api")]
public sealed class TeamsPackageApiTests(AppFixture app)
{
    [Fact]
    public async Task Only_admins_get_the_package_and_missing_values_are_explained()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        Assert.Equal(HttpStatusCode.Forbidden, (await thomas.GetAsync("/api/admin/teams-package")).StatusCode);

        // Die Testumgebung läuft auf http://localhost – das Paket verweigert das mit klarer Meldung
        var anna = await app.LoginAsync("dev-anna");
        var res = await anna.GetAsync("/api/admin/teams-package");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("https://", await res.Content.ReadAsStringAsync());

        var status = JsonDocument.Parse(await anna.GetStringAsync("/api/admin/status")).RootElement;
        Assert.Matches(@"^\d+\.\d+\.\d+$", status.GetProperty("version").GetString());
    }
}
