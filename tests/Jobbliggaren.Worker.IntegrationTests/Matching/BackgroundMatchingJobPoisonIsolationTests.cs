using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// #751 (audit w1-bgmatch-shared-context) — the poison-ISOLATION acceptance test for
/// <see cref="BackgroundMatchingJob"/> against REAL Postgres. It proves the load-bearing claim the
/// per-user child scope exists for: one user whose atomic <c>SaveChangesAsync</c> FAILS must not
/// break any OTHER user's scan.
///
/// <para>
/// <b>Why a DB trigger, not a throwing collaborator.</b> The unit isolation tests
/// (<c>BackgroundMatchingJobTests</c>) already cover a collaborator that THROWS — but a throw
/// happens BEFORE <c>db.UserJobAdMatches.Add(...)</c>, so it leaves only tracked-Unchanged state,
/// which was harmless even on the OLD shared-context code. GENUINE poison needs the per-user atomic
/// <c>SaveChangesAsync</c> to fail AFTER <c>Add</c> + <c>AdvanceMatchScan</c>, leaving poisoned
/// Added/Modified entities in the change tracker. <c>user_job_ad_matches</c> has NO FK to
/// <c>job_ads</c> (by-identity, ADR 0058/0059), so a match row whose <c>job_ad_id</c> points at a
/// never-seeded ad inserts cleanly at the DB level — there is nothing to make the INSERT fail on its
/// own. So the failure is induced deterministically by a sentinel-keyed plpgsql BEFORE INSERT
/// trigger on <c>user_job_ad_matches</c> that raises whenever <c>job_ad_id</c> equals a sentinel
/// GUID.
/// </para>
///
/// <para>
/// <b>The counterfactual (built in).</b> The profile-builder substitute makes the FIRST of the pair
/// the arbitrary due-set order scans the POISONED one, so the CLEAN user is always scanned AFTER it.
/// On the current (fixed) code every user owns its own child scope, so the poisoned user's tracker
/// dies with its scope and the clean user's own atomic commit succeeds. On the OLD shared-context
/// code the poisoned user's Added sentinel row survives in the single shared tracker; the clean
/// user's <c>SaveChangesAsync</c> re-flushes it → the trigger fires again → the clean user fails too
/// → its assertions (one row + advanced watermark) go RED. That is the regression this test pins.
/// </para>
///
/// <para>
/// The Postgres container is SHARED across the serial <c>[Collection("Worker")]</c>, so the trigger
/// + function are named with the per-test <c>_run</c> suffix and DROPped in a <c>finally</c> — a
/// leaked trigger would fail unrelated later tests in the collection.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class BackgroundMatchingJobPoisonIsolationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // Per-test-unique run id (xUnit news a fresh instance per [Fact]). Used for the sentinel
    // trigger/function names (the shared container demands uniqueness) and for the ad/seeker shadows
    // (parity BackgroundMatchingJobIntegrationTests — harmless here since the scorer is substituted).
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];
    private string OccupationGroup => $"grp-{_run}";
    private string Region => $"reg-{_run}";
    private string Employment => $"emp-{_run}";

    // The plpgsql identities must start with a letter (Postgres identifier rule): the _run suffix is
    // hex and could lead with a digit, so both names are letter-prefixed.
    private string PoisonFn => $"poison_fn_{_run}";
    private string PoisonTrg => $"poison_trg_{_run}";

    // The job's `now` — fixed so the cold-start floor + watermark advance are deterministic (reuse
    // the sibling class's value).
    private static readonly DateTimeOffset Now =
        new(2026, 6, 1, 3, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_IsolatesPoisonedUserSaveFailure_FromEveryOtherUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // One real Active ad inside the cold-start window (CreatedAt = Now) — its presence makes the
        // new-ads set non-empty so the scan reaches the scorer. The scorer is substituted, so the
        // ad's shadows do not drive the grade (the clean user's grade comes from the Strong stub).
        var realAdId = await SeedAdAsync(ct);

        // Two consenting seekers. Which one becomes the poisoned one is decided at RUN TIME by the
        // profile-builder substitute (the first of the pair the arbitrary due-set order scans), so
        // the counterfactual holds regardless of ordering.
        var ourA = await SeedConsentingSeekerAsync(ct);
        var ourB = await SeedConsentingSeekerAsync(ct);

        var sentinelAdId = Guid.NewGuid(); // never seeded — only the poison scorer references it

        // Profiles keyed by REFERENCE identity (distinct instances, distinct content). Leftover
        // consenting users accumulated in the shared container get the empty-SSYK profile → the SSYK
        // gate (watermark bump, no scoring, no rows — harmless and order-independent).
        var emptySsyk = new FullCandidateMatchProfile(
            new CandidateMatchProfile("", [], [], [], []), []);
        var poisonProfile = new FullCandidateMatchProfile(
            new CandidateMatchProfile("Poison", ["ssyk-poison"], [], [], []), []);
        var cleanProfile = new FullCandidateMatchProfile(
            new CandidateMatchProfile("Clean", ["ssyk-clean"], [], [], []), []);

        var poisonedUserId = Guid.Empty;
        var cleanUserId = Guid.Empty;
        var pairAssigned = false;

        // The lambda returns the raw FullCandidateMatchProfile; NSubstitute's ValueTask<T> Returns
        // overload unwraps it into the ValueTask the port declares (a bare `new ValueTask<T>(...)`
        // return here would trip CA2012).
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var uid = ci.Arg<Guid>();
                if (uid != ourA && uid != ourB)
                    return emptySsyk;                         // leftover users → gated, harmless
                if (!pairAssigned)
                {
                    pairAssigned = true;
                    poisonedUserId = uid;                     // first of the pair scanned = poisoned
                    cleanUserId = uid == ourA ? ourB : ourA;
                    return poisonProfile;
                }
                return cleanProfile;                          // the other of the pair = clean
            });

        // Strong-shaped score: notifiable (persisted) but NOT Top → no Top-direct email dispatch, so
        // the email seam stays out of this test entirely.
        var strongScored = new FullScoredMatch(StrongScore(), SsykIsRelated: false, []);
        var scorer = Substitute.For<IMatchScorer>();
        // Poison profile → score the SENTINEL ad id → the scan Adds a sentinel-keyed match row whose
        // atomic SaveChanges (AFTER Add + AdvanceMatchScan) trips the trigger → genuine poison.
        scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Is<FullCandidateMatchProfile>(p => ReferenceEquals(p, poisonProfile)),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [new JobAdId(sentinelAdId)] = strongScored,
            });
        // Clean profile → score the REAL ad id → a normal, committable match.
        scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Is<FullCandidateMatchProfile>(p => ReferenceEquals(p, cleanProfile)),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [realAdId] = strongScored,
            });

        await InstallPoisonTriggerAsync(sentinelAdId, ct);
        try
        {
            // Isolation held: the poisoned user's SaveChanges failure is caught per-user, never
            // propagated out of the batch.
            await Should.NotThrowAsync(() => RunPoisonJobAsync(profileBuilder, scorer, ct));

            poisonedUserId.ShouldNotBe(Guid.Empty, "en av paret måste ha scannats som förgiftad");
            cleanUserId.ShouldNotBe(Guid.Empty, "den andra av paret måste ha scannats som ren");

            // THE COUNTERFACTUAL (RED on the old shared-context code): the clean user — always
            // scanned AFTER the poisoned one — persists its OWN match and advances its OWN watermark.
            (await CountMatchesAsync(cleanUserId, ct)).ShouldBe(1,
                "den rena userns scan ska vara oberoende av den förgiftade userns fel — exakt en " +
                "match (#751 child scope per user; på delad kontext golvas denna till 0 → RED)");
            (await HasMatchAsync(cleanUserId, realAdId, ct)).ShouldBeTrue(
                "den rena userns enda match ska vara den riktiga annonsen");
            (await GetLastMatchScanAtAsync(cleanUserId, ct)).ShouldBe(Now,
                "den rena userns watermark ska avanceras (dess egen atomiska commit lyckas)");

            // The poisoned user: never advanced, no rows (isolation on the FAILING user — the throw
            // rolls back both the sentinel insert AND the watermark advance in its one SaveChanges).
            (await CountMatchesAsync(poisonedUserId, ct)).ShouldBe(0,
                "den förgiftade userns match rullas tillbaka (atomisk commit kastar)");
            (await GetLastMatchScanAtAsync(poisonedUserId, ct)).ShouldBeNull(
                "watermark ska ALDRIG avanceras vid fel (re-scannas rent nästa körning)");

            // No sentinel-keyed row anywhere: the failed user's partial entities are never flushed by
            // a later save (old code: the trigger kept blocking it; new code: its scope died).
            (await CountRowsWithJobAdIdAsync(new JobAdId(sentinelAdId), ct)).ShouldBe(0,
                "en sentinel-nyckel-rad ska ALDRIG committas (den förgiftade userns partiella " +
                "entiteter flushas aldrig av en senare save)");
        }
        finally
        {
            await DropPoisonTriggerAsync();
        }
    }

    // ─────────────────────────── SUT construction ───────────────────────────

    // The job runs its production per-user child-scope resolution path end-to-end: an
    // OverridingScopeFactory wraps the fixture's REAL root IServiceScopeFactory, so every user
    // resolves a REAL AppDbContext / IUserAccountService from a real DI scope — EXCEPT
    // IMatchProfileBuilder / IMatchScorer, which are the test substitutes (deterministic poison +
    // clean scores). The clock is a FixedClock so `now` (cold-start floor + watermark) is
    // deterministic. Strong scores mean no Top dispatch, so the substituted email sender is inert.
    private async Task RunPoisonJobAsync(
        IMatchProfileBuilder profileBuilder, IMatchScorer scorer, CancellationToken ct)
    {
        var scopeFactory = new OverridingScopeFactory(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(), profileBuilder, scorer);
        var job = new BackgroundMatchingJob(
            scopeFactory,
            Substitute.For<IEmailSender>(),
            new FixedClock(Now),
            _fixture.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<BackgroundMatchingJob>());

        await job.RunAsync(ct);
    }

    // ─────────────────────────── Score recipe (parity BackgroundMatchingJobTopDirectTests) ──────

    private static MatchDimension Match() => new(MatchDimensionVerdict.Match, [], []);
    private static MatchDimension NotAssessed() => new(MatchDimensionVerdict.NotAssessed, [], []);

    // Strong: Ssyk/Region/Employment Match + must-have Match (gate met), no skill/nice signal →
    // Strong (a notifiable grade that is NOT Top → no Top-direct email).
    private static FullMatchScore StrongScore() => new(
        Fast: new MatchScore(Match(), NotAssessed(), Match(), Match()),
        SkillOverlap: NotAssessed(),
        MustHaveCoverage: Match(),
        NiceToHaveCoverage: NotAssessed());

    // ─────────────────────────── Poison trigger (install / drop) ───────────────────────────

    // A sentinel-keyed plpgsql BEFORE INSERT trigger on user_job_ad_matches: any INSERT whose
    // job_ad_id equals the sentinel raises, so the poisoned user's atomic SaveChanges fails AFTER
    // Add + AdvanceMatchScan (genuine poison, not a throw-before-Add). Two statements, two calls.
    private async Task InstallPoisonTriggerAsync(Guid sentinelAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // DDL with an embedded (test-generated, non-user) sentinel GUID literal — it cannot be
        // parameterized (identifiers + a plpgsql body), so the SQL is built into plain `string`
        // locals (not passed as an interpolated-string expression) to satisfy EF1002 without
        // ExecuteSqlAsync's parameterization, which does not apply to DDL.
        string createFunction =
            $@"CREATE OR REPLACE FUNCTION {PoisonFn}() RETURNS trigger AS $fn$
BEGIN
    IF NEW.job_ad_id = '{sentinelAdId}'::uuid THEN
        RAISE EXCEPTION 'poison sentinel {_run}';
    END IF;
    RETURN NEW;
END;
$fn$ LANGUAGE plpgsql;";
        string createTrigger =
            $@"CREATE TRIGGER {PoisonTrg} BEFORE INSERT ON user_job_ad_matches
    FOR EACH ROW EXECUTE FUNCTION {PoisonFn}();";

        await db.Database.ExecuteSqlRawAsync(createFunction, ct);
        await db.Database.ExecuteSqlRawAsync(createTrigger, ct);
    }

    // Drop in a finally, no CancellationToken — cleanup must run even if the test token is cancelled
    // (a leaked trigger fails unrelated later tests in the shared Worker collection).
    private async Task DropPoisonTriggerAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Plain `string` locals (not interpolated-string expressions at the call site) — EF1002, as
        // in InstallPoisonTriggerAsync; identifiers cannot be parameterized.
        string dropTrigger = $"DROP TRIGGER IF EXISTS {PoisonTrg} ON user_job_ad_matches;";
        string dropFunction = $"DROP FUNCTION IF EXISTS {PoisonFn}();";
        await db.Database.ExecuteSqlRawAsync(dropTrigger);
        await db.Database.ExecuteSqlRawAsync(dropFunction);
    }

    // ─────────────────────────── Seeding (mechanics copied from the sibling class) ──────────────

    // Seeds an Imported (→ Active) JobAd created at `now` → CreatedAt inside the 7-day cold-start
    // window the scan queries. terms are omitted (the scorer is substituted, so the ad's extracted
    // terms / shadows do not drive the grade). Parity BackgroundMatchingJobIntegrationTests.SeedAdAsync.
    private async Task<JobAdId> SeedAdAsync(CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rawPayload = BuildRawPayload(externalId, OccupationGroup, Region, Employment);
        var publishedAt = Now.AddDays(-1);
        var jobAd = JobAd.Import(
            title: "Bakgrundsmatchnings-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: publishedAt.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: []).Value; // CreatedAt = Now

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // occupation_group + employment_type are TOP-LEVEL; region lives under workplace_address
    // (parity BackgroundMatchingJobIntegrationTests.BuildRawPayload).
    private static string BuildRawPayload(
        string externalId, string occupationGroup, string region, string employment) =>
        $"{{\"id\":\"{externalId}\","
        + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}},"
        + $"\"workplace_address\":{{\"region_concept_id\":\"{region}\"}},"
        + $"\"employment_type\":{{\"concept_id\":\"{employment}\"}}}}";

    // Seeds a CONSENTING JobSeeker (opt-in ON, never withdrawn) → inside the opted-in due-set. The
    // stated preferences are irrelevant here (the profile builder is substituted) but seeded for
    // parity with a real consenting user.
    private async Task<Guid> SeedConsentingSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var userId = Guid.NewGuid();
        var jobSeeker = JobSeeker.Register(userId, "Poison-isolation seed", clock).Value;

        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: [OccupationGroup],
            preferredRegions: [Region],
            preferredEmploymentTypes: [Employment],
            preferredMunicipalities: null,
            preferredSkills: []).Value;
        jobSeeker.UpdateMatchPreferences(prefs, clock);
        jobSeeker.UpdateNotificationConsent(true, DigestCadence.Weekly, clock);

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // ─────────────────────────── Read-back (fresh fixture scopes, never the SUT's) ──────────────

    private async Task<int> CountMatchesAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking().CountAsync(m => m.UserId == userId, ct);
    }

    private async Task<bool> HasMatchAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);
    }

    private async Task<int> CountRowsWithJobAdIdAsync(JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking().CountAsync(m => m.JobAdId == jobAdId, ct);
    }

    private async Task<DateTimeOffset?> GetLastMatchScanAtAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobSeeker = await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == userId, ct);
        jobSeeker.ShouldNotBeNull("seeded JobSeeker måste finnas");
        return jobSeeker.LastMatchScanAt;
    }

    // ─────────────────────────── SUT scope factory + clock ───────────────────────────

    // Wraps the fixture's REAL root IServiceScopeFactory: each child scope resolves everything from a
    // REAL DI scope EXCEPT IMatchProfileBuilder / IMatchScorer, which are the test substitutes. Lets
    // the job run its production per-user child-scope resolution path while the two matching
    // collaborators are controlled — the db reads/writes still hit real Postgres.
    private sealed class OverridingScopeFactory(
        IServiceScopeFactory inner,
        IMatchProfileBuilder profileBuilder,
        IMatchScorer scorer) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            new OverridingScope(inner.CreateScope(), profileBuilder, scorer);

        private sealed class OverridingScope(
            IServiceScope innerScope,
            IMatchProfileBuilder profileBuilder,
            IMatchScorer scorer) : IServiceScope, IServiceProvider, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IMatchProfileBuilder) ? profileBuilder
                : serviceType == typeof(IMatchScorer) ? scorer
                : innerScope.ServiceProvider.GetService(serviceType);

            public void Dispose() => innerScope.Dispose();

            public async ValueTask DisposeAsync()
            {
                if (innerScope is IAsyncDisposable ad)
                    await ad.DisposeAsync();
                else
                    innerScope.Dispose();
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
