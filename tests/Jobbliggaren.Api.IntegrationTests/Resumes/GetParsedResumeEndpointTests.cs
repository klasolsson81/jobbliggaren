using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Resumes.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B1b — HTTP wiring + fail-closed IDOR for the GetParsedResume staging read
// (GET /api/v1/resumes/parsed/{id}). The artifact is imported through the B1a endpoint, so
// these tests also prove the import → staging-read round-trip through real Postgres (incl.
// the field-encryption decrypt-on-read of the CV-PII content).
[Collection("Api")]
public class GetParsedResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // A scanner-valid personnummer (parity ImportResumeEndpointTests) placed in real DOCX body
    // text so the authoritative server-side scan finds it.
    private const string ValidPersonnummer = "811218-9876";

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            f, email: $"parsed-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _factory, email: $"parsed-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static MultipartFormDataContent PdfForm() =>
        FileForm(PdfBytes, "cv.pdf", "application/pdf");

    private static MultipartFormDataContent FileForm(
        byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        using var form = PdfForm();
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    [Fact]
    public async Task GET_parsed_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_parsed_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_GET_parsed_returns_200_with_the_artifact()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("id").GetString().ShouldBe(id);
        json.GetProperty("status").GetString().ShouldBe("PendingReview");
        json.GetProperty("sourceFileName").GetString().ShouldBe("cv.pdf");
        json.TryGetProperty("confidence", out _).ShouldBeTrue();
        // The encrypted Content shadow decrypted on read into a real object graph (a null
        // Content would have NRE'd the mapper → 500, not this 200): assert the structure.
        var content = json.GetProperty("content");
        content.GetProperty("contact").ValueKind.ShouldBe(JsonValueKind.Object);
        content.GetProperty("experiences").ValueKind.ShouldBe(JsonValueKind.Array);
        content.GetProperty("skills").ValueKind.ShouldBe(JsonValueKind.Array);

        // #1060 PR C: the artifact says WHY it is still an artifact. The 8-byte stub has no
        // text layer, so the real extractor fails and auto-promote left this row pending on
        // ParseNotConfident — production's own path, no seeding.
        json.GetProperty("blockReason").GetString().ShouldBe("ParseNotConfident");
    }

    [Fact]
    public async Task Import_then_GET_parsed_reports_the_SAME_reason_the_import_did_without_a_re_upload()
    {
        // This is #1060's third sub-requirement stated as an assertion. The issue's complaint is
        // that the reason "krävs en ny uppladdning för att ens få veta" — so the test is not
        // "the field is populated", it is "the field the GET returns EQUALS the one the upload
        // returned", proven on two independent responses to two different endpoints, with only
        // a read in between. If the read side ever grew a second predicate, this goes red.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var docx = CvDocxFixtures.BuildDocx("Anna Andersson", $"Personnummer: {ValidPersonnummer}");
        using var form = FileForm(docx, "cv.docx", DocxContentType);
        var import = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.OK);

        var importJson = await import.Content.ReadFromJsonAsync<JsonElement>(ct);
        importJson.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        var reasonAtUpload = importJson.GetProperty("blockReason").GetString();
        reasonAtUpload.ShouldBe("PersonnummerPresent");
        var id = importJson.GetProperty("parsedResumeId").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getJson = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        getJson.GetProperty("blockReason").GetString().ShouldBe(reasonAtUpload);
    }

    [Fact]
    public async Task GET_parsed_blockReason_carries_a_gate_token_and_never_the_text_that_tripped_it()
    {
        // The DTO's personnummer-egress contract in one assertion: the reason names WHICH gate
        // fired, never what it saw. The document below contains a real personnummer that the
        // server scanned and flagged, so if the field ever carried evidence instead of a token
        // — a matched value, a snippet, a count-bearing message — this row is where it would
        // surface, on the highest-priority PII rule in the product (CLAUDE.md §5).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var docx = CvDocxFixtures.BuildDocx("Anna Andersson", $"Personnummer: {ValidPersonnummer}");
        using var form = FileForm(docx, "cv.docx", DocxContentType);
        var import = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        var id = (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);
        var raw = await get.Content.ReadAsStringAsync(ct);

        var reason = JsonDocument.Parse(raw).RootElement.GetProperty("blockReason").GetString();
        reason.ShouldNotBeNull();
        reason.ShouldBe("PersonnummerPresent");
        // A closed token: one of the enum's members, nothing composed around it. The
        // assertion reads the set dynamically, so it needs no count and cannot go stale.
        Enum.GetNames<AutoPromoteBlockReason>().ShouldContain(reason);
        reason.ShouldNotContain(ValidPersonnummer);
        // And the digits themselves never reach this response at all — the two-layer guard on
        // Preamble/content is what keeps that true, and the reason token must not undo it.
        raw.ShouldNotContain(ValidPersonnummer);
        raw.ShouldNotContain(ValidPersonnummer.Replace("-", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GET_parsed_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/parsed/{idA}", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_incomplete_entry_then_GET_parsed_reports_IncompleteContent_from_the_DEK_warm_tier()
    {
        // The third reason, and the only one that makes the read path pay for itself. The two
        // other gates read plaintext columns and answer before any content is touched; this one
        // runs the whole Tier-2 chain on the READ side — compose the transport DTO from the
        // decrypted parse, scan it, and ask Resume.CreateFromParsed. Measured on this fixture:
        // confidence comes back Confident, so Tier 1 passed and Tier 2 is genuinely what
        // produced the answer.
        //
        // Producible by production, not seeded: an experience heading followed by a role line
        // with no employer line is what the segmenter yields for a CV that lists a title without
        // a company, and the canonical Resume rejects the entry (the mapper never drops it to
        // make it fit). This is the population D3(beta) is measuring; if the per-entry
        // decomposition lands, this fixture is where its behaviour change first shows.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var docx = CvDocxFixtures.BuildDocx(
            "Anna Andersson", "anna@example.com",
            "Erfarenhet", "Backend-utvecklare",
            "Utbildning", "Civilingenjör - KTH", "2015-2020",
            "Kompetenser", "C#, PostgreSQL");
        using var form = FileForm(docx, "cv.docx", DocxContentType);
        var import = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.OK);

        var importJson = await import.Content.ReadFromJsonAsync<JsonElement>(ct);
        importJson.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        importJson.GetProperty("blockReason").GetString().ShouldBe("IncompleteContent");
        var id = importJson.GetProperty("parsedResumeId").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getJson = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        getJson.GetProperty("blockReason").GetString().ShouldBe("IncompleteContent");
        // Tier 1 demonstrably did NOT answer this one.
        getJson.GetProperty("confidence").GetProperty("overall").GetString().ShouldBe("Confident");
        getJson.GetProperty("personnummer").GetProperty("found").GetBoolean().ShouldBeFalse();
    }

    private static async Task<JsonElement> MasterPersonalInfoAsync(
        HttpClient client, string resumeId, CancellationToken ct)
    {
        var get = await client.GetAsync($"/api/v1/resumes/{resumeId}", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await get.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("versions")[0].GetProperty("content").GetProperty("personalInfo");
    }

    [Fact]
    public async Task Import_for_an_account_without_a_name_promotes_and_the_canonical_review_misses_no_name()
    {
        // #1741 / ADR 0142 D7, the substance of #734 row 7. An account without a name is what the
        // passwordless consent step registers (CompleteLoginChallengeCommandHandler); before #1741
        // its every import stayed pending on IncompleteContent.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _factory, email: $"parsed-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // B3 grades the name only once e-mail and phone are present, so this CV carries both.
        using var form = FileForm(CvDocxFixtures.ConfidentSwedishDeveloperCv(), "cv.docx", DocxContentType);
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);

        import.StatusCode.ShouldBe(HttpStatusCode.Created);
        var importJson = await import.Content.ReadFromJsonAsync<JsonElement>(ct);
        importJson.GetProperty("outcome").GetString().ShouldBe("Promoted");
        var resumeId = importJson.GetProperty("resumeId").GetString()!;

        (await MasterPersonalInfoAsync(client, resumeId, ct))
            .GetProperty("fullName").ValueKind.ShouldBe(JsonValueKind.Null);

        var review = await client.GetAsync($"/api/v1/resumes/{resumeId}/review?profile=Ats", ct);
        review.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await review.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("verdicts").EnumerateArray()
            .Single(v => v.GetProperty("criterionId").GetString() == "B3")
            .GetProperty("verdict").GetString().ShouldBe("Pass");
    }

    [Fact]
    public async Task GET_parsed_is_SILENT_about_the_label_channel_and_never_clears_it()
    {
        // CTO-bind D1.2(3): the one accepted asymmetry between the two call sites, pinned so
        // that the next reader does not "fix" it by persisting the typed label.
        //
        // The gate's label input is the CV name from the UPLOAD FORM. It is a property of a
        // SUBMISSION, not of the artifact, and this read has no submission — so it evaluates
        // the generated default and the label channel is NOT ASSESSED here. The write path
        // blocks; the read path returns null. That is a silence, not a clearance, and the copy
        // rendered off this field is scoped to the file for exactly that reason.
        //
        // Why the obvious structural fix is refused: carrying the typed label forward would
        // persist user text known to carry a personnummer (§5/§12), and passing null instead
        // makes CreateFromParsed fail ValidateName with Resume.NameRequired, which this gate
        // reports as IncompleteContent — a silent gap turned into a loud lie.
        //
        // What actually closes the loop is the upload form, which now refuses the name at the
        // field and does not navigate (cv-upload-form.tsx). Both states below are produced by
        // production entry points; nothing is seeded.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var docx = CvDocxFixtures.BuildDocx(
            "Anna Andersson", "anna@example.com",
            "Erfarenhet", "Backend-utvecklare", "Beta AB", "2021-2024",
            "Utbildning", "Civilingenjör - KTH", "2015-2020",
            "Kompetenser", "C#, PostgreSQL");

        using var form = FileForm(docx, "cv.docx", DocxContentType);
        form.Add(new StringContent($"Mitt CV {ValidPersonnummer}"), "name");
        var import = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        import.StatusCode.ShouldBe(HttpStatusCode.OK);
        var importJson = await import.Content.ReadFromJsonAsync<JsonElement>(ct);
        importJson.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        // The WRITE path saw the typed label and refused it.
        importJson.GetProperty("blockReason").GetString().ShouldBe("PersonnummerPresent");
        // The FILE is clean — which is what makes this the label channel and not the parse.
        importJson.GetProperty("personnummer").GetProperty("found").GetBoolean().ShouldBeFalse();
        var id = importJson.GetProperty("parsedResumeId").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getJson = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        // The READ path has no such label, so it says nothing about the channel. DOCUMENTED
        // AND ACCEPTED: null here means "nothing in the file", never "this will save".
        getJson.GetProperty("blockReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
