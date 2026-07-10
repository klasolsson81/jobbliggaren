using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;
using Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the /jobb user-read watermark surface
/// (GET /me/jobs/watermark + POST /me/jobs/seen) over the wired HTTP stack against REAL
/// Postgres (Testcontainers). Asserts the auth gate, the JSON contract the FE reads
/// (camelCase <c>lastSeenJobsAt</c>), and the watermark ROUND-TRIP (mark persists → the read
/// reflects it). The watermark is a first-class nullable column on <c>job_seekers</c>, the
/// sibling of <c>last_seen_matches_at</c>. Each test registers a fresh user so rows never
/// collide across the shared [Collection("Api")] schema.
/// </summary>
[Collection("Api")]
public sealed class JobsWatermarkSurfaceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The deterministic seed clock (the seeker is registered relative to this instant).
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<HttpClient> AuthedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private (AppDbContext Db, IServiceScope Scope) NewScope()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (db, scope);
    }

    private static ICurrentUser UserWith(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    // A clock pinned to a chosen instant (SetLastSeenJobs stamps the watermark = clock.UtcNow).
    private static IDateTimeProvider ClockAt(DateTimeOffset at)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(at);
        return clock;
    }

    // Seeds a JobSeeker for the user (never-visited → null watermark) against REAL Postgres.
    private static async Task SeedSeekerAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Watermark User", ClockAt(T0)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task GET_jobs_watermark_authed_freshUser_returns_200_with_null_watermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthedClientAsync(ct);

        var response = await client.GetAsync("/api/v1/me/jobs/watermark", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("lastSeenJobsAt", out var prop).ShouldBeTrue(
            "the response carries a camelCase `lastSeenJobsAt` field — the contract the FE NY-tag reads");
        // A freshly-registered user has never visited /jobb → null watermark → FE shows no NY.
        prop.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_jobs_seen_authed_returns_204_then_watermark_is_nonNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthedClientAsync(ct);

        // The registered user already has a JobSeeker (RegisterCommandHandler auto-provisions
        // one) → MarkJobsSeen succeeds with 204 (not NotFound/400).
        var seen = await client.PostAsync("/api/v1/me/jobs/seen", content: null, ct);
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The watermark round-trips through Postgres → the read now reflects a non-null instant.
        var watermark = await client.GetAsync("/api/v1/me/jobs/watermark", ct);
        watermark.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await watermark.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("lastSeenJobsAt").ValueKind.ShouldBe(JsonValueKind.String,
            "after MarkJobsSeen the persisted watermark is a concrete timestamp");
    }

    // #759 (sibling of #477 Low 4) — POST /me/jobs/seen with a body { seenThrough } sets the
    // watermark to THAT window (the max CreatedAt the FE rendered), NOT wall-clock-now. Proven at
    // the HTTP layer: a seenThrough in the past round-trips verbatim (it is older than now, so the
    // aggregate does not clamp it), so an ad ingested after it stays flagged "Ny".
    [Fact]
    public async Task POST_jobs_seen_withSeenThroughBody_setsWatermarkToThatWindow_notClockNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthedClientAsync(ct);

        // A deterministic instant clearly in the past (older than the wall-clock the handler reads
        // as "now"), so it is neither clamped to now nor swallowed — it must persist verbatim.
        var seenThrough = new DateTimeOffset(2026, 2, 3, 10, 15, 0, TimeSpan.Zero);

        var seen = await client.PostAsJsonAsync(
            "/api/v1/me/jobs/seen", new { seenThrough }, ct);
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var watermark = await client.GetAsync("/api/v1/me/jobs/watermark", ct);
        watermark.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await watermark.Content.ReadFromJsonAsync<JsonElement>(ct);
        var persisted = json.GetProperty("lastSeenJobsAt").GetDateTimeOffset();
        persisted.ShouldBe(seenThrough,
            "the watermark is the seen window the FE sent, not wall-clock-now (#759)");
    }

    [Fact]
    public async Task POST_jobs_seen_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await client.PostAsync("/api/v1/me/jobs/seen", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_jobs_watermark_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me/jobs/watermark", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ===============================================================
    // Handler-level — the MONOTONIC ADVANCE + NON-REWIND contract through REAL Postgres.
    // The endpoint smoke tests above prove null→non-null once over wall-clock; these prove the
    // ACTUAL point of a "last-seen" watermark with a CONTROLLED clock: a later visit moves it
    // FORWARD and a stale call NEVER rewinds it — both persisted to the timestamptz column and
    // re-read across a FRESH scope (so the value provably came from the DB, not the tracker).
    // Parity with MyMatchesSurfaceTests' watermark round-trip (handler-from-DI + ClockAt + fresh
    // scope), mirroring SetLastSeenMatches' integration coverage.
    // ===============================================================

    [Fact]
    public async Task MarkJobsSeen_AdvancesWatermarkForward_OnASecondVisit_RoundTrippedThroughPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var (seedDb, seedScope) = NewScope();
        using (seedScope)
            await SeedSeekerAsync(seedDb, userId, ct);

        // First visit at day 5 → null → day 5 (the baseline established by the visit itself).
        var (db1, scope1) = NewScope();
        using (scope1)
        {
            var mark = new MarkJobsSeenCommandHandler(db1, UserWith(userId), ClockAt(T0.AddDays(5)));
            (await mark.Handle(new MarkJobsSeenCommand(null), ct)).IsSuccess.ShouldBeTrue();
            await db1.SaveChangesAsync(ct);
        }

        var (readDb1, readScope1) = NewScope();
        using (readScope1)
        {
            var read = new GetJobsWatermarkQueryHandler(readDb1, UserWith(userId));
            var wm = await read.Handle(new GetJobsWatermarkQuery(), ct);
            wm.LastSeenJobsAt.ShouldBe(T0.AddDays(5), "the first visit establishes the baseline");
        }

        // Second visit at day 9 → advances the watermark FORWARD (the next /jobb NY set then
        // flags only ads ingested after day 9).
        var (db2, scope2) = NewScope();
        using (scope2)
        {
            var mark = new MarkJobsSeenCommandHandler(db2, UserWith(userId), ClockAt(T0.AddDays(9)));
            (await mark.Handle(new MarkJobsSeenCommand(null), ct)).IsSuccess.ShouldBeTrue();
            await db2.SaveChangesAsync(ct);
        }

        var (readDb2, readScope2) = NewScope();
        using (readScope2)
        {
            var read = new GetJobsWatermarkQueryHandler(readDb2, UserWith(userId));
            var wm = await read.Handle(new GetJobsWatermarkQuery(), ct);
            wm.LastSeenJobsAt.ShouldBe(T0.AddDays(9),
                "a later visit advances the persisted watermark forward");
        }
    }

    [Fact]
    public async Task MarkJobsSeen_NeverRewindsWatermark_WhenAStaleCallArrives_RoundTrippedThroughPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var (seedDb, seedScope) = NewScope();
        using (seedScope)
            await SeedSeekerAsync(seedDb, userId, ct);

        // Establish the watermark at day 9.
        var (db1, scope1) = NewScope();
        using (scope1)
        {
            var mark = new MarkJobsSeenCommandHandler(db1, UserWith(userId), ClockAt(T0.AddDays(9)));
            (await mark.Handle(new MarkJobsSeenCommand(null), ct)).IsSuccess.ShouldBeTrue();
            await db1.SaveChangesAsync(ct);
        }

        // A STALE call at day 5 (an out-of-order load) succeeds but is a monotonic NO-OP — the
        // aggregate guard refuses to rewind; the persisted column stays at day 9.
        var (db2, scope2) = NewScope();
        using (scope2)
        {
            var mark = new MarkJobsSeenCommandHandler(db2, UserWith(userId), ClockAt(T0.AddDays(5)));
            (await mark.Handle(new MarkJobsSeenCommand(null), ct)).IsSuccess.ShouldBeTrue(
                "a stale call is idempotent — still Success, never an error");
            await db2.SaveChangesAsync(ct);
        }

        var (readDb, readScope) = NewScope();
        using (readScope)
        {
            var read = new GetJobsWatermarkQueryHandler(readDb, UserWith(userId));
            var wm = await read.Handle(new GetJobsWatermarkQuery(), ct);
            wm.LastSeenJobsAt.ShouldBe(T0.AddDays(9),
                "the monotonic guard holds through Postgres — a stale call never rewinds the watermark");
        }
    }
}
