using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Tests;

public class AttachmentRuleTests
{
    // 1×1-PNG
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Images_are_recognized_by_their_first_bytes_not_by_name()
    {
        Assert.Equal("image/png", AttachmentRules.ImageType(Png));
        Assert.Equal("image/jpeg", AttachmentRules.ImageType([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]));
        Assert.Equal("image/gif", AttachmentRules.ImageType("GIF89a......"u8.ToArray()));
        Assert.Equal("image/webp", AttachmentRules.ImageType("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Null(AttachmentRules.ImageType("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>"u8.ToArray()));
        Assert.Null(AttachmentRules.ImageType("%PDF-1.7"u8.ToArray()));
    }

    [Fact]
    public void Declared_content_type_is_cleaned_and_image_wins()
    {
        Assert.Equal("image/png", AttachmentRules.ContentType("application/pdf", Png));
        Assert.Equal("text/html", AttachmentRules.ContentType("text/html; charset=utf-8", "<p>x</p>"u8.ToArray()));
        Assert.Equal("application/octet-stream", AttachmentRules.ContentType("<script>alert(1)</script>", "x"u8.ToArray()));
        Assert.Equal("application/octet-stream", AttachmentRules.ContentType(null, "x"u8.ToArray()));
        // Angegebener Bildtyp ohne Bildinhalt gilt nicht
        Assert.Equal("application/octet-stream", AttachmentRules.ContentType("image/png", "<script>alert(1)</script>"u8.ToArray()));
        Assert.Equal("application/octet-stream", AttachmentRules.ContentType("image/svg+xml", "<svg/>"u8.ToArray()));
    }

    [Theory]
    [InlineData("rechnung.pdf.exe")]
    [InlineData("datei.exe.")]
    [InlineData("skript.PS1")]
    [InlineData("verknuepfung.lnk ")]
    public void Programs_and_scripts_are_blocked(string name)
    {
        var e = Assert.Throws<ApiException>(() => AttachmentRules.CheckFile(name, [1, 2, 3]));
        Assert.Equal(400, e.Status);
    }

    [Fact]
    public void Empty_and_too_large_files_are_rejected_names_are_cleaned()
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => AttachmentRules.CheckFile("a.txt", [])).Status);
        Assert.Equal(413, Assert.Throws<ApiException>(() => AttachmentRules.CheckFile("a.txt", new byte[AttachmentRules.MaxFileBytes + 1])).Status);
        Assert.Equal("anhang", AttachmentRules.CheckFile("  ", [1]));
        Assert.Equal("Angebot Dach.pdf", AttachmentRules.CheckFile("Angebot Dach.pdf", [1]));
    }
}

[Collection("api")]
public sealed class AttachmentApiTests(AppFixture app)
{
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, int taskId, string name, byte[] data, string type = "application/octet-stream")
    {
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return await client.PostAsync($"/api/tasks/{taskId}/attachments?name={Uri.EscapeDataString(name)}", content);
    }

    /// <summary>Fehlermeldung aus der JSON-Antwort, im Rohtext stehen Umlaute als Escape-Folgen.</summary>
    private static async Task<string> Error(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString() ?? "";

    [Fact]
    public async Task Pasted_screenshot_is_stored_previewed_downloaded_and_removed()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var task = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Mangel Treppenhaus dokumentieren" }));

        var shot = await Read<AttachmentDto>(await Upload(thomas, task.Id, "Bildschirmfoto 2026-11-21 14-05-09.png", AttachmentRuleTests.Png, "image/png"));
        Assert.Equal("image/png", shot.ContentType);
        Assert.Equal(AttachmentRuleTests.Png.Length, shot.Size);

        // In der Aufgabe sichtbar, mit Eintrag im Verlauf
        var detail = JsonDocument.Parse(await thomas.GetStringAsync($"/api/tasks/{task.Id}")).RootElement;
        Assert.Contains(detail.GetProperty("task").GetProperty("attachments").EnumerateArray(), a => a.GetProperty("id").GetInt32() == shot.Id);
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() == "anhang");

        // Vorschau als Bild, Download weiterhin als Datei
        var preview = await thomas.GetAsync($"/api/tasks/{task.Id}/attachments/{shot.Id}/vorschau");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AttachmentRuleTests.Png, await preview.Content.ReadAsByteArrayAsync());
        var download = await thomas.GetAsync($"/api/tasks/{task.Id}/attachments/{shot.Id}");
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);

        // Entfernen löscht auch den Inhalt
        await Read<TaskDto>(await thomas.DeleteAsync($"/api/tasks/{task.Id}/attachments/{shot.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{task.Id}/attachments/{shot.Id}")).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.AttachmentBlobs.AnyAsync(b => b.AttachmentId == shot.Id)));
    }

    [Fact]
    public async Task Other_files_get_no_preview_and_programs_are_refused()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var task = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Angebot prüfen" }));

        // Ein „Bild“, das in Wahrheit HTML ist, bekommt keine Vorschau
        var fake = await Read<AttachmentDto>(await Upload(thomas, task.Id, "bild.png", Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "image/png"));
        Assert.Equal("application/octet-stream", fake.ContentType);
        Assert.Equal(HttpStatusCode.NotFound, (await thomas.GetAsync($"/api/tasks/{task.Id}/attachments/{fake.Id}/vorschau")).StatusCode);

        var exe = await Upload(thomas, task.Id, "rechnung.pdf.exe", [0x4D, 0x5A, 0x90, 0x00]);
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Contains("„.exe“", await Error(exe));

        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(thomas, task.Id, "leer.txt", [])).StatusCode);
        var tooLarge = await Upload(thomas, task.Id, "gross.bin", new byte[AttachmentRules.MaxFileBytes + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Contains("10 MB", await Error(tooLarge));
    }

    [Fact]
    public async Task Only_people_who_may_edit_can_add_and_the_count_is_limited()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var maria = await app.LoginAsync("dev-maria");
        var task = await Read<TaskDto>(await thomas.PostAsJsonAsync("/api/tasks", new { title = "Private Notiz" }));

        // Fremde private Aufgabe: weder hochladen noch löschen noch sehen
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(maria, task.Id, "a.txt", [1])).StatusCode);
        var mine = await Read<AttachmentDto>(await Upload(thomas, task.Id, "a.txt", [1], "text/plain"));
        Assert.Equal(HttpStatusCode.NotFound, (await maria.DeleteAsync($"/api/tasks/{task.Id}/attachments/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/tasks/{task.Id}/attachments/{mine.Id}")).StatusCode);

        for (var i = 1; i < AttachmentRules.MaxPerTask; i++)
        {
            await Read<AttachmentDto>(await Upload(thomas, task.Id, $"teil-{i}.txt", [1], "text/plain"));
        }
        var over = await Upload(thomas, task.Id, "zu-viel.txt", [1]);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains($"{AttachmentRules.MaxPerTask} Anhänge", await Error(over));
    }
}
