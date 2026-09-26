using System.Data.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1681 part 2 / ADR 0139 — the EXPLAIN pin Klas's budget acceptance of 2026-09-07 rides on
/// ("Klas-beviljande 4", condition (i)).
///
/// <para>
/// <b>Why an acceptance needs a plan pin at all.</b> The accepted state is the composed ad-set
/// ceiling — 20 criteria × ~2 000 ads = a 40 000-id union — measured at 240 ms against ADR 0045's
/// 300 ms class (a). That figure describes a QUERY PLAN, not a query. The measurement
/// (<c>docs/reviews/2026-09-07-1681-part2-fanin-and-corpus-measurement.md</c>) found the planner's
/// row estimate on the grade predicate wrong by 3–4 orders of magnitude (1/2/8/158 estimated against
/// actuals up to 39 277 — 249x), and the access path measurably FLIPPING inside the operating range.
/// An accepted number over an unpinned fragile plan has no shelf life: the plan flips, every semantic
/// test stays green, and the accepted figure silently stops describing the query.
/// </para>
///
/// <para>
/// <b>What is pinned, and what deliberately is NOT.</b> The measured shapes vary LEGITIMATELY with
/// union size and profile — pk bitmap at n=200, an ordered
/// <c>ix_job_ads_status_published_at_id</c> Index Scan at n=600–2 000, a Bitmap Heap Scan at the
/// 40 000 ceiling, and <c>Gather</c> + <c>Parallel Bitmap Heap Scan</c> for broad profiles. Pinning
/// ONE of them would be flaky AND untrue. What every measured shape shares is that the rows are
/// reached THROUGH AN INDEX. So the pin is exactly that pair, and nothing narrower: an index node is
/// present, and there is no <c>Seq Scan</c> on <c>job_ads</c>. A regression to a full scan is the
/// thing that would silently invalidate the accepted figure.
/// </para>
///
/// <para>
/// <b>NO <c>enable_seqscan = off</c> here, and that is not an oversight.</b> The sibling
/// <c>CompanyWatchBrowseQueryPlanTests</c> uses that GUC because it claims index ELIGIBILITY. This
/// test claims the OPPOSITE kind of fact — that the planner does not CHOOSE a full scan — and on
/// PostgreSQL 17+ (this repo runs 18.3) the GUC is an effective prohibition rather than a cost
/// penalty, so setting it would make the assertion vacuous by construction: it would forbid the very
/// regression being guarded. Same discipline as
/// <see cref="JobAdBrowseSortQueryPlanTests"/>'s no-GUC choice guard.
/// </para>
///
/// <para>
/// <b>It EXPLAINs the command production emits, never a hand-typed lookalike.</b> The test calls the
/// real <see cref="IPerUserJobAdSearchQuery.FilterToMatchingAsync"/> on a real
/// <see cref="PerUserJobAdSearchQuery"/>, captures the <see cref="DbCommand"/> EF actually sent
/// (text AND parameter types, through a <see cref="DbCommandInterceptor"/> — the
/// <c>JobAdCountBitmapPlanHygieneTests</c> idiom), and re-issues it prefixed with <c>EXPLAIN</c> on
/// the same connection. This repo has already shipped the other thing: <c>Jobbliggaren.Migrate</c>'s
/// <c>explain-search</c> tool hand-wrote its SQL, drifted from the production predicate, and (in its
/// own comment) "lied in the REASSURING direction". Parameter TYPES matter as much as the text — the
/// id set is bound as <c>uuid[]</c>, and binding it any other way EXPLAINs a different plan.
/// </para>
///
/// <para>
/// <b>The fixture must be large AND WIDE, or the pin is vacuous in one direction and false in the
/// other.</b> On a small table Postgres seq-scans whatever you do, so a
/// <c>ShouldNotContain("Seq Scan")</c> there would simply be wrong about production. Row WIDTH is the
/// half that is easy to miss and that the measurement report calls out by name: its own first probe
/// was 25x narrower per row than dev (76.6 B vs 1 898 B) and "a narrow fixture will tell you the
/// opposite of the truth", because a narrow heap makes a sequential scan cheap. So this class seeds
/// dev's ROW COUNTS (~100 000 rows, ~40 000 of them Active) with a padded description, and
/// <see cref="Fixture_IsWideEnoughToPlanLikeDev"/> asserts the width floor rather than trusting it.
/// The fixture lands between dev's width and the narrow probe's, which errs the RIGHT way: a
/// comparatively cheaper sequential scan than dev's makes an index plan here harder to obtain, not
/// easier, so the pin fails toward the regression rather than away from it.
/// </para>
///
/// <para>
/// <b>The absence assertion has a POSITIVE CONTROL, and without it this would not be a pin.</b> A
/// <c>ShouldNotContain</c> passes for every reason there is, including the ones that mean the
/// instrument is broken: the EXPLAIN failed, the plan came back empty, the statement never ran, the
/// fixture degenerated. <see cref="SeqScanIsObservable_SoTheAbsenceAssertionCanFail"/> EXPLAINs a
/// statement over the SAME table, in the SAME fixture, on the SAME connection, through the SAME
/// helper, whose predicate no index can serve — and asserts the token IS present. That is the
/// difference between an assertion and a pin, and it is this file's sibling's own hard-won lesson
/// (<c>CompanyQueries_StillReadTheRegister_SoTheAbsenceAssertionsCanFail</c>).
/// </para>
///
/// <para>
/// <b>Premise (CLAUDE.md §5 <c>Tests:</c>).</b> What the assertions rest on is a STATISTICS AND
/// STORAGE REGIME — table size, row width, Active share, and an id array — and every one of those is
/// state production's own writers produce: the Platsbanken ingest writes rows through
/// <c>JobAd.Import</c>/<c>SetSourcePayload</c> (facet columns and the payload they were read from,
/// written together here for exactly that reason), <c>ArchiveExternalJobAdCommandHandler</c> writes
/// the Archived share, and <c>CriterionMatchingAdSetResolver</c> hands the id set to the port. The
/// bulk INSERT is convenience, not a premise — it seeds a PLANNER regime, not a semantic fixture, the
/// same argument <see cref="JobAdBrowseSortQueryPlanTests"/> makes. Nothing here asserts a GRADE:
/// that the SQL rank equals <c>MatchGradeCalculator</c> is pinned, membership by membership, in
/// <c>FilterToMatchingTests</c>, and this class deliberately does not restate it.
/// </para>
///
/// <para>
/// <b>What the fixture does NOT reproduce, said rather than dressed up.</b> Its facet vocabulary is
/// coarser than dev's (20 occupation groups / 10 regions / 50 municipalities / 4 employment types
/// against dev's 391 / 21 / 290). That simplification is admissible HERE for one reason worth
/// stating: the grade predicate is a <c>CASE</c> expression, which the planner cannot estimate from
/// any column statistic at all — it is the source of the 249x error this pin exists for — so the
/// facet DISTRIBUTION cannot move the plan. Table size, row width, the Active share and the id
/// array's length can, and those are reproduced. What the profile has to be is non-degenerate in
/// both directions, and the theory ASSERTS that rather than this paragraph claiming it.
/// </para>
///
/// <para>
/// <b>Nor does it reproduce dev's shapes one-for-one, and it does not need to.</b> Dev's four
/// measured shapes and this fixture's are both entirely index-driven, but they are not the same four
/// — which is the whole argument for writing the pin shape-agnostically rather than pinning a node.
/// What this fixture does reproduce is the two properties the acceptance actually rests on: the
/// planner's row estimate on the grade predicate collapses to single- and double-digit numbers
/// against five-figure actuals, and the access path FLIPS between indexes inside the operating range
/// as the union grows. Run the class and read its output for the current plans; a plan transcribed
/// into this comment would be a measured number in a tracked file (CLAUDE.md §5 <c>Comments:</c>),
/// and it would decay. This is not a latency measurement either — the timings live in the report.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
// Parity the collection siblings: the trait excludes nothing from a default run (AGENTS.md §7). It
// marks that this class TRUNCATEs job_ads, seeds 100 000 wide rows and ANALYZEs, so its cost is
// visible to whoever reads the suite.
[Trait("Category", "SmokeTest")]
public class FilterToMatchingQueryPlanTests(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    // Dev's row COUNTS (SuggestUnionLatencyMeasurement records the same ~106 000 figure for the dev
    // database; the 2026-09-04 measurement records 41 597 Active). Two rows in five are Active, so
    // the 40 000-id ceiling union is ~40 % of the table rather than ~80 % — which is the ratio the
    // report's ceiling row was measured at, and the ratio at which a bitmap path is on the table at
    // all.
    private const int TotalRows = 100_000;
    private const int ActiveRows = TotalRows * 2 / 5;

    // The seed marker. The guard below keys on THIS, never on a row count: a collection sibling
    // (JobAdBrowseSortQueryPlanTests, SuggestUnionLatencyMeasurement) also owns job_ads and also
    // seeds a five-figure regime, and a count-only guard would happily measure their NARROW fixture
    // — the exact "tells you the opposite of the truth" failure this class is written against.
    private const string SeedMarker = "gradeplan-";

    // Row width — description padding, in repetitions of one Swedish sentence. Tune this if the
    // width floor below stops clearing; run Fixture_IsWideEnoughToPlanLikeDev and read its output
    // rather than trusting an arithmetic estimate, because whether the padding survives into the
    // heap or is compressed away depends on where the whole tuple lands relative to
    // TOAST_TUPLE_THRESHOLD, which the payload and the STORED search_vector also push on.
    private const int DescriptionRepeats = 21;

    // The floor the width claim is asserted against. It is a REGIME SEPARATOR, not a target: the
    // report's narrow probe was 76.6 B/row and inverted its own finding, dev measured 1 898 B/row,
    // and anything clearing this floor is unambiguously in the second regime. This fixture sits
    // between the two and therefore makes a sequential scan comparatively CHEAPER than dev's does —
    // which is the conservative direction for a pin whose whole content is "no sequential scan".
    private const int MinBytesPerRow = 1_000;

    // Facet vocabulary — see the docblock for why coarse is admissible here.
    private const int OccupationGroups = 20;
    private const int Regions = 10;
    private const int Municipalities = 50;
    private const int EmploymentTypes = 4;

    // The report's four measured union sizes, which is also the whole operating range: the detail
    // page's small set, the common list case, one criterion pinned at CriterionMatchingAdSetResolver
    // .MaxSetSize, and the accepted ceiling (20 criteria × 2 000).
    public static TheoryData<int> UnionSizes => [200, 600, 2_000, 40_000];

    [Theory]
    [MemberData(nameof(UnionSizes))]
    public async Task GradeQuery_ReachesRowsThroughAnIndex_AndNeverFullScansJobAds(int unionSize)
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var recorder = new CapturingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        await EnsureSeededAsync(db, ct);

        var ids = await UnionIdsAsync(db, unionSize, ct);
        ids.Count.ShouldBe(unionSize, "the fixture must be able to supply the whole union");

        var sut = NewSut(scope, db);

        // The REAL call. It executes, which is what makes the id array a real uuid[] parameter and
        // gives the non-degeneracy check below something to measure.
        recorder.Clear();
        var matching = await sut.FilterToMatchingAsync(RealisticProfile, ids, ct);

        // Anti-degeneracy, both directions. An empty match set would mean the fixture grades nothing
        // (so the grade predicate is doing no work and the plan describes a different query); a full
        // match set would mean it grades everything. Either way the plan below would not be the
        // production plan. Deliberately a BAND, not a number — the share is a property of the seed,
        // and a pinned count here would be a second, decaying claim.
        matching.Count.ShouldBeGreaterThan(
            0, "the fixture must grade SOME ad ≥Good, or the grade predicate is not being exercised");
        matching.Count.ShouldBeLessThan(
            ids.Count, "the fixture must grade some ad BELOW Good, or the predicate is vacuous");

        var captured = recorder.Captured.ShouldHaveSingleItem();
        captured.CommandText.ShouldContain(
            "job_ads",
            customMessage:
                "the captured statement must be the grade query itself, not some other command");

        var plan = await ExplainAsync(db, captured, ct);
        _output.WriteLine($"--- union {unionSize}, matched {matching.Count} ---");
        _output.WriteLine(plan);

        // POSITIVE half: the rows are reached through an index node. True of all four measured
        // shapes (pk bitmap, the ordered status Index Scan, Bitmap Heap Scan, Parallel Bitmap Heap
        // Scan) and false of every full-scan plan, without pinning WHICH of them runs.
        ReachesRowsThroughAnIndex(plan).ShouldBeTrue(
            "The grade query's plan carries no index node at all. Every shape the 2026-09-07 "
            + "measurement observed reaches job_ads through an index; a plan without one is not a "
            + "plan the accepted 240 ms figure was measured over."
            + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}");

        // NEGATIVE half: and never by full-scanning job_ads. This is the regression that would leave
        // every semantic test green while the accepted number stopped being true.
        plan.ShouldNotContain(
            SeqScanToken,
            Case.Insensitive,
            "The grade query has fallen to a sequential scan of job_ads. That is the plan flip ADR "
            + "0139's accepted budget overrun is conditioned against (Klas-beviljande 4, condition "
            + "(i)): the planner's estimate on the grade CASE is wrong by 3-4 orders of magnitude, "
            + "so it flips on small changes in statistics, and the accepted 240 ms then describes a "
            + "query that no longer runs. Note the pin is regime-dependent by construction — check "
            + "first that the fixture is still WIDE (Fixture_IsWideEnoughToPlanLikeDev) before "
            + "concluding production regressed."
            + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task SeqScanIsObservable_SoTheAbsenceAssertionCanFail()
    {
        // THE POSITIVE CONTROL, and it is not optional. Every assertion in the theory above is an
        // ABSENCE, and an absence passes for every reason there is — the EXPLAIN failed, the reader
        // returned nothing, the helper broke, the statement never ran, the table is empty. This test
        // drives a statement over the SAME table, in the SAME fixture, on the SAME connection,
        // through the SAME ExplainAsync helper, whose predicate is an expression no index in this
        // schema can serve, and asserts the token IS there. Delete this and the four pins above go
        // green against a broken instrument.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var recorder = new CapturingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        await EnsureSeededAsync(db, ct);

        // length(company_name) is not indexed and cannot be — so this plan MUST full-scan job_ads,
        // whatever the planner's mood. It is deliberately a statement over the SAME relation, so it
        // also proves the token appears in its table-qualified form for THIS table.
        var control = new CapturedCommand(
            "SELECT count(*) FROM job_ads WHERE length(company_name) = -1", []);

        var plan = await ExplainAsync(db, control, ct);
        _output.WriteLine("--- positive control ---");
        _output.WriteLine(plan);

        plan.ShouldContain(
            SeqScanToken + " on job_ads",
            Case.Insensitive,
            "The positive control did NOT produce a sequential scan of job_ads. The instrument is "
            + "therefore not capable of observing the token the theory above asserts the ABSENCE of, "
            + "and those four pins are currently vacuous — fix this before trusting them."
            + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task Fixture_IsWideEnoughToPlanLikeDev()
    {
        // The second way this pin could go quietly vacuous, and the one the measurement report warns
        // about in its own reproduction section: a narrow heap makes a sequential scan cheap, so a
        // narrow fixture would answer the opposite question while looking identical. Its first probe
        // was 76.6 B/row against dev's 1 898 B and inverted the finding. This asserts the storage
        // regime the four plan pins are read in, so a future edit to the seed cannot silently move
        // them into the regime that lies.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var recorder = new CapturingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        await EnsureSeededAsync(db, ct);

        var bytesPerRow = await db.Database
            .SqlQuery<long>(
                $"SELECT (pg_relation_size('job_ads') / greatest(count(*), 1))::bigint AS \"Value\" FROM job_ads")
            .SingleAsync(ct);

        var activeRows = await db.Database
            .SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM job_ads WHERE status = 'Active'")
            .SingleAsync(ct);

        _output.WriteLine($"--- fixture: {bytesPerRow} heap bytes/row, {activeRows} Active ---");

        bytesPerRow.ShouldBeGreaterThanOrEqualTo(
            MinBytesPerRow,
            $"The job_ads fixture is {bytesPerRow} heap bytes/row, against dev's measured 1 898. A "
            + "narrow heap makes a sequential scan cheap, so the four plan pins in this class would "
            + "be answering a different question about a different storage regime — the report's own "
            + "first probe (76.6 B/row) inverted its finding exactly this way.");

        activeRows.ShouldBe(
            ActiveRows,
            "The Active share drives what fraction of the table the 40 000-id ceiling union covers, "
            + "which is one of the few things the planner CAN estimate on this query.");
    }

    // ── the SUT, its profile, and the union ────────────────────────────────────────────────────

    /// <summary>
    /// The report's "realistic" profile SHAPE (a few occupation groups, a couple of regions, a few
    /// municipalities, a couple of employment types) against this fixture's vocabulary — not its
    /// measured share, which this fixture's coarser vocabulary does not carry. Both the ort and the
    /// employment dimension are STATED, which is what makes the grade CASE take its full branch set
    /// (gate → Related cap → RB1 floor → secondaries) rather than collapsing to the gate.
    /// </summary>
    private static readonly FullCandidateMatchProfile RealisticProfile =
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [.. Enumerable.Range(0, 5).Select(g => $"grp-{g:D2}")],
                PreferredRegionConceptIds: [.. Enumerable.Range(0, 4).Select(r => $"reg-{r:D2}")],
                PreferredEmploymentTypeConceptIds: [.. Enumerable.Range(0, 2).Select(e => $"emp-{e:D2}")],
                PreferredMunicipalityConceptIds: [.. Enumerable.Range(0, 4).Select(m => $"mun-{m:D2}")]),
            []);

    /// <summary>
    /// The real port over the recording context. Its two collaborators are unreachable from
    /// <c>FilterToMatchingAsync</c> (that method goes straight to <c>db.JobAds.FromSql</c>), but they
    /// are the REAL ones from the fixture's own DI graph rather than substitutes, so nothing about
    /// this construction can differ from production's.
    /// </summary>
    private static PerUserJobAdSearchQuery NewSut(IServiceScope scope, AppDbContext db)
    {
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        return new PerUserJobAdSearchQuery(db, expander, new JobAdSearchQuery(db, expander));
    }

    /// <summary>
    /// A uniform sample of the Active set. The seeded ids are <c>md5(marker || i)::uuid</c>, so
    /// ordering by id is uncorrelated with physical order — the same uniform-random id set the
    /// measurement used, and (as its own residual section says) broader than the clustered sets real
    /// criteria produce.
    /// </summary>
    private static async Task<IReadOnlyCollection<JobAdId>> UnionIdsAsync(
        AppDbContext db, int size, CancellationToken ct)
    {
        var ids = await db.Database
            .SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM job_ads WHERE status = 'Active' ORDER BY id LIMIT {size}")
            .ToListAsync(ct);

        return [.. ids.Select(id => new JobAdId(id))];
    }

    // ── the fixture ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TRUNCATE-and-own → bulk-seed dev's row counts at dev's row width → ANALYZE, once per
    /// container. The guard keys on this class's own seed marker rather than a row count, because
    /// two collection siblings also own job_ads at a five-figure size and a count-only guard would
    /// silently adopt their narrow regime.
    /// </summary>
    private static async Task EnsureSeededAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(900);

        var seeded = await db.Database
            .SqlQuery<long>(
                $"SELECT count(*)::bigint AS \"Value\" FROM job_ads WHERE external_id LIKE {SeedMarker + "%"}")
            .SingleAsync(ct);

        if (seeded == TotalRows)
            return;

        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        // The facet columns and the raw_payload they were read from are written TOGETHER and from the
        // same expressions, so each row is the shape SetSourcePayload produces rather than a
        // half-populated one no ingest writes (CLAUDE.md §5 Tests:). search_vector and
        // extracted_lexemes are STORED generated and are omitted.
        //
        // Two rows in five Active, interleaved on i rather than blocked, so the Active set is not
        // physically clustered — which would price an index path's heap fetches as sequential and
        // flatter the very plan under test (the collection siblings' discipline).
        var seed =
            "INSERT INTO job_ads ("
            + "id, title, company_name, description, url, source, external_source, external_id, "
            + "raw_payload, status, published_at, expires_at, created_at, remote, organization_number, "
            + "occupation_group_concept_id, region_concept_id, municipality_concept_id, "
            + "employment_type_concept_id) "
            + "SELECT "
            + $"md5('{SeedMarker}' || i)::uuid, "
            + "'Systemutvecklare ' || i, "
            + $"'Foretag ' || ((i::bigint * 7919) % {TotalRows}) || ' AB', "
            // WIDTH — the half a plan fixture gets wrong silently (see the docblock). One repeated
            // Swedish sentence rather than random text, so the STORED search_vector stays a handful
            // of lexemes instead of becoming the widest thing in the row.
            + "'Beskrivning ' || i || '. ' || repeat("
            + "'Arbetsuppgifter, kvalifikationer och villkor for tjansten. ', "
            + DescriptionRepeats + "), "
            + "'https://example.com/jobb/' || i, "
            + "'Platsbanken', 'Platsbanken', "
            + $"'{SeedMarker}' || i, "
            + "jsonb_build_object("
            + $"'id', '{SeedMarker}' || i, "
            + "'employer', jsonb_build_object("
            + $"'name', 'Foretag ' || ((i::bigint * 7919) % {TotalRows}) || ' AB', "
            + "'organization_number', lpad((5520000000 + i)::text, 10, '0')), "
            + "'occupation_group', jsonb_build_object("
            + $"'concept_id', 'grp-' || lpad(((i::bigint * 7919) % {OccupationGroups})::text, 2, '0')), "
            + "'workplace_address', jsonb_build_object("
            + $"'region_concept_id', 'reg-' || lpad(((i::bigint * 104729) % {Regions})::text, 2, '0'), "
            + $"'municipality_concept_id', 'mun-' || lpad(((i::bigint * 15485863) % {Municipalities})::text, 2, '0')), "
            + "'employment_type', jsonb_build_object("
            + $"'concept_id', 'emp-' || lpad(((i::bigint * 31) % {EmploymentTypes})::text, 2, '0'))), "
            + "CASE WHEN i % 5 < 2 THEN 'Active' ELSE 'Archived' END, "
            + $"now() - (((i::bigint * 7919) % {TotalRows}) || ' minutes')::interval, "
            + "now() + interval '60 days', now(), false, "
            + "lpad((5520000000 + i)::text, 10, '0'), "
            + $"'grp-' || lpad(((i::bigint * 7919) % {OccupationGroups})::text, 2, '0'), "
            + $"'reg-' || lpad(((i::bigint * 104729) % {Regions})::text, 2, '0'), "
            + $"'mun-' || lpad(((i::bigint * 15485863) % {Municipalities})::text, 2, '0'), "
            + $"'emp-' || lpad(((i::bigint * 31) % {EmploymentTypes})::text, 2, '0') "
            + $"FROM generate_series(0, {TotalRows - 1}) AS i;";
        await db.Database.ExecuteSqlRawAsync(seed, ct);

        // MANDATORY, not hygiene. TRUNCATE wipes the statistics, and without them the planner falls
        // back on default selectivity constants — which is precisely the regime this pin must NOT be
        // read in, because the whole finding is about what the planner does with (bad) real estimates.
        await db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
    }

    /// <summary>
    /// A fresh <see cref="AppDbContext"/> against the fixture's own database, carrying only the
    /// capturing interceptor — the <c>JobAdCountBitmapPlanHygieneTests</c> idiom, so the SQL EF emits
    /// is observable without touching shared host wiring. job_ads carries no DEK-encrypted column, so
    /// the field-encryption interceptor pair is not needed on this path.
    /// </summary>
    private static AppDbContext NewRecordingContext(
        IServiceScope scope, CapturingCommandInterceptor recorder)
    {
        var connectionString = scope.ServiceProvider
            .GetRequiredService<AppDbContext>().Database.GetConnectionString();

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(recorder)
            .Options);
    }

    // "Seq Scan" covers "Parallel Seq Scan" too — a parallel full scan is the same regression with
    // more workers, and the report saw exactly that shape on the rejected alternative composition.
    private const string SeqScanToken = "Seq Scan";

    private static bool ReachesRowsThroughAnIndex(string plan) =>
        plan.Contains("Index Scan", StringComparison.OrdinalIgnoreCase)
        || plan.Contains("Index Only Scan", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// EXPLAIN (never EXPLAIN ANALYZE — this is a PLANNER assertion; row-level truth is
    /// <c>FilterToMatchingTests</c>'s job) of the command EF actually sent, with its parameters
    /// re-bound at their captured types. The parameters are cloned on the way in so the captured set
    /// stays reusable, and cloned again here so this command owns its own.
    /// </summary>
    private static async Task<string> ExplainAsync(
        AppDbContext db, CapturedCommand captured, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXPLAIN " + captured.CommandText;
        foreach (var parameter in captured.Parameters)
            cmd.Parameters.Add(parameter.Clone());

        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(reader.GetString(0));

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record CapturedCommand(
        string CommandText, IReadOnlyList<NpgsqlParameter> Parameters);

    /// <summary>
    /// Captures the command text AND its parameters (cloned at intercept time, before EF disposes
    /// the command) so the EXPLAIN below runs the statement production emitted rather than a
    /// re-typing of it. Only the reader path is intercepted: <c>FilterToMatchingAsync</c> ends in
    /// <c>ToListAsync</c>.
    /// </summary>
    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        // EF executes a context's commands sequentially and each test uses one context on one
        // thread, so a plain list needs no synchronization here.
        private readonly List<CapturedCommand> _captured = [];

        public IReadOnlyList<CapturedCommand> Captured => _captured;

        public void Clear() => _captured.Clear();

        private void Capture(DbCommand command) =>
            _captured.Add(new CapturedCommand(
                command.CommandText,
                [.. command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone())]));

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
