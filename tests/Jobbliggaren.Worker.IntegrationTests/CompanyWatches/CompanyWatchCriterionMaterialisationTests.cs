using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #1681 (ADR 0139) — Testcontainers tests for the criterion-membership materialisation against REAL
/// Postgres. Deliberately not InMemory: every property under test is one InMemory cannot see — the
/// <c>text[]</c> array-overlap predicate, the <c>LIMIT</c> that carries the breadth gate, the FK
/// cascade, and the two-table transaction.
///
/// <para>
/// <b>Test premises follow CLAUDE.md §5 <c>Tests:</c>.</b> Every fixture row here is one production
/// itself produces: register rows are written by the SCB upsert, criteria by
/// <c>CreateCompanyWatchCriterionCommandHandler</c> through the same
/// <c>CompanyWatchCriterion.Create</c> this suite calls, and member/state rows only ever by the
/// production materialiser under test — this suite never hand-writes one. The single exception is
/// declared where it occurs: <see cref="Materialise_DropsPersonnummerShapedCandidates_AndCountsThem"/>
/// seeds a personnummer-shaped REGISTER row, which no path in <c>src/</c> produces because
/// <c>ScbLegalEntityFilter</c> drops it at ingest. That test names the actor and asserts the guard's
/// own predicate admits the state, per the §5 rule — see its own comment.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchCriterionMaterialisationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 5, 30, 0, TimeSpan.Zero);

    private const string SniIt = "62010";
    private const string SniBygg = "41200";
    private const string KommunStockholm = "0180";
    private const string KommunGoteborg = "1480";

    [Fact]
    public async Task Materialise_WritesExactlyTheMatchingActiveCompanies_AndAnHonestState()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // Three companies the criterion matches, one it does not (wrong kommun), and one it would
        // match but for its status — the M-D6 case, which is the whole reason this job replaced a
        // read-path predicate (security-auditor Major 3).
        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000003", KommunGoteborg, [SniIt], CompanyRegisterStatus.Active),
            ("5560000004", "0181", [SniIt], CompanyRegisterStatus.Active),
            ("5560000005", KommunStockholm, [SniIt], CompanyRegisterStatus.Deregistered));

        var criterionId = await SeedCriterionAsync(
            Guid.NewGuid(), [SniIt], [KommunStockholm, KommunGoteborg], ct);

        var result = await RunAsync(ct);

        result.CriteriaSeen.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(1);
        result.CriteriaTooBroad.ShouldBe(0);
        result.MembersWritten.ShouldBe(3);

        (await ReadMembersAsync(criterionId, ct))
            .ShouldBe(["5560000001", "5560000002", "5560000003"], ignoreOrder: true);

        var state = await ReadStateAsync(criterionId, ct);
        state.ShouldNotBeNull();
        state.Parsed.ShouldBe(MaterialisationState.Materialised);
        state.MemberCount.ShouldBe(3);
    }

    [Fact]
    public async Task Materialise_RemovesADeregisteredCompany_OnTheNextRun_TheMD6Replacement()
    {
        // security-auditor Major 3, the named replacement for DPIA M-D6. The mitigation used to be
        // structural on the READ path (CompanyWatchBrowseQuery's positive-polarity status = @status);
        // materialisation takes that predicate off the read path, so the enforcement point moves here.
        // What makes it structural again is that every run REBUILDS from status = 'Active' — there is
        // no "remove the dead ones" step that could be omitted. This test is what proves that, and it
        // would go red if the job ever became a delta rather than a full recompute.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);
        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(2);

        // SCB de-registers one of them. The actor is the production deregister sweep
        // (ScbCompanyRegisterStore.DeregisterMissingAsync), which flips status and never deletes —
        // this update is that transition, applied directly because the sweep needs a whole SCB run to
        // drive it.
        await DeregisterAsync("5560000002", ct);

        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).ShouldBe(["5560000001"]);
        (await ReadStateAsync(criterionId, ct))!.MemberCount.ShouldBe(1);
    }

    [Fact]
    public async Task Materialise_RefusesACriterionPastTheBreadthGate_StoringNoMembers()
    {
        // The breadth gate, at its exact boundary and on the refusing side. MaxPerCriterion + 1
        // matching companies must produce ZERO stored members and a TooBroad state — never a
        // truncated set, which would make every count derived from it a floor wearing a magnitude's
        // clothes (ADR 0120 clause 5), and never an empty Materialised state, which the read side is
        // entitled to render as an honest zero.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion + 1, KommunStockholm, SniIt, ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        var result = await RunAsync(ct);

        result.CriteriaTooBroad.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(0);
        result.MembersWritten.ShouldBe(0);

        (await ReadMembersAsync(criterionId, ct)).ShouldBeEmpty();

        var state = await ReadStateAsync(criterionId, ct);
        state.ShouldNotBeNull();
        state.Parsed.ShouldBe(MaterialisationState.TooBroad);
        state.MemberCount.ShouldBe(0);
    }

    [Fact]
    public async Task Materialise_AcceptsACriterionExactlyAtTheBreadthGate()
    {
        // The admitting side of the SAME boundary. Without this, a mutant changing the refusal from
        // "> MaxPerCriterion" to ">= MaxPerCriterion" — i.e. refusing a criterion that legitimately
        // fits — stays green, and the product silently gets stricter than its own documented bound.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion, KommunStockholm, SniIt, ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        var result = await RunAsync(ct);

        result.CriteriaMaterialised.ShouldBe(1);
        result.CriteriaTooBroad.ShouldBe(0);
        (await ReadMembersAsync(criterionId, ct)).Count
            .ShouldBe(CompanyWatchCriterionMember.MaxPerCriterion);
        (await ReadStateAsync(criterionId, ct))!.Parsed.ShouldBe(MaterialisationState.Materialised);
    }

    [Fact]
    public async Task Materialise_AfterTheCriterionIsNarrowed_DeletesTheOldSet_NeverSupplementsIt()
    {
        // security-auditor Major 5(c), verbatim: "criterion EDITING must DELETE the old set, not
        // supplement it, or stale members count companies she no longer watches (Art. 5(1)(d) +
        // 5(1)(e))". The edit goes through the aggregate's own UpdateCriteria — the production path —
        // so this measures the real transition rather than a hand-built one.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000002", KommunGoteborg, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(
            Guid.NewGuid(), [SniIt], [KommunStockholm, KommunGoteborg], ct);

        await RunAsync(ct);
        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(2);

        await NarrowCriterionAsync(criterionId, [SniIt], [KommunStockholm], ct);
        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).ShouldBe(["5560000001"],
            "den gamla mängden ska RADERAS, inte kompletteras — annars räknas bolag hon inte längre "
            + "bevakar");
    }

    [Fact]
    public async Task Materialise_AfterTheCriterionIsWidenedPastTheGate_DeletesTheMembersItHad()
    {
        // The transition the previous test does not reach, and the one an upsert-shaped
        // implementation would get wrong: a criterion that WAS narrow enough to materialise and is
        // then widened past the gate must end with zero members. Leaving the old set behind would
        // pair stored members with a TooBroad state — material to render a number from, next to a
        // state saying the number must not be rendered.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunGoteborg, [SniBygg], CompanyRegisterStatus.Active));
        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion + 1, KommunStockholm, SniIt, ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniBygg], [KommunGoteborg], ct);

        await RunAsync(ct);
        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(1);

        await NarrowCriterionAsync(criterionId, [SniIt], [KommunStockholm], ct);
        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).ShouldBeEmpty();
        (await ReadStateAsync(criterionId, ct))!.Parsed.ShouldBe(MaterialisationState.TooBroad);
    }

    [Fact]
    public async Task Materialise_OnACriterionMatchingNothing_WritesAnHonestZero_NotAnAbsentState()
    {
        // The third of the three facts the state table exists to keep apart. "No members" here means
        // genuinely no match, and it MUST be distinguishable from "never materialised" (no state row)
        // and from too broad (TooBroad) — otherwise the read side has one symbol for three
        // situations and must guess, which is how the dishonest zero got shipped in #1656.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunGoteborg, [SniBygg], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).ShouldBeEmpty();
        var state = await ReadStateAsync(criterionId, ct);
        state.ShouldNotBeNull("ett kriterium som matchar noll bolag ÄR materialiserat — frånvaron av "
            + "en tillståndsrad betyder 'aldrig körd', vilket är något annat");
        state.Parsed.ShouldBe(MaterialisationState.Materialised);
        state.MemberCount.ShouldBe(0);
    }

    [Fact]
    public async Task ACriterionNeverMaterialised_HasNoStateRow_SoTheReadSideCannotMistakeItForZero()
    {
        // The negative that gives the previous test its meaning. Seeding a criterion and NOT running
        // the job must leave no state row at all: this is the state every criterion is in between its
        // creation and the next nightly run, so it is the common case, not an exotic one.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        (await ReadStateAsync(criterionId, ct)).ShouldBeNull();
        (await ReadMembersAsync(criterionId, ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletingTheCriterion_CascadesBothTables_AtTheDatabase()
    {
        // security-auditor Major 5(a): "a DB-level FK with ON DELETE CASCADE, not handler discipline,
        // the only form that cannot be forgotten." The delete here goes through EF's Remove — the
        // same call DeleteCompanyWatchCriterionCommandHandler makes under the hard-delete verdict
        // (C-D8 / Fork G1) — and nothing in this test, or in that handler, touches the two derived
        // tables. If the cascade were absent, member rows (derived personal data about the user)
        // would outlive their criterion: Art. 17 and Art. 5(1)(e).
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);
        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(1);
        (await ReadStateAsync(criterionId, ct)).ShouldNotBeNull();

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, ct);
            db.CompanyWatchCriteria.Remove(criterion);
            await db.SaveChangesAsync(ct);
        }

        (await ReadMembersAsync(criterionId, ct)).ShouldBeEmpty();
        (await ReadStateAsync(criterionId, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Materialise_DropsPersonnummerShapedCandidates_AndCountsThem()
    {
        // security-auditor Major 4 — the guard at this job's OWN write boundary.
        //
        // TEST PREMISE (CLAUDE.md §5 Tests:). The personnummer-shaped register row seeded below is a
        // state NO path in src/ produces: ScbLegalEntityFilter.Apply drops exactly this value class at
        // ingest, pinned at the same third-digit boundary in ScbLegalEntityFilterTests. So the actor
        // is named — there is none in production, and that is the point of the test: the property
        // under test is that this table does not INHERIT pnr-freedom from that other subsystem's
        // ingest invariant (#454). The state is reachable in the world the guard defends against — a
        // misconfigured SCB query, an unexpected SCB row, a future second writer — and the assertion
        // is about THIS filter's behaviour on such input, never about what production emits.
        //
        // The predicate itself is pinned ELSEWHERE, and §5 requires the seam to name it when it is:
        // CompanyWatchCriterionMemberFilterTests
        //   .Apply_ExcludesPersonnummerShaped_AtTheExactThirdDigitBoundary
        // covers both sides of the third-digit boundary. Also ScbLegalEntityFilterTests, which pins
        // that the CURRENT register writer does not produce this shape at all.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5510000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        var result = await RunAsync(ct);

        result.MembersExcludedPersonnummerShaped.ShouldBe(1);
        (await ReadMembersAsync(criterionId, ct)).ShouldBe(["5560000001"],
            "ett personnummerformat org.nr får aldrig nå en andra at-rest-lagring (ADR 0090 D5)");

        // Counted, not merely dropped: with the expected count being zero, a silent drop and a guard
        // that never ran produce identical evidence.
        (await ReadStateAsync(criterionId, ct))!.ExcludedPersonnummerShaped.ShouldBe(1);
    }

    [Fact]
    public async Task Materialise_IsIdempotent_AcrossRepeatedRuns()
    {
        // The job is registered with Hangfire's DEFAULT retry (unlike the SCB refresh), so a retry
        // re-running a partially-complete run must converge rather than accumulate. Replace semantics
        // make that true by construction; this is the oracle for it.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);
        await RunAsync(ct);
        await RunAsync(ct);

        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(2);
        (await ReadStateAsync(criterionId, ct))!.MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task Materialise_KeepsEachCriterionsMemberSetSeparate_AcrossTwoCriteriaInOneRun()
    {
        // THE test this suite was missing, and the gap was structural: every other case runs ONE
        // criterion while the orchestrator is a loop over N (test-writer, 2026-09-06). With N=1,
        // stripping `WHERE criterion_id = @criterion_id` from the store's DELETE stays GREEN — and in
        // production that mutant makes one user's materialisation wipe every other user's member set,
        // leaving each state row asserting a member_count whose rows are gone. Two criteria, two
        // users, disjoint municipalities, one run.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000003", KommunGoteborg, [SniIt], CompanyRegisterStatus.Active));

        var stockholm = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);
        var goteborg = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunGoteborg], ct);

        var result = await RunAsync(ct);

        // The aggregates too: with N=1 everywhere, `materialised++` -> `materialised = 1` and every
        // `+=` -> `=` survived. Summing over two criteria kills all six at once.
        result.CriteriaSeen.ShouldBe(2);
        result.CriteriaMaterialised.ShouldBe(2);
        result.CriteriaFailed.ShouldBe(0);
        result.MembersWritten.ShouldBe(3);

        (await ReadMembersAsync(stockholm, ct)).ShouldBe(["5560000001", "5560000002"], ignoreOrder: true);
        (await ReadMembersAsync(goteborg, ct)).ShouldBe(["5560000003"]);
    }

    [Fact]
    public async Task Materialise_CountsTheTwoOutcomesSeparately_WhenOneCriterionIsTooBroad()
    {
        // The if/else branch counted per arm, in ONE run — so a mutant collapsing tooBroad into
        // materialised (or either counter into an assignment) cannot hide behind a single-criterion
        // fixture.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunGoteborg, [SniBygg], CompanyRegisterStatus.Active));
        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion + 1, KommunStockholm, SniIt, ct);

        var narrow = await SeedCriterionAsync(Guid.NewGuid(), [SniBygg], [KommunGoteborg], ct);
        var broad = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        var result = await RunAsync(ct);

        result.CriteriaSeen.ShouldBe(2);
        result.CriteriaMaterialised.ShouldBe(1);
        result.CriteriaTooBroad.ShouldBe(1);
        result.MembersWritten.ShouldBe(1);

        (await ReadMembersAsync(narrow, ct)).ShouldBe(["5560000001"]);
        (await ReadMembersAsync(broad, ct)).ShouldBeEmpty();
        (await ReadStateAsync(broad, ct))!.Parsed.ShouldBe(MaterialisationState.TooBroad);
    }

    [Fact]
    public async Task Materialise_ClearsTheExcludedPersonnummerCount_WhenTheOffendingRowIsGone()
    {
        // The DO UPDATE branch for excluded_personnummer_shaped. The pnr test runs the job ONCE, so it
        // only ever exercises the INSERT arm — drop that column from the conflict list and the counter
        // becomes STICKY: a plugged ingest hole would never clear the security signal, and the signal
        // is the entire reason the filter counts rather than merely dropping.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5510000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);
        (await ReadStateAsync(criterionId, ct))!.ExcludedPersonnummerShaped.ShouldBe(1);

        await DeleteRegisterRowAsync("5510000002", ct);
        await RunAsync(ct);

        (await ReadStateAsync(criterionId, ct))!.ExcludedPersonnummerShaped.ShouldBe(0,
            "räknaren måste nollställas när hålet är tätat — annars kan en gammal träff aldrig skiljas "
            + "från en ny");
    }

    [Fact]
    public async Task Materialise_StampsMaterialisedAtFromTheClock_AndAdvancesItOnEveryRun()
    {
        // materialised_at was asserted NOWHERE — the column was not even selected — so binding
        // DateTimeOffset.MinValue, a constant, or dropping it from the conflict list were all green.
        // The docblock calls it "the staleness axis" and #1681 part 2 renders how old an answer is out
        // of it, so an unmeasured column would have shipped as a load-bearing one.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));
        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);
        var first = (await ReadStateAsync(criterionId, ct))!.MaterialisedAt;

        // Not a default and not a constant. Deliberately NOT claimed: that the value came from the
        // injected clock rather than a SQL now() — the fixture's IDateTimeProvider IS the system
        // clock, so this assertion cannot tell those apart, and saying it could would be a comment
        // asserting a discrimination the test does not have.
        first.ShouldBeGreaterThan(DateTimeOffset.MinValue);
        first.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
        await RunAsync(ct);

        (await ReadStateAsync(criterionId, ct))!.MaterialisedAt.ShouldBeGreaterThan(first,
            "conflict-grenen måste flytta fram stämpeln, annars åldras aldrig en materialisering");
    }

    [Fact]
    public async Task Materialise_WhenDisabled_ReturnsAnEmptyRun_AndLeavesExistingRowsUntouched()
    {
        // The kill-switch, in its DANGEROUS direction. Deleting the whole `if (!Enabled)` block was a
        // surviving mutant (inverting it is caught, deleting it is not), and the options docblock
        // claims switching off "degrades honestly: existing state rows go stale" — which is only true
        // if the disabled path leaves them ALONE rather than clearing them.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));
        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);
        (await ReadMembersAsync(criterionId, ct)).Count.ShouldBe(1);
        var stampBefore = (await ReadStateAsync(criterionId, ct))!.MaterialisedAt;

        var result = await RunDisabledAsync(ct);

        result.CriteriaSeen.ShouldBe(0);
        result.CriteriaMaterialised.ShouldBe(0);
        result.CriteriaTooBroad.ShouldBe(0);
        result.MembersWritten.ShouldBe(0);
        result.CriteriaFailed.ShouldBe(0);

        (await ReadMembersAsync(criterionId, ct)).ShouldBe(["5560000001"],
            "avstängd materialisering får inte RADERA en befintlig mängd — den ska bara sluta uppdatera den");
        (await ReadStateAsync(criterionId, ct))!.MaterialisedAt.ShouldBe(stampBefore);
    }

    [Fact]
    public async Task Materialise_ContinuesWithTheRemainingCriteria_WhenOneCriterionFails()
    {
        // The per-criterion catch, which was entirely unmeasured: removing the try/catch, or widening
        // its filter to `when (true)` so cancellation is swallowed, both stayed green.
        //
        // TEST PREMISE (CLAUDE.md §5 Tests:). The failing criterion is one with an EMPTY SNI axis, a
        // state NO path in src/ produces — CompanyWatchCriteriaSpec.Create forbids it and the create
        // handler goes through Create. It is DECLARED UNREACHABLE by production, and declared so in
        // production's own words: CompanyWatchBrowseQuery.BindPredicate exists to fail loud on exactly
        // this shape, because "a browse against an empty axis returns zero rows silently instead of
        // failing. The criterion is corrupt." So the actor is the database, not a domain method, and
        // the assertion is confined to what §5 permits for an unreachable state: that the READ SIDE
        // DEGRADES SAFELY — the other criteria still materialise and the run reports the failure -
        // never a claim about what production emits.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000003", KommunGoteborg, [SniIt], CompanyRegisterStatus.Active));

        var healthy = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);
        var corrupt = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunGoteborg], ct);
        await CorruptSniAxisAsync(corrupt, ct);

        var result = await RunAsync(ct);

        result.CriteriaSeen.ShouldBe(2);
        result.CriteriaFailed.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(1);

        (await ReadMembersAsync(healthy, ct)).ShouldBe(["5560000001"],
            "ett trasigt kriterium får inte några andra användares medlemsmängder");
        (await ReadStateAsync(corrupt, ct)).ShouldBeNull(
            "det felande kriteriet behåller sitt tidigare tillstånd — här: inget");
    }

    [Fact]
    public async Task Materialise_WhenEveryCriterionFails_Throws_SoTheRetryCanFire()
    {
        // The asymmetry dotnet-architect required: a partial failure is survivable, a total failure is
        // not. Without the throw nothing ever leaves RunAsync, so the Hangfire default retry the
        // Worker docblock deliberately KEEPS could never fire for the scenario it was kept for.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var corrupt = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);
        await CorruptSniAxisAsync(corrupt, ct);

        await Should.ThrowAsync<InvalidOperationException>(async () => await RunAsync(ct));
    }

    [Fact]
    public async Task Materialise_DoesNotThrow_WhenOneCriterionFailedButAnotherWasRefused()
    {
        // The THIRD conjunct of the total-failure guard (`tooBroad == 0`), which no fixture reached:
        // one failed + one TooBroad + zero materialised. Drop that conjunct and this run throws,
        // failing a Hangfire job that DID do useful work — refusing a criterion honestly is work,
        // and the state row proves it. The other two conjuncts are covered by
        // ContinuesWithTheRemainingCriteria (materialised > 0) and RefusesACriterionPastTheBreadthGate
        // (failed == 0).
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion + 1, KommunStockholm, SniIt, ct);

        var broad = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);
        var corrupt = await SeedCriterionAsync(Guid.NewGuid(), [SniBygg], [KommunGoteborg], ct);
        await CorruptSniAxisAsync(corrupt, ct);

        var result = await RunAsync(ct);

        result.CriteriaFailed.ShouldBe(1);
        result.CriteriaTooBroad.ShouldBe(1);
        result.CriteriaMaterialised.ShouldBe(0);
        (await ReadStateAsync(broad, ct))!.Parsed.ShouldBe(MaterialisationState.TooBroad);
    }

    [Fact]
    public async Task Materialise_CountsInvalidCandidatesSeparately_FromPersonnummerShapedOnes()
    {
        // MembersExcludedInvalid was asserted NOWHERE, so setting it to a constant 0 in the result
        // construction was green. It matters because the two counters carry different meanings: a
        // non-zero pnr count is a SECURITY signal that the register's ingest guard has a hole, while
        // a non-zero invalid count is data quality. Collapsing them makes the security signal
        // unreadable.
        //
        // TEST PREMISE (CLAUDE.md §5 Tests:): the 9-digit register row is a state no path in src/
        // produces — OrganizationNumber.Create refuses it at ingest, pinned in ScbLegalEntityFilter-
        // Tests, and the column is varchar(10) which permits it. Declared unreachable; the assertion
        // is confined to this filter's behaviour on hostile input, exactly as the pnr case is.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5510000002", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("556000003", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        var result = await RunAsync(ct);

        result.MembersExcludedPersonnummerShaped.ShouldBe(1);
        result.MembersExcludedInvalid.ShouldBe(1);
        result.MembersWritten.ShouldBe(1);
        (await ReadMembersAsync(criterionId, ct)).ShouldBe(["5560000001"]);
    }

    [Fact]
    public async Task ReplaceAsync_Throws_WhenTooBroadIsPairedWithMembers_ButNotWhenItIsEmpty()
    {
        // The store's own defensive contract. It is unreachable through its ONE production caller
        // (which always passes [] on the TooBroad path), so deleting the guard was a surviving mutant.
        // Calling the guard's own predicate with the input it exists to reject is the same form §5
        // sanctions for the pnr filter, so there is no premise problem. The negative control matters
        // as much: an UNCONDITIONAL guard would also pass a throw-only assertion.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>();

        // #1681 part 2 — the fingerprint is the criterion's OWN, computed the way the production
        // caller computes it (CriteriaFingerprint.Of on the aggregate's Criteria); nothing about this
        // test turns on its value, only on the guard it sits beside.
        var fingerprint = CriteriaFingerprint.Of(
            CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await store.ReplaceAsync(
                criterionId.Value, ["5560000001"], MaterialisationState.TooBroad, 0,
                DateTimeOffset.UtcNow, fingerprint, ct));

        await store.ReplaceAsync(
            criterionId.Value, [], MaterialisationState.TooBroad, 0, DateTimeOffset.UtcNow,
            fingerprint, ct);
        (await ReadStateAsync(criterionId, ct))!.Parsed.ShouldBe(MaterialisationState.TooBroad);
    }

    [Fact]
    public async Task Materialise_StampsThePredicatesFingerprint_OnTheMaterialisedPath()
    {
        // #1681 part 2 (ADR 0139) — the staleness guard's discriminator, asserted where it is
        // WRITTEN. The read side compares this stored value against the digest of the criterion's
        // current predicate, so a run that omitted the stamp, or stamped a constant, would make every
        // read report "not materialised" for a criterion that WAS materialised — and every other test
        // in this file would stay green, because none of them selects the column.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);

        var expected = CriteriaFingerprint.Of(
            CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value);
        (await ReadStateAsync(criterionId, ct))!.CriteriaFingerprint.ShouldBe(expected.Value);
    }

    [Fact]
    public async Task Materialise_StampsThePredicatesFingerprint_OnTheTooBroadPathToo()
    {
        // The path a refusal takes, and the reason the store writes the column on EVERY branch: a
        // refusal is about a PREDICATE, and it has to stop applying the moment that predicate
        // changes. Without the stamp here, a user who narrows a too-broad watch keeps being told it
        // is too broad until the next nightly run — the refusal would outlive the predicate that
        // earned it.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterRangeAsync(
            CompanyWatchCriterionMember.MaxPerCriterion + 1, KommunStockholm, SniIt, ct);

        var criterionId = await SeedCriterionAsync(Guid.NewGuid(), [SniIt], [KommunStockholm], ct);

        await RunAsync(ct);

        var state = await ReadStateAsync(criterionId, ct);
        state!.Parsed.ShouldBe(MaterialisationState.TooBroad);

        var expected = CriteriaFingerprint.Of(
            CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value);
        state.CriteriaFingerprint.ShouldBe(expected.Value,
            "en vägran gäller ett PREDIKAT — utan stämpeln kan den aldrig upphöra att gälla när "
            + "predikatet ändras");
    }

    [Fact]
    public async Task Materialise_RestampsTheFingerprint_WhenThePredicateChanges()
    {
        // The DO UPDATE branch for criteria_fingerprint. Drop the column from the conflict list and
        // the stamp becomes STICKY: an edited criterion would be re-materialised correctly and then
        // read as stale forever, because the row still names the predicate it was first written for.
        // The single-run tests above only ever exercise the INSERT arm.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        await SeedRegisterAsync(ct,
            ("5560000001", KommunStockholm, [SniIt], CompanyRegisterStatus.Active),
            ("5560000003", KommunGoteborg, [SniIt], CompanyRegisterStatus.Active));

        var criterionId = await SeedCriterionAsync(
            Guid.NewGuid(), [SniIt], [KommunStockholm, KommunGoteborg], ct);

        await RunAsync(ct);
        var before = (await ReadStateAsync(criterionId, ct))!.CriteriaFingerprint;

        // The aggregate's own transition — the production edit path.
        await NarrowCriterionAsync(criterionId, [SniIt], [KommunStockholm], ct);
        await RunAsync(ct);

        var after = (await ReadStateAsync(criterionId, ct))!.CriteriaFingerprint;

        after.ShouldNotBe(before);
        after.ShouldBe(
            CriteriaFingerprint.Of(
                CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value).Value);
    }

    // ----- helpers -------------------------------------------------------------------------------

    private async Task<CompanyWatchCriterionMaterialisationResult> RunAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = scope.ServiceProvider
            .GetRequiredService<ICompanyWatchCriterionMaterialiser>();
        return await materialiser.MaterialiseAsync(ct);
    }

    /// <summary>
    /// The same orchestrator with Enabled=false. Constructed directly rather than through the scope,
    /// because the kill-switch is an OPTIONS value and the fixture binds it enabled — the parity
    /// precedent is ScbCompanyRegisterRefresherTests, which builds its refresher the same way to reach
    /// the disabled arm. Everything else is resolved from the real graph, so only the switch differs.
    /// </summary>
    private async Task<CompanyWatchCriterionMaterialisationResult> RunDisabledAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = new CompanyWatchCriterionMaterialiser(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>(),
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            Microsoft.Extensions.Options.Options.Create(
                new CompanyWatchMaterialisationOptions { Enabled = false }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompanyWatchCriterionMaterialiser>.Instance);

        return await materialiser.MaterialiseAsync(ct);
    }

    /// <summary>
    /// Empties the criterion's SNI axis directly in Postgres. See
    /// <see cref="Materialise_ContinuesWithTheRemainingCriteria_WhenOneCriterionFails"/> for why this
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

    private async Task DeleteRegisterRowAsync(string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM company_register WHERE organization_number = {0};", [orgNr], ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Criteria first: the two derived tables cascade from it, so this also exercises the FK on
        // every test rather than only in the one that asserts it.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_watch_criteria;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_register;", ct);
    }

    private async Task SeedRegisterAsync(
        CancellationToken ct,
        params (string OrgNr, string Kommun, string[] Sni, CompanyRegisterStatus Status)[] rows)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO company_register (
                    organization_number, company_name, sate_kommun_code, sate_kommun_name,
                    sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
                VALUES ({0}, {1}, {2}, NULL, {3}, false, '1', {4}, now(), now());
                """,
                [row.OrgNr, "Bolag " + row.OrgNr, row.Kommun, row.Sni, row.Status.ToString()], ct);
        }
    }

    private async Task SeedRegisterRangeAsync(
        int count, string kommun, string sni, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Generated server-side: 1 001 individual round-trips would dominate the test's runtime for
        // no additional coverage. '55' + '7' fixes the third digit at 7, so every generated org.nr is
        // legal-entity-shaped and none is dropped by the write-boundary filter — this test is about
        // the breadth gate, and a filter drop would confound the count it asserts. The '7' also keeps
        // this generator's key space DISJOINT from SeedRegisterAsync's '556…' rows, so a test may use
        // both seeders without colliding on the register's primary key.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT '557' || lpad(i::text, 7, '0'), 'Bolag ' || i, {1}, NULL,
                   ARRAY[{2}], false, '1', 'Active', now(), now()
            FROM generate_series(1, {0}) i;
            """,
            [count, kommun, sni], ct);
    }

    private async Task DeregisterAsync(string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE company_register SET status = 'Deregistered' WHERE organization_number = {0};",
            [orgNr], ct);
    }

    private async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        Guid userId, string[] sni, string[] kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var spec = CompanyWatchCriteriaSpec.Create(sni, kommun);
        spec.IsSuccess.ShouldBeTrue("seed: specen måste vara giltig");

        var criterion = CompanyWatchCriterion.Create(userId, spec.Value, null, new FixedClock(T0));
        criterion.IsSuccess.ShouldBeTrue("seed: kriteriet måste kunna skapas");

        db.CompanyWatchCriteria.Add(criterion.Value);
        await db.SaveChangesAsync(ct);

        return criterion.Value.Id;
    }

    private async Task NarrowCriterionAsync(
        CompanyWatchCriterionId id, string[] sni, string[] kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, ct);
        var spec = CompanyWatchCriteriaSpec.Create(sni, kommun);
        spec.IsSuccess.ShouldBeTrue("seed: den nya specen måste vara giltig");

        // The aggregate's own transition, not a column poke — the production edit path.
        criterion.UpdateCriteria(spec.Value, new FixedClock(T0)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
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

    private async Task<StateRow?> ReadStateAsync(CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Database
            .SqlQueryRaw<StateRow>(
                """
                SELECT state, member_count, excluded_personnummer_shaped, materialised_at,
                       criteria_fingerprint
                FROM company_watch_criterion_materialisations
                WHERE criterion_id = {0};
                """,
                id.Value)
            .ToListAsync(ct);

        return rows.Count == 0 ? null : rows[0];
    }

    // House idiom: a private fixed clock per suite (parity AuditLogRetentionJobIntegrationTests,
    // HardDeleteAccountsJobIntegrationTests). §5 forbids DateTime.UtcNow in production; the seed path
    // takes the same injected IDateTimeProvider production does, so the timestamps a test writes are
    // produced the way production produces them.
    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // Read back as the STORED TEXT and parsed here, so the test sees the physical column value rather
    // than an EF materialisation — a state column silently persisted by ordinal would round-trip fine
    // through EF and still be wrong in the database.
    //
    // No column aliases: the context applies the snake_case naming convention to unmapped SqlQuery
    // types too, so `State` binds to `state` and `MemberCount` to `member_count` by convention. An
    // alias would have to fight that rather than help it.
    private sealed record StateRow(
        string State,
        int MemberCount,
        int ExcludedPersonnummerShaped,
        DateTimeOffset MaterialisedAt,
        string CriteriaFingerprint)
    {
        public MaterialisationState Parsed => Enum.Parse<MaterialisationState>(State);
    }
}
