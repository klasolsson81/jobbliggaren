using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #1682 — Testcontainers tests for the occupation × SNI-division profile against REAL Postgres.
/// Deliberately not InMemory: every property under test is one InMemory cannot see — the
/// <c>LEFT JOIN</c> that keeps not-in-register ads as their own bucket, <c>sni_codes[1]</c> on a
/// <c>text[]</c>, the positive status allow-lists on both sides of the join, the one-transaction
/// replace, and the <c>ANALYZE</c> the job owes the tables it loads.
///
/// <para>
/// Test premises (AGENTS.md §5 <c>Tests:</c>): every ad is produced by <see cref="JobAd.Import"/>, the
/// production entry point; <c>Archived</c> and <c>Erased</c> are produced by the domain actors
/// <see cref="JobAd.Archive"/> and <see cref="JobAd.Erase"/>, which the retention and Art. 17 jobs call;
/// register rows take the shape <c>ScbCompanyRegisterStore</c>'s upsert writes. Occupation-group ids
/// are per-test strings because the profile is keyed on the id alone — the taxonomy is not consulted.
/// </para>
/// </summary>
[Collection("Worker")]
public class OccupationDivisionProfileTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 9, 14, 3, 35, 0, TimeSpan.Zero);

    private const string OrgIt = "5560000001";          // Active, primary 62201 -> division 62
    private const string OrgCare = "5560000002";        // Deregistered, primary 86101 -> division 86
    private const string OrgNoSni = "5560000003";       // Active, empty sni_codes
    private const string OrgUnknown = "5560009999";     // never in the register

    [Fact]
    public async Task Build_CountsEveryNonErasedAdByItsEmployersPrimaryDivision_AndKeepsTheBucketsApart()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct,
            (OrgIt, ["62201", "70100"], CompanyRegisterStatus.Active),
            (OrgCare, ["86101"], CompanyRegisterStatus.Deregistered),
            (OrgNoSni, [], CompanyRegisterStatus.Active));

        var ogX = "og-x-" + Guid.NewGuid().ToString("N")[..8];
        var ogY = "og-y-" + Guid.NewGuid().ToString("N")[..8];
        await SeedAdAsync(ogX, OrgIt, ct);
        await SeedAdAsync(ogX, OrgIt, ct);
        var archived = await SeedAdAsync(ogX, OrgIt, ct);
        var erased = await SeedAdAsync(ogX, OrgIt, ct);
        await SeedAdAsync(ogX, OrgCare, ct);       // deregistered employer: the SNI still counts (D6)
        await SeedAdAsync(ogX, OrgUnknown, ct);    // org.nr the register does not hold
        await SeedAdAsync(ogX, null, ct);          // no org.nr on the ad at all
        await SeedAdAsync(ogX, OrgNoSni, ct);      // in the register, no SNI code
        await SeedAdAsync(ogY, OrgIt, ct);
        await ArchiveAsync(archived, ct);
        await EraseAsync(erased, ct);

        var result = await BuildAsync(new FixedClock(T0), ct);

        result.OccupationGroupsProfiled.ShouldBe(2);
        result.RowsWritten.ShouldBe(5);
        result.AdsCounted.ShouldBe(8, "the erased Art. 17 tombstone is the ninth ad and must not count");
        result.AdsNotInRegister.ShouldBe(2);
        result.AdsInRegisterWithoutSni.ShouldBe(1);

        var rows = await ReadRowsAsync(ct);
        rows.ShouldBe(new Dictionary<(string, string), int>
        {
            [(ogX, "62")] = 3,   // two active + one archived, primary code 62201 -> 62, never 70100
            [(ogX, "86")] = 1,   // the deregistered employer's division
            [(ogX, OccupationDivisionProfileRow.NotInRegisterCode)] = 2,
            [(ogX, OccupationDivisionProfileRow.NoSniCode)] = 1,
            [(ogY, "62")] = 1,
        }, ignoreOrder: true);

        var run = await ReadRunAsync(ct);
        run.ShouldNotBeNull();
        run.ProfiledAt.ShouldBe(T0);
        run.AdsCounted.ShouldBe(8);
    }

    [Fact]
    public async Task Build_ReplacesTheWholeProfile_NeverSupplementsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct, (OrgIt, ["62201"], CompanyRegisterStatus.Active));
        var ogGone = "og-gone-" + Guid.NewGuid().ToString("N")[..8];
        var ogKept = "og-kept-" + Guid.NewGuid().ToString("N")[..8];
        var gone = await SeedAdAsync(ogGone, OrgIt, ct);
        await SeedAdAsync(ogKept, OrgIt, ct);
        await BuildAsync(new FixedClock(T0), ct);
        (await ReadRowsAsync(ct)).Keys.ShouldContain((ogGone, "62"));

        await EraseAsync(gone, ct);
        var second = await BuildAsync(new FixedClock(T0.AddDays(1)), ct);

        second.AdsCounted.ShouldBe(1);
        var rows = await ReadRowsAsync(ct);
        rows.Keys.ShouldNotContain((ogGone, "62"), "a stale row surviving a rebuild is a supplement, not a replace");
        rows.ShouldContainKey((ogKept, "62"));
        (await ReadRunAsync(ct))!.ProfiledAt.ShouldBe(T0.AddDays(1));
    }

    [Fact]
    public async Task Build_Disabled_WritesNothing_AndTheReadSideAnswersNotProfiled()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct, (OrgIt, ["62201"], CompanyRegisterStatus.Active));
        var og = "og-off-" + Guid.NewGuid().ToString("N")[..8];
        await SeedAdAsync(og, OrgIt, ct);

        var result = await BuildAsync(new FixedClock(T0), ct, enabled: false);

        result.RowsWritten.ShouldBe(0);
        result.AdsCounted.ShouldBe(0);
        (await ReadRowsAsync(ct)).ShouldBeEmpty();
        (await ReadRunAsync(ct)).ShouldBeNull();

        var profile = (await QueryAsync(new FixedClock(T0), [og], ct))[og];
        profile.State.ShouldBe(OccupationDivisionProfileState.NotProfiled);
        profile.TotalAds.ShouldBeNull();
    }

    [Fact]
    public async Task Query_AppliesTheShareThresholdAndTheDerivedFloor_AtReadTime_PerGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct,
            (OrgIt, ["62201"], CompanyRegisterStatus.Active),
            ("5560000011", ["70100"], CompanyRegisterStatus.Active),
            (OrgCare, ["86101"], CompanyRegisterStatus.Active));

        // 25 ads: 20 -> 62 (80 %), 3 -> 70 (12 %), 1 -> 86 (4 %, under 5 %), 1 not in register.
        var ogBig = "og-big-" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 20; i++) await SeedAdAsync(ogBig, OrgIt, ct);
        for (var i = 0; i < 3; i++) await SeedAdAsync(ogBig, "5560000011", ct);
        await SeedAdAsync(ogBig, OrgCare, ct);
        await SeedAdAsync(ogBig, OrgUnknown, ct);

        // 20 ads: exactly one under the floor — at 20 a single ad clears 5 % on its own.
        var ogSmall = "og-small-" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 20; i++) await SeedAdAsync(ogSmall, OrgIt, ct);

        var ogNone = "og-none-" + Guid.NewGuid().ToString("N")[..8];

        await BuildAsync(new FixedClock(T0), ct);
        var profiles = await QueryAsync(new FixedClock(T0.AddHours(1)), [ogBig, ogSmall, ogNone], ct);

        var big = profiles[ogBig];
        big.State.ShouldBe(OccupationDivisionProfileState.Profiled);
        big.TotalAds.ShouldBe(25);
        big.Divisions.ShouldBe([new OccupationDivisionShare("62", 20), new OccupationDivisionShare("70", 3)]);
        big.NotInRegisterAdCount.ShouldBe(1);
        big.ProfiledAt.ShouldBe(T0);

        var small = profiles[ogSmall];
        small.State.ShouldBe(OccupationDivisionProfileState.TooFewAds);
        small.TotalAds.ShouldBe(20);
        small.Divisions.ShouldBeNull();

        var none = profiles[ogNone];
        none.State.ShouldBe(OccupationDivisionProfileState.TooFewAds);
        none.TotalAds.ShouldBe(0, "a group the profile holds no rows for is refused at zero, never absent");
    }

    [Fact]
    public async Task Query_AnswersNotProfiled_WhenTheRunIsAbsentOrOlderThanTheReadAgeBound()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct, (OrgIt, ["62201"], CompanyRegisterStatus.Active));
        var og = "og-age-" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 21; i++) await SeedAdAsync(og, OrgIt, ct);

        (await QueryAsync(new FixedClock(T0), [og], ct))[og].State
            .ShouldBe(OccupationDivisionProfileState.NotProfiled, "no run yet");

        await BuildAsync(new FixedClock(T0), ct);

        (await QueryAsync(new FixedClock(T0.AddHours(47)), [og], ct))[og].State
            .ShouldBe(OccupationDivisionProfileState.Profiled, "inside two cadence intervals");
        (await QueryAsync(new FixedClock(T0.AddHours(49)), [og], ct))[og].State
            .ShouldBe(OccupationDivisionProfileState.NotProfiled, "two consecutive missed runs is a broken job, not a stale number");
    }

    [Fact]
    public async Task Build_AnalyzesTheProfileTables_OncePerRunThatWroteRows_AndNotWhenDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(ct, (OrgIt, ["62201"], CompanyRegisterStatus.Active));
        await SeedAdAsync("og-an-" + Guid.NewGuid().ToString("N")[..8], OrgIt, ct);

        var before = await AnalyzeCountAsync("occupation_division_profiles", ct);
        await BuildAsync(new FixedClock(T0), ct, enabled: false);
        (await AnalyzeCountAsync("occupation_division_profiles", ct)).ShouldBe(before, "a disabled run loads nothing");

        await BuildAsync(new FixedClock(T0), ct);
        (await AnalyzeCountAsync("occupation_division_profiles", ct)).ShouldBe(before + 1);
        (await AnalyzeCountAsync("occupation_division_profile_runs", ct)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RealGraph_BindsTheOptionsFromTheirOwnSection_AndResolvesBothPorts()
    {
        // The fixture seeds OccupationDivisionProfile:CadenceCron = "22 22 * * *" — neither the shipped
        // default nor any sibling section's value — so a bind against the wrong section shows here.
        using var scope = _fixture.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OccupationDivisionProfileOptions>>().Value;
        options.CadenceCron.ShouldBe("22 22 * * *");
        options.MinimumAdsPerGroup.ShouldBe(21, "derived from the 5 % threshold: 1/20 clears it, 1/21 does not");

        scope.ServiceProvider.GetRequiredService<IOccupationDivisionProfileBuilder>()
            .ShouldBeOfType<OccupationDivisionProfileBuilder>();
        scope.ServiceProvider.GetRequiredService<IOccupationDivisionProfileQuery>()
            .ShouldBeOfType<OccupationDivisionProfileQuery>();
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<OccupationDivisionProfileResult> BuildAsync(
        IDateTimeProvider clock, CancellationToken ct, bool enabled = true)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var builder = new OccupationDivisionProfileBuilder(
            new OccupationDivisionProfileStore(db),
            clock,
            Options.Create(new OccupationDivisionProfileOptions { Enabled = enabled }),
            NullLogger<OccupationDivisionProfileBuilder>.Instance);
        return await builder.BuildAsync(ct);
    }

    private async Task<IReadOnlyDictionary<string, OccupationDivisionProfile>> QueryAsync(
        IDateTimeProvider clock, IReadOnlyList<string> ids, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = new OccupationDivisionProfileQuery(
            new OccupationDivisionProfileStore(db),
            clock,
            Options.Create(new OccupationDivisionProfileOptions()));
        return await query.GetDivisionProfilesAsync(ids, ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM occupation_division_profiles;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM occupation_division_profile_runs;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM job_ads;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_register;", ct);
    }

    private async Task SeedRegisterAsync(
        CancellationToken ct,
        params (string OrgNr, string[] Sni, CompanyRegisterStatus Status)[] rows)
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
                VALUES ({0}, {1}, '0180', NULL, {2}, false, '1', {3}, now(), now());
                """,
                [row.OrgNr, "Bolag " + row.OrgNr, row.Sni, row.Status.ToString()], ct);
        }
    }

    /// <summary>An Active ad through the production import, with the facets the profile keys on.</summary>
    private async Task<JobAdId> SeedAdAsync(string occupationGroupConceptId, string? orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = "odp-" + Guid.NewGuid().ToString("N");
        var employer = orgNr is null
            ? "\"employer\":{\"name\":\"Utan org.nr AB\"}"
            : "\"employer\":{\"name\":\"Bolag " + orgNr + "\",\"organization_number\":\"" + orgNr + "\"}";
        var rawPayload =
            "{\"id\":\"" + externalId + "\"," + employer + ","
            + "\"occupation_group\":{\"concept_id\":\"" + occupationGroupConceptId + "\"}}";

        var jobAd = JobAd.Import(
            title: "Annons",
            company: Company.Create(orgNr is null ? "Utan org.nr AB" : "Bolag " + orgNr).Value,
            description: "beskrivning",
            url: "https://example.com/jobs/" + externalId,
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: T0.AddDays(-2),
            expiresAt: T0.AddDays(60),
            clock: new FixedClock(T0.AddDays(-2)),
            declaredContacts: [],
            extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task ArchiveAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == id, ct);
        ad.Archive(new FixedClock(T0.AddDays(-1))).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    private async Task EraseAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == id, ct);
        ad.Erase(new FixedClock(T0.AddDays(-1))).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<(string Og, string Division), int>> ReadRowsAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Set<OccupationDivisionProfileRow>().AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(r => (r.OccupationGroupConceptId, r.DivisionCode), r => r.AdCount);
    }

    private async Task<OccupationDivisionProfileRun?> ReadRunAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<OccupationDivisionProfileRun>().AsNoTracking().SingleOrDefaultAsync(ct);
    }

    /// <summary>The MANUAL analyze counter, never the autovacuum one — the job's own call is what is measured.</summary>
    private async Task<long> AnalyzeCountAsync(string table, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<long>("SELECT COALESCE(analyze_count, 0)::bigint AS \"Value\" FROM pg_stat_user_tables WHERE relname = {0}", table)
            .ToListAsync(ct);
        return rows.Single();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
