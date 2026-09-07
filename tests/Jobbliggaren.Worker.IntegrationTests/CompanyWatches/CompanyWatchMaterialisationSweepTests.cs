using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #1681 clause (ii) — the reconciling sweep: a criterion the user just created or whose predicate
/// she just edited gets its numbers within one tick instead of within a day.
///
/// <para>
/// <b>Every state these tests assert against is produced by a production path</b> (AGENTS.md §5
/// <c>Tests:</c>). Criteria come from <c>CompanyWatchCriterion.Create</c>, edits from
/// <c>UpdateCriteria</c>, renames from <c>Rename</c>, and every materialisation row is written by the
/// materialiser itself — no hand-seeded fingerprint, no hand-written state row. That matters most for
/// the rename test, whose whole claim is about what production does NOT do.
/// </para>
///
/// <para>
/// The clock is injected per run so <c>materialised_at</c> and <c>updated_at</c> can be ordered
/// deliberately. That ordering IS the prefilter, so a test that let the real clock decide it would be
/// measuring timing rather than behaviour.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchMaterialisationSweepTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset TCreate = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TFirstRun = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TEdit = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TSecondRun = new(2026, 9, 7, 11, 0, 0, TimeSpan.Zero);

    private const string SniIt = "62010";

    [Fact]
    public async Task Sweep_MaterialisesACriterionThatHasNoStateRowYet()
    {
        // The create case, and the defect this whole PR exists to close: before it, the only caller
        // of the materialiser was the nightly job, so a criterion created at 05:31 had no numbers
        // until 05:30 the next morning.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(1, ct);
        var id = await CreateCriterionAsync(Kommun(1), ct);

        var result = await SweepAsync(TFirstRun, ct);

        result.CriteriaSeen.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(1);
        (await ReadMemberCountAsync(id, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_ReplacesTheMemberSet_WhenThePredicateChanged()
    {
        // security-auditor Major 5c: an edit must REPLACE the set, never supplement it — a criterion
        // narrowed away from a company must stop counting that company. The two kommuner are disjoint,
        // so a supplementing implementation leaves 2 members and this fails on the count alone; the
        // per-org.nr assertion then names WHICH company wrongly survived.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(2, ct);
        var id = await CreateCriterionAsync(Kommun(1), ct);

        await SweepAsync(TFirstRun, ct);
        (await ReadMembersAsync(id, ct)).ShouldBe([OrgNr(1)]);

        await EditCriterionAsync(id, Kommun(2), ct);
        var result = await SweepAsync(TSecondRun, ct);

        result.CriteriaSeen.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(1);
        (await ReadMembersAsync(id, ct)).ShouldBe([OrgNr(2)]);
    }

    [Fact]
    public async Task Sweep_DoesNotResolveTheRegister_WhenOnlyTheLabelChanged()
    {
        // THE BOUND ADR 0139 states: "a rename must not cost a register resolution". `Rename` bumps
        // UpdatedAt exactly as UpdateCriteria does — verified in source and the reason
        // CriteriaFingerprint exists at all — so the renamed criterion IS selected by the prefilter
        // and must then be dismissed by the fingerprint.
        //
        // The oracle is materialised_at, and it is chosen because production writes it on EVERY
        // ReplaceAsync path including the TooBroad refusal. So an unchanged stamp is positive proof
        // that no resolution happened, not merely that the answer came out the same — which a
        // member-count assertion alone could not distinguish.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(1, ct);
        var id = await CreateCriterionAsync(Kommun(1), ct);

        await SweepAsync(TFirstRun, ct);
        var stampAfterFirstRun = await ReadMaterialisedAtAsync(id, ct);
        stampAfterFirstRun.ShouldNotBeNull();

        await RenameCriterionAsync(id, "Ett nytt namn", ct);

        // POSITIVE CONTROL, and without it this whole test is a reading a no-op would also produce:
        // every assertion below holds identically if the prefilter never selected the row at all.
        // What is being claimed is narrower and stronger — the criterion IS a candidate, and the
        // FINGERPRINT is what dismisses it.
        (await SelectCandidatesForSweepAsync(ct)).ShouldContain(id.Value,
            "omdöpningen måste bumpa updated_at och därmed VÄLJAS av prefiltret — annars mäter "
            + "testet nedan att ingenting hände, inte att fingeravtrycket avfärdade något");

        var result = await SweepAsync(TSecondRun, ct);

        result.CriteriaSeen.ShouldBe(0,
            "en omdöpning får inte räknas som ett upplöst kriterium — fingeravtrycket är oförändrat");
        result.CriteriaMaterialised.ShouldBe(0);
        (await ReadMaterialisedAtAsync(id, ct)).ShouldBe(stampAfterFirstRun,
            "stämpeln skrivs på VARJE ReplaceAsync-väg, så en oförändrad stämpel bevisar att ingen "
            + "registerupplösning skedde");
        (await ReadMemberCountAsync(id, ct)).ShouldBe(1,
            "och medlemsmängden får inte raderas av en omdöpning heller");
    }

    [Fact]
    public async Task Sweep_TakesAtMostOneBatch_AndTheNextTickTakesTheRest()
    {
        // Two properties in one run, and they are the pair that makes the capped, offset-free loop
        // safe. The cap must BITE (a tick may not run away with an unbounded corpus), and the sweep
        // must still be TOTAL across ticks (nothing may be permanently skipped). An implementation
        // that copied the nightly walk's OFFSET paging fails the second half: every processed
        // criterion drops out of the predicate, so an advancing offset would step past the survivors.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(4, ct);
        var ids = new List<CompanyWatchCriterionId>();
        for (var i = 1; i <= 4; i++)
            ids.Add(await CreateCriterionAsync(Kommun(i), ct));

        var first = await SweepAsync(TFirstRun, ct, batchSize: 2);
        first.CriteriaSeen.ShouldBe(2, "taket måste bita");

        var second = await SweepAsync(TSecondRun, ct, batchSize: 2);
        second.CriteriaSeen.ShouldBe(2);

        // Totality: every criterion ended up materialised, exactly once each.
        foreach (var id in ids)
            (await ReadMemberCountAsync(id, ct)).ShouldBe(1);

        var third = await SweepAsync(TSecondRun, ct, batchSize: 2);
        third.CriteriaSeen.ShouldBe(0, "och sedan finns inget kvar att göra");
    }

    [Fact]
    public async Task Sweep_PrefersACriterionWithNoStateRow_OverOneWhosePredicateChanged()
    {
        // The ordering that makes the cap safe against starvation. A user who just CREATED a watch is
        // looking at a surface that says it does not know yet; a user who EDITED one is looking at
        // the same honest answer but had numbers a moment ago. Under a full backlog the create goes
        // first. Cap of 1 makes the choice observable — with the ORDER BY's first term dropped, the
        // edited criterion (newer updated_at) would win instead.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(2, ct);

        var edited = await CreateCriterionAsync(Kommun(1), ct);
        await SweepAsync(TFirstRun, ct);
        await EditCriterionAsync(edited, Kommun(2), ct);

        var created = await CreateCriterionAsync(Kommun(2), ct);

        await SweepAsync(TSecondRun, ct, batchSize: 1);

        (await ReadMaterialisedAtAsync(created, ct)).ShouldNotBeNull(
            "det nyskapade kriteriet ska gå först — det har inget tal alls att visa");
        (await ReadMaterialisedAtAsync(edited, ct)).ShouldBe(TFirstRun,
            "det redigerade kriteriet väntar till nästa tick och behåller sin gamla stämpel");
    }

    [Fact]
    public async Task Sweep_LeavesAnOverAgeButUnchangedCriterion_ToTheNightlyRun()
    {
        // The read path has THREE NotMaterialised triggers: absent row, fingerprint mismatch, and
        // over-age (MaxReadAgeHours). The sweep deliberately answers only the first two. Sweeping the
        // third would retry a permanently broken criterion every minute forever with no new
        // information between attempts — so an untouched criterion, however old its stamp, is not a
        // candidate.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(1, ct);
        var id = await CreateCriterionAsync(Kommun(1), ct);

        await SweepAsync(TFirstRun, ct);

        // A week later, with nothing edited in between.
        var result = await SweepAsync(TFirstRun.AddDays(7), ct);

        result.CriteriaSeen.ShouldBe(0);
        (await ReadMaterialisedAtAsync(id, ct)).ShouldBe(TFirstRun);
    }

    [Fact]
    public async Task Sweep_WritesNothing_WhenDisabled()
    {
        // The kill-switch degrades HONESTLY: a criterion created while the sweep is off simply has no
        // state row, which the read path renders as "not known yet" — never as a zero.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(1, ct);
        var id = await CreateCriterionAsync(Kommun(1), ct);

        var result = await SweepAsync(TFirstRun, ct, enabled: false);

        result.CriteriaSeen.ShouldBe(0);
        (await ReadMaterialisedAtAsync(id, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Sweep_DoesNotAnalyse_OnAnEmptyTick_ButDoesWhenItWrites()
    {
        // §3.6 says a bulk-load path ANALYZEs the table it loaded. The sweep is a bulk-load path only
        // on the ticks that actually load something, and in steady state it loads nothing — every
        // minute, forever. ANALYZE on a table no statement touched is work for no plan, so the call
        // is gated on the run having written. Both arms are asserted because gating it wrongly in
        // either direction is silent: never analysing degrades the plan the breadth bound rests on,
        // always analysing spends the cost once a minute with nothing to show for it.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(1, ct);
        await CreateCriterionAsync(Kommun(1), ct);

        var beforeWrite = await ReadAnalyzeCountsAsync(ct);
        var wrote = await SweepAsync(TFirstRun, ct);
        wrote.CriteriaMaterialised.ShouldBe(1);
        var afterWrite = await ReadAnalyzeCountsAsync(ct);

        afterWrite.Members.ShouldBe(beforeWrite.Members + 1);
        afterWrite.Materialisations.ShouldBe(beforeWrite.Materialisations + 1);

        var idle = await SweepAsync(TSecondRun, ct);
        idle.CriteriaSeen.ShouldBe(0);
        var afterIdle = await ReadAnalyzeCountsAsync(ct);

        afterIdle.Members.ShouldBe(afterWrite.Members, "en tom tick ska inte ANALYZE:a någonting");
        afterIdle.Materialisations.ShouldBe(afterWrite.Materialisations);
    }

    [Fact]
    public async Task AfterASweep_TheReadPathStopsAnsweringNotMaterialised_AndCountsTheNewPredicate()
    {
        // THE END-TO-END CLAIM, and the one the rest of this suite only approaches. Everything above
        // measures what the sweep WROTE; this measures that the read path BELIEVES it — which is a
        // different fact, because the gate compares three things (state, fingerprint, age) and the
        // sweep controls only two.
        //
        // The clock is the graph's real one here, deliberately: materialised_at must land inside
        // MaxReadAgeHours of the reader's own now, and a fixed clock would make this test start
        // failing 72 hours after the date it was written — a pin that decays into a false red.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(2, ct);
        await SeedActiveAdsAsync(OrgNr(2), 3, ct);

        // The user creates the watch pointing at kommun 1, where nothing is advertising.
        var id = await CreateCriterionAsync(Kommun(1), ct);
        await SweepWithGraphClockAsync(ct);
        (await CountAsync(id, Kommun(1), ct)).ShouldBe(MaterialisedAdCount.Counted(0, saturated: false));

        // She then edits it to kommun 2, where three ads are live. Before this PR that edit showed
        // "vet inte" until the next nightly run; the point of the sweep is that it does not.
        await EditCriterionAsync(id, Kommun(2), ct, at: DateTimeOffset.UtcNow);

        // The read path answers about the CURRENT predicate, so it is asked with the current
        // fingerprint. Until the sweep runs, the stored fingerprint is the old one and the gate
        // refuses — which is the honest pre-state this PR shortens rather than removes.
        (await CountAsync(id, Kommun(2), ct)).ShouldBe(MaterialisedAdCount.NotMaterialised);

        await SweepWithGraphClockAsync(ct);

        (await CountAsync(id, Kommun(2), ct)).ShouldBe(
            MaterialisedAdCount.Counted(3, saturated: false),
            "efter svepet ska läsvägen ge ett EXAKT tal för det nya predikatet, inte NotMaterialised");
    }

    [Fact]
    public async Task Sweep_TakesTheNewestEditFirst_WhenEveryCandidateAlreadyHasAStateRow()
    {
        // The ORDER BY's SECOND term, which no other test reaches (test-writer, 2026-09-07). The
        // preference test above puts its two candidates in DIFFERENT groups for
        // (m.criterion_id IS NOT NULL), so the first term decides alone; the batch test gives its
        // four criteria an IDENTICAL updated_at, so their order is arbitrary. Flipping DESC to ASC
        // therefore survived both.
        //
        // The term is load-bearing: a candidate the fingerprint dismisses is re-selected every tick
        // until the nightly run (#1701), so those rows are STANDING. Under ASC the oldest of them
        // would fill the batch permanently and a fresh edit would never be reached.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(3, ct);

        var older = await CreateCriterionAsync(Kommun(1), ct);
        var newer = await CreateCriterionAsync(Kommun(2), ct);
        await SweepAsync(TFirstRun, ct);          // both now carry a state row

        await EditCriterionAsync(older, Kommun(3), ct, at: TEdit);
        await EditCriterionAsync(newer, Kommun(3), ct, at: TEdit.AddMinutes(1));

        await SweepAsync(TSecondRun, ct, batchSize: 1);

        (await ReadMaterialisedAtAsync(newer, ct)).ShouldBe(TSecondRun,
            "den senaste redigeringen går först");
        (await ReadMaterialisedAtAsync(older, ct)).ShouldBe(TFirstRun,
            "den äldre väntar till nästa tick och behåller sin gamla stämpel");
    }

    [Fact]
    public async Task Sweep_DoesNotThrow_WhenEveryCandidateFails_AndCountsTheFailure()
    {
        // TESTPREMISS (AGENTS.md §5 `Tests:`): an EMPTY SNI axis is a state NO path in src/ produces
        // — CompanyWatchCriteriaSpec.Create forbids it and BindPredicate fails loudly on it. It is
        // DECLARED UNREACHABLE, and the assertion is therefore limited to how the run degrades, never
        // to what production does. Same actor and same declaration as the nightly suite's
        // Materialise_WhenEveryCriterionFails_Throws.
        //
        // The nightly run THROWS here; this one deliberately does NOT, and the asymmetry is the
        // decision (senior-cto-advisor 2026-09-07, reversing his own earlier bind). The throw exists
        // so Hangfire's retry can fire, and the sweep carries AutomaticRetry(Attempts = 0) — so it
        // would add no information while manufacturing a FAILED job for, among other things, a user
        // deleting her own watch mid-tick (an FK cascade failure). Without this pin, restoring the
        // throw would be green everywhere.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        var corrupt = await CreateCriterionAsync(Kommun(1), ct);
        await CorruptSniAxisAsync(corrupt, ct);

        var result = await SweepAsync(TFirstRun, ct);

        result.CriteriaFailed.ShouldBe(1, "felet räknas — det sväljs aldrig");
        result.CriteriaMaterialised.ShouldBe(0);
        (await ReadMaterialisedAtAsync(corrupt, ct)).ShouldBeNull(
            "det felande kriteriet behåller sitt tidigare tillstånd — här: inget");
    }

    [Fact]
    public async Task Sweep_RefusesATooBroadCriterion_AndNarrowingItGivesANumberWithinOneTick()
    {
        // TooBroad reached through the SWEEP, which no other test does — and with it the ANALYZE
        // gate's second arm (`|| tally.TooBroad > 0`), whose removal survived every other test
        // (test-writer mutation 5d, 2026-09-07). A refusal writes a state row, so it IS a bulk-load
        // path even though it writes no members.
        //
        // The second half is clause (ii)'s own promise, measured on the state the options docblock
        // names: "a user who narrowed a too-broad watch would keep being told it is too broad until
        // the next run" — here it takes one tick.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterInKommunAsync(
            Kommun(1), CompanyWatchCriterionMember.MaxPerCriterion + 1, ct);
        await SeedRegisterInKommunAsync(Kommun(2), 1, ct, orgNrOffset: 500_000);

        var id = await CreateCriterionAsync(Kommun(1), ct);

        var beforeRefusal = await ReadAnalyzeCountsAsync(ct);
        var refused = await SweepAsync(TFirstRun, ct);
        var afterRefusal = await ReadAnalyzeCountsAsync(ct);

        refused.CriteriaTooBroad.ShouldBe(1);
        refused.CriteriaMaterialised.ShouldBe(0);
        (await ReadMemberCountAsync(id, ct)).ShouldBe(0, "en vägran lagrar ingen mängd");
        afterRefusal.Members.ShouldBe(beforeRefusal.Members + 1,
            "en tick som SKREV en vägran är en bulk-load-väg och ska ANALYZE:a");

        await EditCriterionAsync(id, Kommun(2), ct, at: TEdit);
        var narrowed = await SweepAsync(TSecondRun, ct);

        narrowed.CriteriaMaterialised.ShouldBe(1);
        (await ReadMemberCountAsync(id, ct)).ShouldBe(1,
            "att smalna en för bred bevakning ska ge ett tal inom EN tick, inte vid nästa dygn");
    }

    // ----- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// The production orchestrator out of the real graph, with only the clock and the two knobs under
    /// test overridden — same construction as <c>CompanyWatchMaterialisationSeamTests</c>.
    /// </summary>
    private async Task<CompanyWatchCriterionMaterialisationResult> SweepAsync(
        DateTimeOffset now, CancellationToken ct, int batchSize = 50, bool enabled = true)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = new CompanyWatchCriterionMaterialiser(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>(),
            new FixedClock(now),
            Options.Create(new CompanyWatchMaterialisationOptions
            {
                SweepBatchSize = batchSize,
                Enabled = enabled,
            }),
            NullLogger<CompanyWatchCriterionMaterialiser>.Instance);

        return await materialiser.MaterialiseChangedAsync(ct);
    }

    /// <summary>The sweep on the graph's OWN clock, for the one test whose assertion depends on
    /// materialised_at being recent relative to the reader.</summary>
    private async Task<CompanyWatchCriterionMaterialisationResult> SweepWithGraphClockAsync(
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = new CompanyWatchCriterionMaterialiser(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>(),
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            Options.Create(new CompanyWatchMaterialisationOptions()),
            NullLogger<CompanyWatchCriterionMaterialiser>.Instance);

        return await materialiser.MaterialiseChangedAsync(ct);
    }

    /// <summary>The read path, asked about the predicate the criterion carries RIGHT NOW.</summary>
    private async Task<MaterialisedAdCount> CountAsync(
        CompanyWatchCriterionId id, string kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var browse = scope.ServiceProvider.GetRequiredService<ICompanyWatchBrowseQuery>();

        var spec = CompanyWatchCriteriaSpec.Create([SniIt], [kommun]);
        spec.IsSuccess.ShouldBeTrue();

        return await browse.CountActiveAdsAsync(
            id, CriteriaFingerprint.Of(spec.Value), ceiling: 10_000, ct);
    }

    /// <summary>
    /// Active ads carrying an org.nr — the same column and the same shape the Platsbanken ingest
    /// writes through <c>JobAd.Import</c>. Bulk-inserted for convenience; the state asserted against
    /// is state <c>src/</c> produces (AGENTS.md §5 <c>Tests:</c>).
    /// </summary>
    private async Task SeedActiveAdsAsync(string orgNr, int count, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO job_ads (
                id, title, company_name, description, url, source, external_source, external_id,
                raw_payload, status, published_at, expires_at, created_at, remote, organization_number)
            SELECT gen_random_uuid(), 'Roll ' || i, 'Bolag', 'beskrivning',
                   'https://example.com/jobs/' || i, 'Platsbanken', 'Platsbanken', 'sweep-ext-' || i,
                   jsonb_build_object(), 'Active', now() - (i || ' days')::interval,
                   now() + interval '60 days', now(), false, {1}
            FROM generate_series(1, {0}) AS i;
            """,
            [count, orgNr], ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_watch_criteria;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_register;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_ads WHERE external_id LIKE 'sweep-ext-%';", ct);
    }

    private async Task SeedRegisterAsync(int count, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT '558' || lpad(i::text, 7, '0'), 'Bolag ' || i, lpad(i::text, 4, '0'), NULL,
                   ARRAY[{1}], false, '1', 'Active', now(), now()
            FROM generate_series(1, {0}) i;
            """,
            [count, SniIt], ct);
    }

    /// <summary>The candidate set the sweep would take this tick, read through the production query.</summary>
    private async Task<IReadOnlyList<Guid>> SelectCandidatesForSweepAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>();
        var rows = await store.SelectStaleCriterionIdsAsync(500, ct);
        return [.. rows.Select(r => r.Id)];
    }

    /// <summary>
    /// N ACTIVE legal-entity companies, all in ONE kommun, all carrying the same SNI code.
    /// <paramref name="orgNrOffset"/> keeps two calls from generating the SAME org.nr range — without
    /// it the second call's rows collide on the primary key and are silently dropped by
    /// ON CONFLICT DO NOTHING, which reads as "the criterion matched nothing" (measured 2026-09-07).
    /// </summary>
    private async Task SeedRegisterInKommunAsync(
        string kommun, int count, CancellationToken ct, int orgNrOffset = 0)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT '559' || lpad((i + {3})::text, 7, '0'), 'Bolag ' || i, {1}, NULL,
                   ARRAY[{2}], false, '1', 'Active', now(), now()
            FROM generate_series(1, {0}) i
            ON CONFLICT (organization_number) DO NOTHING;
            """,
            [count, kommun, SniIt, orgNrOffset], ct);
    }

    /// <summary>
    /// Empties the criterion's SNI axis directly in Postgres. See
    /// <see cref="Sweep_DoesNotThrow_WhenEveryCandidateFails_AndCountsTheFailure"/> for why this
    /// state is declared unreachable and what may therefore be asserted about it.
    /// </summary>
    private async Task CorruptSniAxisAsync(CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE company_watch_criteria SET sni_codes = ARRAY[]::text[] WHERE id = {0};",
            [id.Value], ct);
    }

    private static string Kommun(int i) =>
        i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

    private static string OrgNr(int i) =>
        "558" + i.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<CompanyWatchCriterionId> CreateCriterionAsync(
        string kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var spec = CompanyWatchCriteriaSpec.Create([SniIt], [kommun]);
        spec.IsSuccess.ShouldBeTrue("seed: specen måste vara giltig");

        var criterion = CompanyWatchCriterion.Create(
            Guid.NewGuid(), spec.Value, null, new FixedClock(TCreate));
        criterion.IsSuccess.ShouldBeTrue("seed: kriteriet måste kunna skapas");

        db.CompanyWatchCriteria.Add(criterion.Value);
        await db.SaveChangesAsync(ct);
        return criterion.Value.Id;
    }

    /// <summary>A PREDICATE change, through the aggregate's own transition — never a raw UPDATE.</summary>
    private async Task EditCriterionAsync(
        CompanyWatchCriterionId id, string kommun, CancellationToken ct, DateTimeOffset? at = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, ct);
        var spec = CompanyWatchCriteriaSpec.Create([SniIt], [kommun]);
        spec.IsSuccess.ShouldBeTrue("seed: den nya specen måste vara giltig");

        // The edit's stamp must land AFTER the preceding run's materialised_at, because that ordering
        // IS the prefilter. Tests that pin the fixed-clock sequence take the default; the one test
        // that runs the sweep on the graph's clock must stamp on the same timeline or the edit would
        // look older than the materialisation it is meant to invalidate.
        criterion.UpdateCriteria(spec.Value, new FixedClock(at ?? TEdit)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A LABEL change, through <c>Rename</c> — the actor whose UpdatedAt bump is the whole
    /// reason the prefilter is a superset and the fingerprint is the test.</summary>
    private async Task RenameCriterionAsync(
        CompanyWatchCriterionId id, string label, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, ct);
        criterion.Rename(label, new FixedClock(TEdit)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> ReadMemberCountAsync(CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM company_watch_criterion_members "
                + "WHERE criterion_id = {0};",
                id.Value)
            .ToListAsync(ct);
        return rows[0];
    }

    private async Task<IReadOnlyList<string>> ReadMembersAsync(
        CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQueryRaw<string>(
                "SELECT organization_number AS \"Value\" FROM company_watch_criterion_members "
                + "WHERE criterion_id = {0} ORDER BY organization_number;",
                id.Value)
            .ToListAsync(ct);
    }

    private async Task<DateTimeOffset?> ReadMaterialisedAtAsync(
        CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<DateTimeOffset>(
                "SELECT materialised_at AS \"Value\" FROM company_watch_criterion_materialisations "
                + "WHERE criterion_id = {0};",
                id.Value)
            .ToListAsync(ct);
        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<(long Members, long Materialisations)> ReadAnalyzeCountsAsync(
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<AnalyzeRow>(
                """
                SELECT relname, analyze_count
                FROM pg_stat_user_tables
                WHERE relname IN ('company_watch_criterion_members',
                                  'company_watch_criterion_materialisations');
                """)
            .ToListAsync(ct);

        return (
            rows.SingleOrDefault(r => r.Relname == "company_watch_criterion_members")?.AnalyzeCount ?? 0,
            rows.SingleOrDefault(r => r.Relname == "company_watch_criterion_materialisations")?.AnalyzeCount ?? 0);
    }

    private sealed record AnalyzeRow(string Relname, long AnalyzeCount);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
