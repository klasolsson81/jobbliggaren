using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0080 Vag 4 PR-3 (Beslut 2/3) — Testcontainers integration tests for
/// <see cref="BackgroundMatchingJob"/> against REAL Postgres. NEVER EF-InMemory: the grade is
/// computed by the real <c>MatchScorer.ScoreFullBatchAsync</c>, which reads the STORED generated
/// shadow columns (occupation_group / region / employment) + the jsonb <c>extracted_lexemes</c>
/// overlap — all hidden by InMemory (memory ef_strongly_typed_vo_contains /
/// denormalized_projection_plaintext_dek_free). The consent predicate also runs against the real
/// jsonb <c>preferences</c> column.
///
/// <para>
/// The job is constructed directly (<c>new BackgroundMatchingJob(...)</c>) against the fixture's
/// REAL root <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/> (#751 —
/// the job resolves db/builder/scorer/account-service from a child scope PER USER, so these tests
/// exercise the production resolution path end-to-end). It does NOT depend on the Hangfire
/// wrapper / DI registration. An injected <see cref="FixedClock"/> is the job's <c>now</c>, so
/// the cold-start floor + watermark are deterministic.
/// </para>
///
/// <para>
/// <b>Grade-seeding recipe</b> (against <see cref="MatchGradeCalculator.Grade(FullMatchScore)"/>,
/// SSYK=Match assumed via a matching occupation_group shadow):
/// <list type="bullet">
/// <item><b>Good</b> — region + employment Match (both secondaries) but the user has NO confirmed
///   skills → MustHaveCoverage = NotAssessed → requirement gate NOT met → preference-fit "Good".</item>
/// <item><b>Top</b> — region + employment Match + the user confirms the ad's must-have skill
///   (MustHaveCoverage = Match) AND a skill signal (SkillOverlap = Match) → "Top". The Worker is
///   the FIRST place the FULL grade runs, so the first place Top is produced.</item>
/// <item><b>Basic</b> (honest floor) — region + employment shadows NULL (both NotAssessed) and no
///   skills → 0 confirmed secondaries → "Basic" → NEVER persisted.</item>
/// </list>
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class BackgroundMatchingJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // PER-TEST-UNIQUE shadow concept-ids (xUnit news up a fresh instance per [Fact]). The Worker
    // job has no filter knob — it scans EVERY Active ad in the window — and the [Collection]
    // fixture shares ONE Postgres container across the whole class, so ads accumulate across tests.
    // Without isolation, a user in one test would also match another test's Good-band ads (same
    // occupation/region/employment), inflating per-user match counts (the idempotency test caught
    // this). A unique run-id woven into the shadows makes each test's user match ONLY its own ads
    // — the analogue of the sort oracles' unique-worktime-extent run isolation. Kept within the
    // concept-id regex (^[A-Za-z0-9_-]{1,32}$): "grp-" + 20 hex = 24 chars.
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];
    private string OccupationGroup => $"grp-{_run}";
    private string Region => $"reg-{_run}";
    private string Employment => $"emp-{_run}";
    private string SkillConceptId => $"skill-{_run}";
    private const string SkillDisplay = "C#";

    // The job's `now` — a fixed point so the cold-start 7-day floor and the watermark advance
    // are deterministic (the real DateTimeProvider would make "8 days ago" flaky).
    private static readonly DateTimeOffset Now =
        new(2026, 6, 1, 3, 20, 0, TimeSpan.Zero);

    // ────────────────────────────────────────────────────────────────
    // 1. Persists notifiable matches + advances the watermark.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PersistsNotifiableMatch_AndAdvancesWatermark_WhenConsentingUserMatchesAd()
    {
        var ct = TestContext.Current.CancellationToken;

        // A Good-band ad (occupation + region + employment all Match; no must-have for the
        // skill-less user). Published within the 7-day cold-start floor.
        var jobAdId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);

        // Consenting user, stated occupation+region+employment matching the ad, NO skills →
        // the requirement gate is unmet → Good (a notifiable grade).
        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);

        var match = await GetMatchAsync(userId, jobAdId, ct);
        match.ShouldNotBeNull("en consenting user vars match ska persisteras");
        match.Grade.ShouldBe(NotifiableMatchGrade.Good);
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        match.UserId.ShouldBe(userId);
        match.JobAdId.ShouldBe(jobAdId);

        // The watermark advanced (scanned through `now`) — atomically with the insert.
        var scanAt = await GetLastMatchScanAtAsync(userId, ct);
        scanAt.ShouldNotBeNull("LastMatchScanAt ska sättas (watermark) efter en scan");
        scanAt.Value.ShouldBe(Now);
    }

    // ────────────────────────────────────────────────────────────────
    // 2. Grade parity — the persisted grade equals the engine's grade for that ad+profile.
    //    Includes a Top case (the Worker is the first producer of Top).
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PersistsGrade_EqualToEngineGrade_IncludingTop()
    {
        var ct = TestContext.Current.CancellationToken;

        // Top recipe: occupation + region + employment Match, the ad has a MUST-HAVE skill term,
        // and the user CONFIRMS that skill → MustHaveCoverage = Match + SkillOverlap = Match
        // (skill term is also a generic Skill term) → Top.
        var terms = ExtractedTerms.From(
        [
            SkillTerm(SkillConceptId, SkillDisplay),
            RequirementTerm(SkillConceptId, SkillDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms, ct);

        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],
            employments: [Employment],
            skills: [SkillConceptId],
            ct: ct);

        await RunJobAsync(ct);

        var match = await GetMatchAsync(userId, jobAdId, ct);
        match.ShouldNotBeNull();
        match.Grade.ShouldBe(NotifiableMatchGrade.Top,
            "Worker ska producera Top (den är första stället FULL-graden körs)");

        // SSOT cross-check: recompute the engine grade the same way the Worker does (the same
        // scorer + the same profile + MatchGradeCalculator.Grade(FullMatchScore)), then map to
        // NotifiableMatchGrade. The persisted grade MUST equal this — no Worker drift.
        var engineGrade = await ComputeEngineGradeAsync(userId, jobAdId, ct);
        engineGrade.ShouldNotBeNull("the engine must also produce a notifiable grade for this seed");
        match.Grade.ShouldBe(engineGrade.Value,
            "den persisterade graden ska vara EXAKT motorns grad (ingen Worker-drift)");
    }

    // ────────────────────────────────────────────────────────────────
    // 3. Honest floor — a Basic-grade ad produces NO match row.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DoesNotPersist_WhenGradeIsBasic_HonestFloor()
    {
        var ct = TestContext.Current.CancellationToken;

        // Basic: occupation Match but NEITHER secondary confirmed (region + employment shadows
        // NULL → both NotAssessed) and no skills → 0 confirmed secondaries → Basic.
        var jobAdId = await SeedAdAsync(
            OccupationGroup, regionConceptId: null, employmentTypeConceptId: null,
            Now.AddDays(-1), terms: null, ct);

        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            // stated, ad has no region/employment → Basic. Pre-#552 via 0 confirmed secondaries
            // (both NotAssessed); post-#552 via the ort + employment NoMatch RB1 floor. Basic either way.
            regions: [Region],
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);

        var match = await GetMatchAsync(userId, jobAdId, ct);
        match.ShouldBeNull("Basic/null ska ALDRIG persisteras (honest floor, D1)");

        // The watermark still advances (we scanned through `now`, just produced 0 notifiable).
        var scanAt = await GetLastMatchScanAtAsync(userId, ct);
        scanAt.ShouldBe(Now, "watermark ska avanceras även när 0 notifierbara matchningar skapas");
    }

    // ────────────────────────────────────────────────────────────────
    // 3b. #552 grade-gate — a PREVIOUSLY-GOOD ad the gate demotes to Basic stops being persisted.
    //
    //    This is distinct from the honest-floor test above: there the ad had 0 confirmed secondaries
    //    (Basic in every era). HERE the ad had exactly ONE confirmed secondary (employment Match) and
    //    a NULL ort shadow, so pre-#552 it graded GOOD (ort NotAssessed does not floor) and WAS
    //    persisted + notified. #552 makes the STATED-ort NULL shadow a NoMatch → RB1 floors it to
    //    Basic → below the notifiable threshold → NO UserJobAdMatch row. So this is the consequence
    //    the ticket names: the background scan stops persisting (and emailing) matches the gate
    //    demotes. RED against current production, which persists a Good row for the gated ad.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DoesNotPersist_WhenStatedOrtGateFloorsPreviouslyGoodToBasic()
    {
        var ct = TestContext.Current.CancellationToken;

        // gatedAd: occupation Match + region NULL (the ad states no ort) + employment Match. The user
        // STATES a region preference, so pre-#552 the NULL ort read NotAssessed → 1 confirmed
        // secondary (employment) → GOOD (persisted). #552: stated region + NULL ort shadow → RegionFit
        // NoMatch → RB1 floor → Basic → NOT persisted.
        var gatedAd = await SeedAdAsync(
            OccupationGroup, regionConceptId: null, employmentTypeConceptId: Employment,
            Now.AddDays(-1), terms: null, ct);

        // notifiableAd (non-vacuity): region Match + employment Match → Good for a skill-less user →
        // persisted in BOTH production states, proving the scan ran and can persist.
        var notifiableAd = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);

        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],          // STATED region — the dimension the ad leaves NULL
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);

        // Non-vacuity: the genuinely-notifiable ad IS persisted (the scan ran and produced a match).
        (await GetMatchAsync(userId, notifiableAd, ct))
            .ShouldNotBeNull("den äkta notifierbara annonsen ska persisteras (scannen körde)");

        // THE #552 GATE: the previously-Good ad is floored to Basic → NO row persisted.
        (await GetMatchAsync(userId, gatedAd, ct))
            .ShouldBeNull("#552: en annons vars ort-grind golvar den från Good till Basic ska INTE " +
                "persisteras (bakgrundsscannen slutar persistera/notifiera demoterade matchningar)");

        // Engine-grade cross-check: recompute the grade exactly as the Worker does — the gated ad is
        // no longer notifiable (Basic → null), the anchor still is.
        (await ComputeEngineGradeAsync(userId, gatedAd, ct))
            .ShouldBeNull("#552: den grindade annonsen graderar Basic → inte notifierbar (motorn)");
        (await ComputeEngineGradeAsync(userId, notifiableAd, ct))
            .ShouldNotBeNull("ankar-annonsen är fortfarande notifierbar");
    }

    // ────────────────────────────────────────────────────────────────
    // 4. Idempotency — running twice does not duplicate or throw.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_RunTwice_DoesNotDuplicateMatches_NoDbUpdateException()
    {
        var ct = TestContext.Current.CancellationToken;

        var jobAdId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);
        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);
        var countAfterFirst = await CountMatchesAsync(userId, ct);
        countAfterFirst.ShouldBe(1, "första körningen ska skapa exakt en match");

        // Second run — the watermark advanced past the ad AND the dedup skip both protect it.
        // Must not throw DbUpdateException (UNIQUE(UserId, JobAdId)).
        await RunJobAsync(ct);
        var countAfterSecond = await CountMatchesAsync(userId, ct);

        countAfterSecond.ShouldBe(countAfterFirst,
            "andra körningen ska INTE skapa dubbletter (watermark + UNIQUE-dedup)");
    }

    // ────────────────────────────────────────────────────────────────
    // 5. Cold-start floor — an ad INGESTED (CreatedAt) more than 7 days ago is NOT matched on the
    //    first run; one ingested within 7 days IS. The window is CreatedAt (ingest time), NOT
    //    PublishedAt (ADR 0080 Beslut 2 / security-auditor 2026-06-24), so this varies CreatedAt
    //    via the backdate override; PublishedAt is fixed within-window and irrelevant to the gate.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_FirstRun_MatchesAdsIngestedWithinSevenDays_ButNotOlder()
    {
        var ct = TestContext.Current.CancellationToken;

        // INGESTED (CreatedAt) 8 days before `now` → outside the cold-start floor (LastMatchScanAt
        // null → floor = now - 7d). PublishedAt is fixed within-window to PROVE the gate reads
        // CreatedAt, not PublishedAt — a recent publish must NOT rescue an old-ingest ad.
        var oldIngestId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct,
            createdAtOverride: Now.AddDays(-8));
        // Ingested at `now` (default CreatedAt = Now) → inside the floor.
        var recentIngestId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);

        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);

        (await GetMatchAsync(userId, oldIngestId, ct))
            .ShouldBeNull("en annons INGESTAD (CreatedAt) äldre än 7 dagar ska INTE matchas på " +
                "första körningen (cold-start floor läser CreatedAt, inte PublishedAt)");
        (await GetMatchAsync(userId, recentIngestId, ct))
            .ShouldNotBeNull("en annons ingestad inom 7 dagar SKA matchas på första körningen");
    }

    // ────────────────────────────────────────────────────────────────
    // 6. Consent gate — only consenting (opt-in, not withdrawn) users get matches.
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_OnlyConsentingUsersAreScanned_OffAndWithdrawnAreExcluded()
    {
        var ct = TestContext.Current.CancellationToken;

        var jobAdId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);

        // a) Consent OFF (default — never opted in).
        var (offUserId, _) = await SeedJobSeekerAsync(
            occupationGroups: [OccupationGroup], regions: [Region], employments: [Employment],
            skills: [], consent: ConsentState.Off, ct: ct);

        // b) Opted in then WITHDREW (the opted-in query filters on withdrawn-null).
        var (withdrawnUserId, _) = await SeedJobSeekerAsync(
            occupationGroups: [OccupationGroup], regions: [Region], employments: [Employment],
            skills: [], consent: ConsentState.Withdrawn, ct: ct);

        // c) Consenting (opt-in ON, not withdrawn).
        var (onUserId, _) = await SeedJobSeekerAsync(
            occupationGroups: [OccupationGroup], regions: [Region], employments: [Employment],
            skills: [], consent: ConsentState.On, ct: ct);

        await RunJobAsync(ct);

        (await GetMatchAsync(offUserId, jobAdId, ct))
            .ShouldBeNull("user med consent OFF ska INTE få matchningar (GDPR Art. 6/7)");
        (await GetMatchAsync(withdrawnUserId, jobAdId, ct))
            .ShouldBeNull("user som återkallat samtycke ska INTE få nya matchningar");
        (await GetMatchAsync(onUserId, jobAdId, ct))
            .ShouldNotBeNull("en consenting user SKA få matchningar");

        // The off/withdrawn users were never scanned → watermark stays null.
        (await GetLastMatchScanAtAsync(offUserId, ct))
            .ShouldBeNull("en icke-scannad user ska inte ha någon watermark");
        (await GetLastMatchScanAtAsync(withdrawnUserId, ct))
            .ShouldBeNull("en återkallad user ska inte scannas → ingen watermark");
    }

    // ────────────────────────────────────────────────────────────────
    // 7. Atomicity (observable post-condition) — after a run that produces matches, BOTH the
    //    rows AND the watermark are present (they commit together in one SaveChanges).
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CommitsMatchesAndWatermarkTogether_BothPresentAfterRun()
    {
        var ct = TestContext.Current.CancellationToken;

        var jobAdId = await SeedAdAsync(
            OccupationGroup, Region, Employment, Now.AddDays(-1), terms: null, ct);
        var (userId, _) = await SeedConsentingJobSeekerAsync(
            occupationGroups: [OccupationGroup],
            regions: [Region],
            employments: [Employment],
            skills: [],
            ct: ct);

        await RunJobAsync(ct);

        // Both observable post-conditions of the single transaction must hold together: the match
        // row exists AND the watermark advanced (a non-atomic split would show one without the other).
        var match = await GetMatchAsync(userId, jobAdId, ct);
        var scanAt = await GetLastMatchScanAtAsync(userId, ct);

        match.ShouldNotBeNull("match-raden ska finnas (committad)");
        scanAt.ShouldBe(Now, "watermark ska vara avancerad (committad i SAMMA transaktion)");
    }

    // ─────────────────────────── SUT construction ───────────────────────────

    // #751: the job now owns its per-user child scopes, so the SUT gets the fixture's REAL root
    // IServiceScopeFactory — every user resolves a fresh AppDbContext + the REAL IMatchProfileBuilder
    // / IMatchScorer from fixture DI (the fixture registers AddMatchingEngine + AddTextAnalysis +
    // AddCoreIdentityForWorker, mirroring Worker/Program.cs — the pre-#751 comment claiming the
    // matching collaborators were unregistered was stale). This exercises the production resolution
    // path end-to-end: child scope → scoped context shared by db/builder/scorer within one user.
    // The real scoped IUserAccountService resolves too; no Identity users are seeded, so GetEmailAsync
    // returns null and any Top-row dispatch skips benignly — these grade-parity / watermark /
    // cold-start tests assert PERSISTENCE, not delivery (the delivery seam is pinned by the unit
    // tests + DigestDispatchJobIntegrationTests). The match rows persist before the send regardless.
    // The clock is a FixedClock so `now` (cold-start floor + watermark) is deterministic.
    private async Task RunJobAsync(CancellationToken ct)
    {
        var job = new BackgroundMatchingJob(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IEmailSender>(),
            new FixedClock(Now),
            _fixture.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<BackgroundMatchingJob>());

        await job.RunAsync(ct);
    }

    // Recomputes the grade exactly as the Worker does (same scorer, same profile builder, same
    // MatchGradeCalculator.Grade(FullMatchScore)), then maps Good/Strong/Top → NotifiableMatchGrade.
    // The C# SSOT the parity test compares against — if this diverges from the persisted grade,
    // the Worker has drifted from the engine.
    private async Task<NotifiableMatchGrade?> ComputeEngineGradeAsync(
        Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var profileBuilder = NewProfileBuilder(db, sp);
        var scorer = NewScorer(db);

        var profile = await profileBuilder.BuildFullForUserIdAsync(userId, ct);
        var scores = await scorer.ScoreFullBatchAsync([jobAdId], profile, ct);
        scores.TryGetValue(jobAdId, out var scored).ShouldBeTrue("the seeded ad must be scored");

        // PR-4 (#300, ADR 0084): the carrier holds the score + SsykIsRelated. Recompute the
        // grade EXACTLY as the Worker does — Grade(FullMatchScore, bool) with the carrier's
        // related bit — so this SSOT cross-check mirrors the production path (a related-only hit
        // grades Related, which the switch below maps to null = not notifiable).
        var grade = MatchGradeCalculator.Grade(scored!.Score, scored.SsykIsRelated);
        return grade switch
        {
            MatchGrade.Good => NotifiableMatchGrade.Good,
            MatchGrade.Strong => NotifiableMatchGrade.Strong,
            MatchGrade.Top => NotifiableMatchGrade.Top,
            _ => null,
        };
    }

    // The real Application-layer preference→profile mapper. BuildFullForUserIdAsync loads by an
    // explicit user-id (the background/system seam) and does NOT consult ICurrentUser, but the
    // ctor requires one — resolve the fixture's WorkerSystemUser. #300 PR-3: the ctor also takes
    // ITaxonomyReadModel (the related-occupation ACL) — resolved from the fixture SP, which gets
    // it via AddMatchingEngine()'s TryAddSingleton (the Worker is HTTP-free, no AddJobSources).
    // The background path never broadens, so it is never called, but the ctor needs it.
    private static MatchProfileBuilder NewProfileBuilder(AppDbContext db, IServiceProvider sp) =>
        new(db,
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel>());

    // The real Infrastructure scorer (internal — visible via InternalsVisibleTo) + the real
    // Swedish Snowball analyzer. Parity FullMatchScorerIntegrationTests.NewScorer.
    private static MatchScorer NewScorer(AppDbContext db) =>
        new(db, new LocalTextAnalyzer(new SnowballStemmer()));

    // ─────────────────────────── Seeding helpers ───────────────────────────

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    private static ExtractedTerm RequirementTerm(
        string conceptId, string display, ExtractedTermSource source) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Requirement,
            Source: source, MatchedOn: display, ConceptId: conceptId, Weight: 1);

    // Seeds an Imported (→ Active) JobAd whose raw_payload drives the facet columns
    // (occupation_group / region / employment) and, when terms is non-null, the extracted_terms
    // VO (which generates the STORED extracted_lexemes GIN column). Parity
    // FullMatchScorerIntegrationTests.
    //
    // The Worker's new-ads window filters on CreatedAt (INGEST time), NOT PublishedAt (ADR 0080
    // Beslut 2 / security-auditor 2026-06-24 — PublishedAt is JobTech-supplied and can be
    // backdated). So the ad is IMPORTED with a clock fixed at `now` → CreatedAt == Now (a
    // deterministic ingest time aligned with the job's `now`, so a fresh ad is inside the window
    // CreatedAt > now-7d). publishedAt no longer drives the window — fixed to a within-window
    // value. createdAtOverride backdates CreatedAt via raw SQL (the cold-start floor lever): the
    // aggregate stamps CreatedAt = clock.UtcNow on Import, so an OLD ingest time can only be set
    // post-insert (raw UPDATE on created_at, the column the Worker filters on).
    private async Task<JobAdId> SeedAdAsync(
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        ExtractedTerms? terms,
        CancellationToken ct,
        DateTimeOffset? createdAtOverride = null)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId, employmentTypeConceptId);

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
            clock: new FixedClock(Now), declaredContacts: []).Value; // CreatedAt = Now (deterministic ingest time)

        if (terms is not null)
            jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);

        if (createdAtOverride is { } createdAt)
        {
            // Backdate the INGEST timestamp the Worker windows on (created_at), distinct from the
            // domain-stamped CreatedAt = Now. Raw SQL because CreatedAt has a private setter.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE job_ads SET created_at = {createdAt} WHERE id = {jobAd.Id.Value}", ct);
        }

        return jobAd.Id;
    }

    // occupation_group + employment_type are TOP-LEVEL; region lives under workplace_address
    // (parity FullMatchScorerIntegrationTests / the sort oracles). A null id → that shadow stays
    // NULL → the corresponding dimension reports NotAssessed (never NoMatch).
    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId)
    {
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";
        var addressJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";
        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson}}}";
    }

    private enum ConsentState { Off, On, Withdrawn }

    // Seeds a JobSeeker with stated MatchPreferences and opt-IN consent (the common case).
    private Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedConsentingJobSeekerAsync(
        IReadOnlyList<string> occupationGroups,
        IReadOnlyList<string> regions,
        IReadOnlyList<string> employments,
        IReadOnlyList<string> skills,
        CancellationToken ct) =>
        SeedJobSeekerAsync(occupationGroups, regions, employments, skills, ConsentState.On, ct);

    // Seeds a JobSeeker with stated MatchPreferences + the chosen consent state. Off = never
    // opted in (default). On = UpdateNotificationConsent(true). Withdrawn = opt in then opt out
    // (UpdateNotificationConsent(true) then (false)) → BackgroundMatchNotificationsEnabled=false
    // AND NotificationConsentWithdrawnAt set (the opted-in query excludes it on either predicate).
    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedJobSeekerAsync(
        IReadOnlyList<string> occupationGroups,
        IReadOnlyList<string> regions,
        IReadOnlyList<string> employments,
        IReadOnlyList<string> skills,
        ConsentState consent,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var userId = Guid.NewGuid();
        var jobSeeker = JobSeeker.Register(userId, "Bakgrundsmatch Seed", clock).Value;

        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: occupationGroups,
            preferredRegions: regions,
            preferredEmploymentTypes: employments,
            preferredMunicipalities: null,
            preferredSkills: skills).Value;
        jobSeeker.UpdateMatchPreferences(prefs, clock);

        switch (consent)
        {
            case ConsentState.On:
                jobSeeker.UpdateNotificationConsent(true, DigestCadence.Weekly, clock);
                break;
            case ConsentState.Withdrawn:
                jobSeeker.UpdateNotificationConsent(true, DigestCadence.Weekly, clock);
                jobSeeker.UpdateNotificationConsent(false, DigestCadence.Weekly, clock);
                break;
            case ConsentState.Off:
            default:
                break; // default OFF, never opted in
        }

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return (userId, jobSeeker.Id);
    }

    // ─────────────────────────── Read-back helpers ───────────────────────────

    // The (UserId, JobAdId) pair is unique (the dedup spine), so FirstOrDefault is exact.
    private async Task<UserJobAdMatch?> GetMatchAsync(
        Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);
    }

    private async Task<int> CountMatchesAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches
            .AsNoTracking()
            .CountAsync(m => m.UserId == userId, ct);
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
