using System.Net;
using System.Text.Json;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aufgaben.Tests;

public class PrivacyPageTests
{
    private const string Notice = "<p class=\"hinweis\">";

    private static PrivacyOptions Complete() => new()
    {
        Controller = "Beispiel GmbH | Musterstraße 1 | 12345 Musterstadt",
        DpoContact = "Erika Mustermann | datenschutz@example.test",
        Retention = "Erledigte Aufgaben nach 24 Monaten",
    };

    [Fact]
    public void Without_values_the_page_keeps_placeholders_and_the_template_notice()
    {
        var empty = new PrivacyOptions();
        var html = PrivacyPage.Render(empty);

        Assert.Contains(Notice, html);
        Assert.Contains("<p>[Name, Anschrift und Kontakt des Verantwortlichen]</p>", html);
        Assert.Contains("<p>[Durch den Verantwortlichen festzulegen, z. B. Löschung erledigter Aufgaben nach X Monaten", html);
        Assert.Contains("<p>[Name und Kontakt der bzw. des Datenschutzbeauftragten]</p>", html);
        Assert.Contains("<p>Die Daten werden auf einem eigenen Server im Unternehmen gespeichert.</p>", html);
        Assert.DoesNotContain("{{", html);
        Assert.Equal(
            [PrivacyPage.ControllerSetting, PrivacyPage.DpoContactSetting, PrivacyPage.RetentionSetting],
            PrivacyPage.Missing(empty));
    }

    [Fact]
    public void Complete_values_replace_the_placeholders_and_remove_the_notice()
    {
        var options = Complete();
        options.Hosting = "Rechenzentrum der Beispiel IT GmbH";
        var html = PrivacyPage.Render(options);

        Assert.DoesNotContain(Notice, html);
        Assert.Contains("<p>Beispiel GmbH<br>Musterstraße 1<br>12345 Musterstadt</p>", html);
        Assert.Contains("<p>Erika Mustermann<br>datenschutz@example.test</p>", html);
        Assert.Contains("<p>Erledigte Aufgaben nach 24 Monaten</p>", html);
        Assert.Contains("<p>Rechenzentrum der Beispiel IT GmbH</p>", html);
        Assert.DoesNotContain("[Name", html);
        Assert.DoesNotContain("eigenen Server", html);
        Assert.DoesNotContain("{{", html);
        Assert.Empty(PrivacyPage.Missing(options));
    }

    [Fact]
    public void Notice_stays_until_every_required_value_is_set()
    {
        var options = Complete();
        options.Retention = "  |  ";

        var html = PrivacyPage.Render(options);

        Assert.Contains(Notice, html);
        Assert.Contains("[Durch den Verantwortlichen festzulegen", html);
        Assert.Contains("Beispiel GmbH<br>", html);
        Assert.Equal([PrivacyPage.RetentionSetting], PrivacyPage.Missing(options));
    }

    [Fact]
    public void Html_in_values_is_shown_as_text()
    {
        var options = Complete();
        options.Controller = "<script>alert(1)</script>";
        options.DpoContact = "\"><img src=x onerror=alert(1)> & Co.";

        var html = PrivacyPage.Render(options);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("&quot;&gt;&lt;img src=x onerror=alert(1)&gt; &amp; Co.", html);
    }

    [Fact]
    public void Placeholders_inside_values_are_not_replaced_again()
    {
        var options = Complete();
        options.Controller = "{{SPEICHERDAUER}}";

        var html = PrivacyPage.Render(options);

        Assert.Contains("<p>{{SPEICHERDAUER}}</p>", html);
        Assert.Contains("<p>Erledigte Aufgaben nach 24 Monaten</p>", html);
    }

    [Theory]
    [InlineData("A | B")]
    [InlineData("A\\nB")] // so steht \n unverändert in einer .env ohne Anführungszeichen
    [InlineData("A\nB")]
    [InlineData("A\r\nB")]
    [InlineData(" A || \\n B ")]
    public void Values_are_split_into_lines(string value)
    {
        Assert.Equal(["A", "B"], PrivacyPage.Lines(value));
        Assert.Equal("A<br>B", PrivacyPage.Text(value, "leer"));
    }

    [Fact]
    public void Umlauts_stay_readable_in_the_html_source()
    {
        Assert.Equal("Müller &amp; Söhne<br>Straße 5", PrivacyPage.Text("Müller & Söhne | Straße 5", "leer"));
        Assert.Equal("leer", PrivacyPage.Text(null, "leer"));
    }
}

[Collection("api")]
public sealed class PrivacyPageApiTests(AppFixture app)
{
    [Fact]
    public async Task Privacy_page_shows_values_from_the_environment_as_text_with_security_headers()
    {
        // Werte setzt AppFixture als Umgebungsvariablen – wie die .env in Docker Compose. Speicherdauer fehlt bewusst.
        var client = app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/datenschutz.html");
        var html = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'self'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());

        Assert.Contains("<p>Beispiel GmbH<br>Musterstraße 1<br>12345 Musterstadt</p>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;<br>datenschutz@example.test", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("[Durch den Verantwortlichen festzulegen", html);
        Assert.Contains("<p class=\"hinweis\">", html);
        Assert.Contains("eigenen Server im Unternehmen", html);

        var anna = await app.LoginAsync("dev-anna");
        var status = JsonDocument.Parse(await anna.GetStringAsync("/api/admin/status")).RootElement;
        Assert.Equal(
            [PrivacyPage.RetentionSetting],
            status.GetProperty("privacyMissing").EnumerateArray().Select(e => e.GetString()).ToList());
    }
}
