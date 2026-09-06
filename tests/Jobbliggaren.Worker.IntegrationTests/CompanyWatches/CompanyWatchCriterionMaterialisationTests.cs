using Jobbliggaren.Application.CompanyRegister.Abstractions;
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
        // and from "för bred" (TooBroad) — otherwise the read side has one symbol for three
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

    // ----- helpers -------------------------------------------------------------------------------

    private async Task<CompanyWatchCriterionMaterialisationResult> RunAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = scope.ServiceProvider
            .GetRequiredService<ICompanyWatchCriterionMaterialiser>();
        return await materialiser.MaterialiseAsync(ct);
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
                SELECT state, member_count, excluded_personnummer_shaped
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
    private sealed record StateRow(string State, int MemberCount, int ExcludedPersonnummerShaped)
    {
        public MaterialisationState Parsed => Enum.Parse<MaterialisationState>(State);
    }
}
