using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aufgaben.Api.Contracts;
using Aufgaben.Api.Endpoints;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Options;
using Aufgaben.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aufgaben.Tests;

public class AiSuggestionRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 24); // Dienstag
    private const int Me = 1;
    private static readonly AiPerson[] People =
    [
        new(Me, "Rujbin Nassereslam", "IT"),
        new(2, "Thomas Becker", "Bauleitung"),
        new(5, "Thomas Schmitt", "Technik"),
        new(7, "Maria Klein", "Vermietung"),
    ];
    private static readonly AiProject[] Projects = [new(11, "1001 Lindenpark"), new(12, "2002 Bergweg 3-7")];

    private static AiSuggestionDto Validate(string json) => TaskAssistant.Validate(json, Today, Me, People, Projects);

    [Fact]
    public void Valid_answer_becomes_drafts_with_real_people_and_projects()
    {
        var result = Validate("""
            {"aufgaben":[{"titel":"  Angebot   Dachdecker prüfen ","notizen":"PDF liegt bei.","faellig":"2026-11-27","uhrzeit":"10:30",
              "prioritaet":"hoch","wiedervorlage":false,"verantwortlich":2,"projekt":11,"checkliste":["Preise vergleichen","Preise vergleichen",""],
              "quelle":"bis Freitag das Angebot prüfen","fragen":[]}],"hinweis":null}
            """);
        var task = Assert.Single(result.Tasks);
        Assert.Equal("Angebot Dachdecker prüfen", task.Title);
        Assert.Equal("2026-11-27", task.DueDate);
        Assert.Equal("10:30", task.DueTime);
        Assert.Equal("hoch", task.Priority);
        Assert.Equal("Thomas Becker", task.Assignee?.Name);
        Assert.Equal("1001 Lindenpark", task.Project?.Label);
        Assert.Equal(["Preise vergleichen"], task.Checklist);
        Assert.Null(result.Note);
    }

    [Fact]
    public void Invented_people_projects_and_dates_are_dropped()
    {
        var result = Validate("""
            {"aufgaben":[
              {"titel":"A","faellig":"27.11.2026","uhrzeit":"10:30","prioritaet":"egal","verantwortlich":99,"projekt":404,"checkliste":[],"fragen":[]},
              {"titel":"B","faellig":"2031-01-01","verantwortlich":1,"prioritaet":"normal","checkliste":[],"fragen":[]},
              {"titel":"","verantwortlich":2},
              {"notizen":"ohne Titel"}],"hinweis":null}
            """);
        Assert.Equal(2, result.Tasks.Count);
        var a = result.Tasks[0];
        Assert.Null(a.DueDate);
        Assert.Null(a.DueTime); // keine Uhrzeit ohne Datum
        Assert.Null(a.Assignee);
        Assert.Null(a.Project);
        Assert.Equal("normal", a.Priority);
        var b = result.Tasks[1];
        Assert.Null(b.DueDate); // mehr als drei Jahre voraus
        Assert.Null(b.Assignee); // die eingebende Person selbst heißt: bei mir
    }

    [Fact]
    public void Questions_need_at_least_two_real_options()
    {
        var result = Validate("""
            {"aufgaben":[{"titel":"Schlüssel übergeben","verantwortlich":2,"prioritaet":"normal","checkliste":[],"fragen":[
              {"feld":"verantwortlich","frage":"Welcher Thomas?","optionen":[2,"5",99]},
              {"feld":"projekt","frage":"Welches Projekt?","optionen":["11"]},
              {"feld":"faellig","frage":"Welcher Freitag?","optionen":["2026-11-27","2026-12-04","kein Datum"]},
              {"feld":"prioritaet","frage":"Wie wichtig?","optionen":["hoch","normal"]}]}],"hinweis":null}
            """);
        var questions = Assert.Single(result.Tasks).Questions;
        Assert.Equal(2, questions.Count); // Projekt hat nur eine Option, Priorität ist kein erlaubtes Feld
        Assert.Equal(["Thomas Becker", "Thomas Schmitt"], questions[0].Options.Select(o => o.Label));
        Assert.Equal(["Fr 27.11.", "Fr 4.12."], questions[1].Options.Select(o => o.Label));
    }

    [Fact]
    public void Code_fences_are_tolerated_and_broken_answers_rejected()
    {
        var fenced = Validate("```json\n{\"aufgaben\":[{\"titel\":\"X\",\"prioritaet\":\"normal\"}],\"hinweis\":null}\n```");
        Assert.Equal("X", Assert.Single(fenced.Tasks).Title);
        Assert.Throws<AiException>(() => Validate("Hier sind deine Aufgaben: …"));
        Assert.Throws<AiException>(() => Validate("[1,2,3]"));
        var empty = Validate("""{"aufgaben":[],"hinweis":null}""");
        Assert.Equal("Im Text war keine Aufgabe zu erkennen.", empty.Note);
    }

    [Fact]
    public void At_most_ten_tasks()
    {
        var many = string.Join(",", Enumerable.Range(1, 15).Select(i => $$"""{"titel":"Aufgabe {{i}}","prioritaet":"normal"}"""));
        Assert.Equal(TaskAssistant.MaxTasks, Validate($$"""{"aufgaben":[{{many}}],"hinweis":null}""").Tasks.Count);
    }

    [Theory]
    [InlineData("zai-glm-latest", "auto", "max")]
    [InlineData("zai-glm-5-3", "max", "max")]
    [InlineData("zai-glm-latest", "high", "high")]
    [InlineData("mistral-large-latest", "auto", "high")]
    [InlineData("mistral-large-latest", "none", "none")]
    public void Reasoning_effort_like_in_the_project_cockpit(string model, string configured, string expected) =>
        Assert.Equal(expected, MistralClient.ReasoningEffort(model, configured));

    [Fact]
    public void Max_reasoning_only_for_glm() =>
        Assert.Throws<InvalidOperationException>(() => MistralClient.ReasoningEffort("mistral-large-latest", "max"));

    [Fact]
    public void Pasted_images_are_checked_by_their_bytes()
    {
        var png = $"data:image/png;base64,{Convert.ToBase64String(AttachmentRuleTests.Png)}";
        Assert.Equal("image/png", AiEndpoints.ParseImage(png).Type);
        // „PNG“, das in Wahrheit Text ist
        var fake = $"data:image/png;base64,{Convert.ToBase64String("<svg/>"u8.ToArray())}";
        Assert.Equal(400, Assert.Throws<ApiException>(() => AiEndpoints.ParseImage(fake)).Status);
        Assert.Equal(400, Assert.Throws<ApiException>(() => AiEndpoints.ParseImage("data:image/svg+xml;base64,PHN2Zy8+")).Status);
        var big = $"data:image/png;base64,{Convert.ToBase64String(new byte[AiEndpoints.MaxImageBytes + 1])}";
        Assert.Equal(400, Assert.Throws<ApiException>(() => AiEndpoints.ParseImage(big)).Status);
    }
}

public class MistralClientTests
{
    private sealed class FakeMistral(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = answers[Math.Min(_next++, answers.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static MistralClient Client(FakeMistral fake, MistralOptions? o = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AufgabenOptions
        {
            Mistral = o ?? new MistralOptions { Enabled = true, ApiKey = "test-schluessel" },
        });
        var http = new HttpClient(fake) { BaseAddress = new Uri("https://api.mistral.ai/") };
        return new MistralClient(http, options, NullLogger<MistralClient>.Instance) { Wait = (_, _) => Task.CompletedTask };
    }

    private const string Answer = """
        {"choices":[{"message":{"role":"assistant","content":[
          {"type":"thinking","thinking":[{"type":"text","text":"Ich überlege …"}]},
          {"type":"text","text":"{\"aufgaben\":[]}"}]}}]}
        """;

    [Fact]
    public async Task Sends_glm_with_json_mode_and_high_reasoning_and_ignores_thinking()
    {
        var fake = new FakeMistral((HttpStatusCode.OK, Answer));
        var text = await Client(fake).ChatJsonAsync("System", "Text", 2, CancellationToken.None);

        Assert.Equal("""{"aufgaben":[]}""", text);
        var (request, body) = Assert.Single(fake.Requests);
        Assert.Equal("https://api.mistral.ai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-schluessel", request.Headers.Authorization!.ToString());
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("zai-glm-latest", json.GetProperty("model").GetString());
        Assert.Equal("json_object", json.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("high", json.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task Retries_when_busy()
    {
        var fake = new FakeMistral((HttpStatusCode.TooManyRequests, "{}"), (HttpStatusCode.OK, Answer));
        await Client(fake).ChatJsonAsync("System", "Text", 2, CancellationToken.None);
        Assert.Equal(2, fake.Requests.Count);

        var stillBusy = new FakeMistral((HttpStatusCode.TooManyRequests, "{}"));
        var ex = await Assert.ThrowsAsync<AiException>(() => Client(stillBusy).ChatJsonAsync("System", "Text", 2, CancellationToken.None));
        Assert.Contains("ausgelastet", ex.Message);
        Assert.Equal(2, stillBusy.Requests.Count);
    }

    [Fact]
    public async Task Errors_never_contain_the_response_body()
    {
        var fake = new FakeMistral((HttpStatusCode.BadRequest, """{"message":"Mieterin Schmidt, Whg. 3.2, Kontonummer …"}"""));
        var ex = await Assert.ThrowsAsync<AiException>(() => Client(fake).ChatJsonAsync("System", "Text", 1, CancellationToken.None));
        Assert.DoesNotContain("Schmidt", ex.Message);
        Assert.Contains("HTTP 400", ex.Message);

        var wrongKey = new FakeMistral((HttpStatusCode.Unauthorized, "{}"));
        var keyError = await Assert.ThrowsAsync<AiException>(() => Client(wrongKey).ChatJsonAsync("System", "Text", 1, CancellationToken.None));
        Assert.Contains("Schlüssel", keyError.Message);
    }

    [Theory]
    [InlineData(null, "https://api.mistral.ai/")]
    [InlineData("", "https://api.mistral.ai/")]
    [InlineData("global", "https://api.mistral.ai/")]
    [InlineData("eu", "https://api.eu.mistral.ai/")]
    [InlineData(" EU ", "https://api.eu.mistral.ai/")]
    [InlineData("us", "https://api.us.mistral.ai/")]
    public void Region_selects_the_endpoint(string? region, string expected) =>
        Assert.Equal(expected, MistralClient.BaseAddress(region).ToString());

    [Fact]
    public void Unknown_region_is_reported_at_start() =>
        Assert.Contains("eu", Assert.Throws<InvalidOperationException>(() => MistralClient.BaseAddress("europa")).Message);

    [Fact]
    public async Task Model_missing_in_the_region_gets_a_clear_message_without_response_content()
    {
        var fake = new FakeMistral((HttpStatusCode.BadRequest, """{"object":"error","message":"Invalid model: zai-glm-latest","type":"invalid_model","code":"1500"}"""));
        var ex = await Assert.ThrowsAsync<AiException>(() => Client(fake).ChatJsonAsync("System", "Text", 1, CancellationToken.None));
        Assert.Contains("„zai-glm-latest“", ex.Message);
        Assert.Contains("api.mistral.ai", ex.Message);
        Assert.Contains("Aufgaben__Mistral__Region", ex.Message);

        // Andere Fehler bleiben allgemein, auch wenn im Text zufällig „model“ vorkommt
        var other = new FakeMistral((HttpStatusCode.BadRequest, """{"message":"Mieterin Schmidt fragt nach dem model","type":"invalid_request"}"""));
        var otherEx = await Assert.ThrowsAsync<AiException>(() => Client(other).ChatJsonAsync("System", "Text", 1, CancellationToken.None));
        Assert.Contains("HTTP 400", otherEx.Message);
        Assert.DoesNotContain("Schmidt", otherEx.Message);
    }

    [Fact]
    public async Task Nothing_is_sent_while_switched_off()
    {
        var fake = new FakeMistral((HttpStatusCode.OK, Answer));
        var off = Client(fake, new MistralOptions { Enabled = false, ApiKey = "test-schluessel" });
        Assert.False(off.IsConfigured);
        await Assert.ThrowsAsync<AiException>(() => off.ChatJsonAsync("System", "Text", 1, CancellationToken.None));
        Assert.False(Client(fake, new MistralOptions { Enabled = true }).IsConfigured);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Ocr_reads_the_markdown_of_all_pages()
    {
        var fake = new FakeMistral((HttpStatusCode.OK, """{"pages":[{"index":0,"markdown":"Hallo Rujbin,"},{"index":1,"markdown":"bitte bis Freitag …"}]}"""));
        var text = await Client(fake).OcrImageAsync(AttachmentRuleTests.Png, "image/png", CancellationToken.None);
        Assert.Equal("Hallo Rujbin,\r\nbitte bis Freitag …".ReplaceLineEndings(), text.ReplaceLineEndings());
        var body = JsonDocument.Parse(fake.Requests[0].Body).RootElement;
        Assert.Equal("mistral-ocr-4-0", body.GetProperty("model").GetString());
        Assert.StartsWith("data:image/png;base64,", body.GetProperty("document").GetProperty("image_url").GetString());
    }
}

[Collection("api")]
public sealed class AiApiTests(AppFixture app)
{
    [Fact]
    public async Task Without_a_key_the_app_offers_no_ai_and_sends_nothing()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var me = JsonDocument.Parse(await thomas.GetStringAsync("/api/me")).RootElement;
        Assert.False(me.GetProperty("aiAvailable").GetBoolean());

        var res = await thomas.PostAsJsonAsync("/api/ki/vorschlag", new { text = "Bitte bis Freitag das Angebot prüfen" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("nicht eingeschaltet", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Confirmed_suggestions_are_marked_in_the_history()
    {
        var thomas = await app.LoginAsync("dev-thomas");
        var res = await thomas.PostAsJsonAsync("/api/tasks", new { title = "Angebot Dachdecker prüfen", fromAi = true });
        var task = JsonSerializer.Deserialize<TaskDto>(await res.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        Assert.Equal("ki", task.Source);
        var events = JsonDocument.Parse(await thomas.GetStringAsync($"/api/tasks/{task.Id}")).RootElement.GetProperty("events");
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("kind").GetString() == "angelegt" && e.GetProperty("text").GetString() == "mit KI-Vorschlag");
    }
}
