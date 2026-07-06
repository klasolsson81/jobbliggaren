using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b CV-motor v2 PR-4 (#653, ADR 0093 §D8/§D2(e)) — HTTP wiring for the CANONICAL Resume review
// (GET /{id}/review) + the finding-status ledger (PUT /{id}/review/findings/{criterionId}/status)
// over the real import→promote→review round-trip on real Postgres + the real DEK pipeline. Proves
// auth, fail-loud ?profile= validation, owner-scoped IDOR, the actionability guard, and that a
// recorded decision surfaces on the next review. The deep engine/verdict logic + pnr-redaction stay
// covered by the F4-9 unit tests; this is the endpoint + persistence + overlay boundary.
[Collection("Api")]
public class ResumeReviewEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"review-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"review-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static object PromoteBody(string name = "Mitt CV") => new
    {
        name,
        content = new
        {
            personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Stockholm" },
            experiences = Array.Empty<object>(),
            educations = Array.Empty<object>(),
            skills = Array.Empty<object>(),
            summary = (string?)null,
        },
    };

    private static object MasterContentBody(string summary) => new
    {
        personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Göteborg" },
        experiences = Array.Empty<object>(),
        educations = Array.Empty<object>(),
        skills = Array.Empty<object>(),
        summary,
    };

    // A canonical content edit whose single experience description MIXES bullet markers
    // ("•" + "-") so B5 (geometry-free formatting consistency) → Warn on the canonical arm
    // (Fas 4b PR-6a, #655). The two digit-free bullets also make A1 (Mätbara resultat) Fail —
    // a SUBSTANTIVE, non-styleOnly finding used for the negative half of the gate.
    private static object MasterContentBodyWithMixedBullets() => new
    {
        personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = "070-123 45 67", location = "Göteborg" },
        experiences = new[]
        {
            new
            {
                company = "Acme AB",
                role = "Backend-utvecklare",
                startDate = "2021-01-01",
                endDate = (string?)null,
                description = "• Ledde teamet\n- Ansvarade för budget",
            },
        },
        educations = Array.Empty<object>(),
        skills = Array.Empty<object>(),
        summary = (string?)null,
    };

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    // import → promote → the canonical Resume id.
    private static async Task<string> PromoteAsync(HttpClient client, CancellationToken ct)
    {
        var parsedId = await ImportAsync(client, ct);
        var promote = await client.PostAsJsonAsync($"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetReviewAsync(HttpClient client, string resumeId, CancellationToken ct)
    {
        var get = await client.GetAsync($"/api/v1/resumes/{resumeId}/review?profile=Ats", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await get.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    // The first criterion in the review whose verdict is one of the given kinds.
    private static string CriterionWithVerdict(JsonElement review, params string[] verdicts)
    {
        foreach (var v in review.GetProperty("verdicts").EnumerateArray())
        {
            if (verdicts.Contains(v.GetProperty("verdict").GetString()))
                return v.GetProperty("criterionId").GetString()!;
        }

        throw new InvalidOperationException(
            $"No verdict in {{{string.Join(", ", verdicts)}}} found in the review.");
    }

    private static JsonElement VerdictById(JsonElement review, string criterionId) =>
        review.GetProperty("verdicts").EnumerateArray()
            .Single(v => v.GetProperty("criterionId").GetString() == criterionId);

    // ---- Auth + validation ----

    [Fact]
    public async Task GET_review_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/review?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_review_invalid_profile_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);
        var response = await _client.GetAsync($"/api/v1/resumes/{resumeId}/review?profile=Klingon", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_review_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/review?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_review_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await PromoteAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/{idA}/review?profile=Ats", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Review happy path ----

    [Fact]
    public async Task Promote_then_GET_review_returns_200_with_rubric_and_verdicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var review = await GetReviewAsync(_client, resumeId, ct);

        review.GetProperty("rubricVersion").GetString().ShouldNotBeNullOrEmpty();
        review.GetProperty("profile").GetString().ShouldBe("Ats");
        review.GetProperty("verdicts").GetArrayLength().ShouldBeGreaterThan(0);
        // B4 (Personnummer ej angivet): a canonical CV runs the pnr guard on every save → clean → Pass.
        VerdictById(review, "B4").GetProperty("verdict").GetString().ShouldBe("Pass");
        // B8 (Filnamn): the canonical arm has no source file → honestly NotAssessed in v1.
        VerdictById(review, "B8").GetProperty("verdict").GetString().ShouldBe("NotAssessed");
    }

    // ---- Finding-status ledger ----

    [Fact]
    public async Task PUT_status_on_actionable_finding_returns_204_and_GET_carries_userStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var review = await GetReviewAsync(_client, resumeId, ct);
        var criterionId = CriterionWithVerdict(review, "Fail", "Warn");

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/{criterionId}/status",
            new { status = "Resolved" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetReviewAsync(_client, resumeId, ct);
        VerdictById(after, criterionId).GetProperty("userStatus").GetString().ShouldBe("Resolved");
    }

    [Fact]
    public async Task PUT_status_writes_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var review = await GetReviewAsync(_client, resumeId, ct);
        var criterionId = CriterionWithVerdict(review, "Fail", "Warn");

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/{criterionId}/status",
            new { status = "Resolved" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // IAuditableCommand: a manual status change writes exactly one audit row for the resume.
        var entries = await ReadAuditEntriesAsync(Guid.Parse(resumeId), ct);
        entries.ShouldContain(e => e.EventType == "Resume.FindingStatusSet" && e.AggregateType == "Resume");
    }

    [Fact]
    public async Task PUT_status_bad_status_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/A1/status",
            new { status = "bogus" }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_status_on_non_actionable_finding_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var review = await GetReviewAsync(_client, resumeId, ct);
        // B4 is a clean Pass on a canonical CV — nothing to resolve.
        var criterionId = CriterionWithVerdict(review, "Pass", "NotAssessed");

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/{criterionId}/status",
            new { status = "Resolved" }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_status_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await PromoteAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var putB = await clientB.PutAsJsonAsync(
            $"/api/v1/resumes/{idA}/review/findings/A1/status", new { status = "Resolved" }, ct);

        putB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Overlay interaction with a content edit (fingerprint staleness semantics) ----

    [Fact]
    public async Task PUT_status_then_master_edit_GET_review_is_coherent()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var review = await GetReviewAsync(_client, resumeId, ct);
        var criterionId = CriterionWithVerdict(review, "Fail", "Warn");

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/{criterionId}/status",
            new { status = "Resolved" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A content edit stamps staleness on the Resolved row (transactionally).
        var edit = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/master", MasterContentBody("En uppdaterad sammanfattning."), ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Coherent (no 5xx): the row either surfaces stale (finding still present) or is silently
        // cleared (finding gone) — both are honest per the fingerprint semantics.
        var after = await GetReviewAsync(_client, resumeId, ct);
        var verdict = VerdictById(after, criterionId);
        var userStatus = verdict.GetProperty("userStatus");
        if (userStatus.ValueKind != JsonValueKind.Null)
        {
            userStatus.GetString().ShouldBe("Resolved");
            // Surfaced-because-still-present carries the staleness stamp.
            verdict.GetProperty("userStatusStaleAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        }
    }

    // ---- The fail-closed gap closure (Fas 4b PR-6a, #655): B5 is now assessable + Ignorable ----

    [Fact]
    public async Task PUT_master_mixed_bullets_makes_B5_Warn_and_Ignored_succeeds_and_persists()
    {
        // Closes PR-5's intentional fail-closed gap: before B5 (styleOnly) had an assessable rule,
        // Ignoring it returned FindingNotActionable (no finding to act on). Now a mixed-marker
        // description makes B5 Warn on the canonical arm, so Ignored is a genuine, reachable decision.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var edit = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/master", MasterContentBodyWithMixedBullets(), ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var review = await GetReviewAsync(_client, resumeId, ct);
        VerdictById(review, "B5").GetProperty("verdict").GetString().ShouldBe("Warn",
            "en beskrivning med blandade punktsymboler ska ge B5 = Warn (arm-oberoende).");

        // B5 is styleOnly → Ignored is allowed. This is the path that returned 400 before B5 had a rule.
        var ignore = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/B5/status", new { status = "Ignored" }, ct);
        ignore.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The recorded Ignored decision surfaces on the next review (the canonical recompute matches
        // the review the user saw — B5's arm-independence is what makes the fingerprint stay fresh).
        var after = await GetReviewAsync(_client, resumeId, ct);
        VerdictById(after, "B5").GetProperty("userStatus").GetString().ShouldBe("Ignored");
    }

    [Fact]
    public async Task PUT_Ignored_on_a_non_styleOnly_finding_still_returns_400_FindingNotIgnorable()
    {
        // The gate still holds: a SUBSTANTIVE (non-styleOnly) finding can never be silenced by Ignored.
        // A1 (Mätbara resultat, critical, NOT styleOnly) fails on the digit-free bullets above.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await PromoteAsync(_client, ct);

        var edit = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/master", MasterContentBodyWithMixedBullets(), ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var review = await GetReviewAsync(_client, resumeId, ct);
        VerdictById(review, "A1").GetProperty("verdict").GetString()
            .ShouldBeOneOf("Fail", "Warn"); // a genuine finding to attempt to ignore

        var ignore = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/A1/status", new { status = "Ignored" }, ct);
        ignore.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ignore.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe("Resume.FindingNotIgnorable",
            "endast stilregler (styleOnly) går att ignorera — A1 är en substansregel.");
    }

    private async Task<List<AuditLogEntry>> ReadAuditEntriesAsync(Guid aggregateId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .Where(a => a.AggregateId == aggregateId)
            .ToListAsync(ct);
    }
}
