using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Persistence.Migrations;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// #552 stale-match purge migration (<see cref="PurgeStaleGradeGateMatches"/>) against REAL
/// Postgres (Testcontainers) — pins the migration's exact DELETE predicate by running its SPOT
/// SQL constant (<see cref="PurgeStaleGradeGateMatches.PurgeSql"/>, InternalsVisibleTo; repo
/// precedent <c>NullResumeVersionLegacyContent.NullOutSql</c> /
/// <c>C2SearchParityReverseLookupAndRecentExpansion.BuildReverseLookupSql</c> — never a
/// hand-copied twin that could drift out of sync) against four seeded rows covering the
/// predicate's full branch matrix.
///
/// <para>
/// <b>This is a predicate unit test, not an end-to-end <c>BackgroundMatchingJob</c> replay.</b>
/// Rows are seeded directly via <see cref="UserJobAdMatch.Create"/> with a chosen grade — the
/// suite does not run the real scorer, because the migration's SQL operates purely over
/// <c>job_ads</c> facet columns + <c>job_seekers.match_preferences</c> jsonb + the match row's
/// (user_id, job_ad_id) pair, independent of how that combination was scored at persist-time.
/// </para>
///
/// <para>
/// <b>Assertions are scoped to each seeded match's OWN id — never a global table count.</b> This
/// migration's DELETE is a global, unscoped scan across the whole <c>user_job_ad_matches</c>
/// table (same class of whole-table-replay risk as <c>C2ReverseLookupMigrationTests</c>, which
/// documents why: the shared <c>[Collection("Api")]</c> Postgres container can carry residual
/// rows left behind by sibling suites that do not clean up after themselves, e.g.
/// <c>MyMatchesSurfaceTests</c>). "Did MY specific row survive/die" is immune to whatever else
/// the same DELETE swept up; a global count is not.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class PurgeStaleGradeGateMatchesMigrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static FixedClock ClockAt(DateTimeOffset at) => new(at);

    private (AppDbContext Db, IServiceScope Scope) NewScope()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (db, scope);
    }

    // Seeds an imported JobAd with EXACTLY the facets the test cares about (TestFacets.From —
    // everything unnamed stays null, mirroring an ad whose payload never carried that key).
    private static async Task<JobAdId> SeedJobAdAsync(
        AppDbContext db, JobAdFacets facets, string label, CancellationToken ct)
    {
        var externalId = $"purge552-{Guid.NewGuid():N}";
        var jobAd = JobAd.Import(
            title: $"Annons {label}",
            company: Company.Create($"{label} AB").Value,
            description: "beskrivning",
            url: $"https://example.com/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: facets,
            declaredContacts: [],
            publishedAt: T0,
            expiresAt: T0.AddDays(30),
            clock: ClockAt(T0)).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Seeds a JobSeeker (a fresh random UserId) carrying exactly the stated MatchPreferences.
    private static async Task<Guid> SeedJobSeekerAsync(
        AppDbContext db, MatchPreferences preferences, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Purge552 Test", ClockAt(T0)).Value;
        seeker.UpdateMatchPreferences(preferences, ClockAt(T0));
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // Seeds a persisted (Good-graded — the grade is irrelevant to the purge predicate, but every
    // real persisted row is by construction >= Good, never Basic; see BackgroundMatchingJob.
    // ToNotifiable) UserJobAdMatch and returns its id for the survival/absence assertion.
    private static async Task<Guid> SeedMatchAsync(
        AppDbContext db, Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        var match = UserJobAdMatch.Create(
            userId, jobAdId, NotifiableMatchGrade.Good, [], ClockAt(T0.AddDays(1))).Value;
        db.UserJobAdMatches.Add(match);
        await db.SaveChangesAsync(ct);
        return match.Id.Value;
    }

    private static async Task<bool> MatchRowExistsAsync(
        AppDbContext db, Guid matchId, CancellationToken ct) =>
        await db.UserJobAdMatches.AnyAsync(m => m.Id == new UserJobAdMatchId(matchId), ct);

    [Fact]
    public async Task Purge_DeletesOrtAndEmploymentStaleRows_SurvivesFullMatchAndVacuousRows_AndIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, scope) = NewScope();
        using (scope)
        {
            // (a) ORT-branch stale row — MUST be deleted. The ad states NEITHER ort value
            // (region AND municipality both NULL); the user states an ort preference. Post-#552
            // this pair is gate-floored to Basic (MatchScorer.ScoreOrtUnion) and can never
            // re-persist.
            var staleOrtAdId = await SeedJobAdAsync(db, TestFacets.From(), "stale-ort", ct);
            var staleOrtUserId = await SeedJobSeekerAsync(
                db,
                MatchPreferences.Create(
                    preferredOccupationGroups: [],
                    preferredRegions: ["reg-stale"],
                    preferredEmploymentTypes: []).Value,
                ct);
            var staleOrtMatchId = await SeedMatchAsync(db, staleOrtUserId, staleOrtAdId, ct);

            // (b) SURVIVOR — a genuine containment/union hit on ort AND a genuine employment
            // match. Neither purge branch's ad-side NULL condition holds, so this row is
            // never touched.
            var survivorAdId = await SeedJobAdAsync(
                db, TestFacets.From(region: "reg-ok", employmentType: "emp-ok"), "survivor-full", ct);
            var survivorUserId = await SeedJobSeekerAsync(
                db,
                MatchPreferences.Create(
                    preferredOccupationGroups: [],
                    preferredRegions: ["reg-ok"],
                    preferredEmploymentTypes: ["emp-ok"]).Value,
                ct);
            var survivorMatchId = await SeedMatchAsync(db, survivorUserId, survivorAdId, ct);

            // (c) VACUOUS-DOCTRINE SURVIVOR — the ad states NEITHER ort nor employment value
            // (both branches' ad-side condition holds), but the user states NO preference at
            // all (MatchPreferences.Empty). Neither branch's "user states X" half is true, so
            // an unstated preference is never penalised.
            var vacuousAdId = await SeedJobAdAsync(db, TestFacets.From(), "vacuous", ct);
            var vacuousUserId = await SeedJobSeekerAsync(db, MatchPreferences.Empty, ct);
            var vacuousMatchId = await SeedMatchAsync(db, vacuousUserId, vacuousAdId, ct);

            // (d) EMPLOYMENT-branch stale row, isolated from the ort branch — MUST be deleted.
            // The ad HAS a matching region (branch 1's ad-side condition is false) but states no
            // employment value; the user states an employment preference. Proves the two
            // branches combine with OR independently.
            var staleEmploymentAdId = await SeedJobAdAsync(
                db, TestFacets.From(region: "reg-emp-iso"), "stale-employment", ct);
            var staleEmploymentUserId = await SeedJobSeekerAsync(
                db,
                MatchPreferences.Create(
                    preferredOccupationGroups: [],
                    preferredRegions: ["reg-emp-iso"],
                    preferredEmploymentTypes: ["emp-stale"]).Value,
                ct);
            var staleEmploymentMatchId =
                await SeedMatchAsync(db, staleEmploymentUserId, staleEmploymentAdId, ct);

            // Act — run the migration's EXACT SQL (SPOT constant, no hand-copied twin).
            await db.Database.ExecuteSqlRawAsync(PurgeStaleGradeGateMatches.PurgeSql, ct);

            // Assert — scoped to each seeded match's OWN id (see class doc: never a global count).
            (await MatchRowExistsAsync(db, staleOrtMatchId, ct)).ShouldBeFalse(
                "a stated ort preference against a both-NULL-ort ad is gate-floored to Basic " +
                "post-#552 and must be purged");
            (await MatchRowExistsAsync(db, survivorMatchId, ct)).ShouldBeTrue(
                "a genuine ort+employment match must never be purged");
            (await MatchRowExistsAsync(db, vacuousMatchId, ct)).ShouldBeTrue(
                "vacuous-gate doctrine: a user with NO stated preference is never penalised, " +
                "even against a both-NULL-ort ad");
            (await MatchRowExistsAsync(db, staleEmploymentMatchId, ct)).ShouldBeFalse(
                "a stated employment preference against a NULL-employment ad is gate-floored " +
                "to Basic post-#552 and must be purged, independent of the ort branch");

            // Idempotency (requirement: a re-run deletes 0 additional rows). Re-running against
            // the now-purged state must not disturb either survivor and must not throw.
            await db.Database.ExecuteSqlRawAsync(PurgeStaleGradeGateMatches.PurgeSql, ct);
            (await MatchRowExistsAsync(db, survivorMatchId, ct)).ShouldBeTrue(
                "an idempotent re-run must not disturb a survivor");
            (await MatchRowExistsAsync(db, vacuousMatchId, ct)).ShouldBeTrue(
                "an idempotent re-run must not disturb the vacuous-doctrine survivor");
        }
    }
}
