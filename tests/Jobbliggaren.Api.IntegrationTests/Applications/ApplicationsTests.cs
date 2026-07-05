using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

[Collection("Api")]
public class ApplicationsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    [Fact]
    public async Task GET_applications_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/applications", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_applications_with_auth_returns_200_with_paged_result()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/applications", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        json.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetProperty("totalCount").ValueKind.ShouldBe(JsonValueKind.Number);
        json.GetProperty("page").GetInt32().ShouldBe(1);
        json.GetProperty("pageSize").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task POST_application_returns_201_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = json.GetProperty("id").GetString();
        id.ShouldNotBeNullOrEmpty();
        Guid.Parse(id!).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_then_GET_returns_application_with_draft_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var listResponse = await _client.GetAsync("/api/v1/applications", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        listJson.ValueKind.ShouldBe(JsonValueKind.Object);
        var items = listJson.GetProperty("items");
        items.ValueKind.ShouldBe(JsonValueKind.Array);

        var found = items.EnumerateArray().Any(a =>
            a.GetProperty("id").GetString() == id &&
            a.GetProperty("status").GetString() == "Draft");
        found.ShouldBeTrue();
    }

    [Fact]
    public async Task GET_application_by_id_returns_200_with_detail_dto()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync(
            "/api/v1/applications",
            new { jobAdId = (Guid?)null, coverLetter = "Mitt brev." },
            ct);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var getResponse = await _client.GetAsync($"/api/v1/applications/{id}", ct);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        getJson.GetProperty("id").GetString().ShouldBe(id);
        getJson.GetProperty("status").GetString().ShouldBe("Draft");
        getJson.GetProperty("coverLetter").GetString().ShouldBe("Mitt brev.");
        getJson.GetProperty("followUps").ValueKind.ShouldBe(JsonValueKind.Array);
        getJson.GetProperty("notes").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_application_by_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync($"/api/v1/applications/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_from_job_ad_with_unknown_jobad_returns_404()
    {
        // TD-84 / #203 — CreateApplicationFromJobAdCommandHandler returnerar
        // DomainError.NotFound("JobAd", …) när annonsen saknas (eller är
        // soft-deletad). Endpoint mappar via DomainError.ToProblemResult()
        // som ger 404 på ".NotFound"-koden. Före TD-84 hårdkodades 400 här —
        // detta test bevakar den nya REST-korrekta 404:an (denna helper, inte
        // NotFoundException-middleware, producerar statuskoden).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsync(
            $"/api/v1/applications/from-job-ad/{Guid.NewGuid()}", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_pipeline_with_auth_returns_200_with_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/applications/pipeline", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task POST_then_pipeline_groups_application_under_draft_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);

        var pipelineResponse = await _client.GetAsync("/api/v1/applications/pipeline", ct);
        pipelineResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pipelineJson = await pipelineResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var draftGroup = pipelineJson.EnumerateArray()
            .FirstOrDefault(g => g.GetProperty("status").GetString() == "Draft");
        draftGroup.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        draftGroup.GetProperty("count").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        // #343 (ADR 0085 §3, CTO Option a): attentionSignal is projected end-to-end and
        // MUST serialize as the enum NAME, not the ordinal — [JsonStringEnumConverter] on
        // ApplicationAttentionSignal. A bare draft (no linked ad, no deadline) → "None".
        // A raw int 0 here would mean the converter is missing and the FE z.enum parse
        // would silently break — this assertion is the STOPP-class guard for that.
        var draftApp = draftGroup.GetProperty("applications").EnumerateArray().First();
        var signal = draftApp.GetProperty("attentionSignal");
        signal.ValueKind.ShouldBe(JsonValueKind.String);
        signal.GetString().ShouldBe("None");
    }

    [Fact]
    public async Task POST_transition_to_submitted_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var transitionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition",
            new { targetStatus = "Submitted" },
            ct);

        transitionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_transition_updates_status_on_get()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition",
            new { targetStatus = "Submitted" },
            ct);

        var getResponse = await _client.GetAsync($"/api/v1/applications/{id}", ct);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        getJson.GetProperty("status").GetString().ShouldBe("Submitted");
    }

    [Fact]
    public async Task POST_transition_skip_to_accepted_returns_200()
    {
        // ADR 0092 D3 (fria övergångar): Draft → Accepted var tidigare 400
        // (Application.InvalidTransition). Ett fritt hopp lyckas nu → 200.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var transitionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition",
            new { targetStatus = "Accepted" },
            ct);

        transitionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_transition_to_ghosted_returns_200()
    {
        // ADR 0092 D3: manuell Ghosted är nu ett giltigt mål (validatorns regel
        // "Ghosted sätts automatiskt av systemet" är borttagen).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var transitionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition",
            new { targetStatus = "Ghosted" },
            ct);

        transitionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_transition_with_unknown_status_returns_400()
    {
        // Validatorn avvisar fortfarande okända statusnamn — den enda kvarvarande
        // 400-vägen på transition-endpointen efter att övergångarna blev fria.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var transitionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition",
            new { targetStatus = "NotAStatus" },
            ct);

        transitionResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_follow_up_returns_201_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var followUpResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/follow-ups",
            new
            {
                channel = "Email",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                note = "Följ upp på ansökan."
            },
            ct);

        followUpResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var followUpJson = await followUpResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUpId = followUpJson.GetProperty("id").GetString();
        followUpId.ShouldNotBeNullOrEmpty();
        Guid.Parse(followUpId!).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_follow_up_then_get_by_id_shows_follow_up_in_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/follow-ups",
            new
            {
                channel = "LinkedIn",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(5).ToString("O"),
                note = (string?)null
            },
            ct);

        var getResponse = await _client.GetAsync($"/api/v1/applications/{id}", ct);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUps = getJson.GetProperty("followUps");
        followUps.ValueKind.ShouldBe(JsonValueKind.Array);
        followUps.GetArrayLength().ShouldBe(1);
        followUps[0].GetProperty("channel").GetString().ShouldBe("LinkedIn");
    }

    [Fact]
    public async Task POST_note_returns_201_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        var noteResponse = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/notes",
            new { content = "Intervjun gick bra." },
            ct);

        noteResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var noteJson = await noteResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var noteId = noteJson.GetProperty("id").GetString();
        noteId.ShouldNotBeNullOrEmpty();
        Guid.Parse(noteId!).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_note_then_get_by_id_shows_note_in_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = postJson.GetProperty("id").GetString()!;

        await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/notes",
            new { content = "Kul bolag." },
            ct);

        var getResponse = await _client.GetAsync($"/api/v1/applications/{id}", ct);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var notes = getJson.GetProperty("notes");
        notes.ValueKind.ShouldBe(JsonValueKind.Array);
        notes.GetArrayLength().ShouldBe(1);
        notes[0].GetProperty("content").GetString().ShouldBe("Kul bolag.");
    }

    [Fact]
    public async Task POST_create_two_applications_different_statuses_pipeline_groups_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post1 = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var id1 = (await post1.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var post2 = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var id2 = (await post2.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        await _client.PostAsJsonAsync(
            $"/api/v1/applications/{id2}/transition",
            new { targetStatus = "Submitted" },
            ct);

        var pipelineResponse = await _client.GetAsync("/api/v1/applications/pipeline", ct);
        pipelineResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pipelineJson = await pipelineResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var groups = pipelineJson.EnumerateArray().ToList();
        groups.ShouldContain(g => g.GetProperty("status").GetString() == "Draft");
        groups.ShouldContain(g => g.GetProperty("status").GetString() == "Submitted");
    }
}
