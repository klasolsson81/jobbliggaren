using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0079 STEG 5 — endpoint-bindning + validering för grad-filtret (?matchGrades=).
// Verifierar att minimal-API binder MatchGrade[] från en UPPREPAD query-param per
// NAMN (Basic/Good/Strong) och att den strukturella ärlighets-grinden håller hela
// vägen via HTTP: Topp avvisas wire-side (Fast-bandet kan inte beräkna must-have-
// täckning i SQL — G3-OPT-A). Den FAKTISKA grad-filtreringen (grad-WHERE ≡
// Grade(Fast), count, frikoppling) pinnas i MatchSortGradeFilterOracleTests; här
// är scopet bindning + 400-grindar.
[Collection("Api")]
public class ListJobAdsMatchGradeFilterEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("Good")]
    [InlineData("Strong")]
    public async Task GET_job_ads_with_single_fast_band_grade_binds_and_returns_200(string grade)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            $"/api/v1/job-ads?matchGrades={grade}&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_with_repeated_match_grades_binds_to_array_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Upprepad query-param → MatchGrade[] { Good, Strong } (samma kontrakt som
        // ?employmentType=/?region=). En ny användare saknar angiven yrkesgrupp →
        // handlern honest-fallbackar (case 2), men bindningen ska ändå lyckas (200).
        var response = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=Good&matchGrades=Strong&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_with_Top_grade_is_rejected_400()
    {
        // Den strukturella ärlighets-grinden via HTTP: Topp är inte Fast-beräkningsbar
        // (G3-OPT-A) → ListJobAdsQueryValidator avvisar den → rent 400.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=Top&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_unparseable_grade_name_returns_400()
    {
        // Ogiltigt enum-namn → minimal-API-bindningen misslyckas → 400 (till skillnad
        // mot en OBUNDEN param som ?ssyk=, som ignoreras; matchGrades ÄR bunden).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=notagrade&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_without_match_grades_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_with_onlyMatched_binds_and_returns_200()
    {
        // #419 pt1 (CTO Approach A) — "Visa bara matchade": ?onlyMatched=true binds to the
        // bool query-param (ASP.NET takes "true", not "1"). A new user has no stated
        // occupation → the handler honest-fallbacks (no profile gate) but the binding must
        // still succeed (200). The actual rank > 0 gating is oracle-pinned via the
        // {Basic,Related,Good,Strong} subset in MatchSortGradeFilterOracleTests.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?onlyMatched=true&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_with_onlyMatched_and_match_grades_returns_200()
    {
        // OnlyMatched composes freely with an explicit grade subset (the subset wins server-
        // side; no validator mutex). Binding both must succeed.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?onlyMatched=true&matchGrades=Strong&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
