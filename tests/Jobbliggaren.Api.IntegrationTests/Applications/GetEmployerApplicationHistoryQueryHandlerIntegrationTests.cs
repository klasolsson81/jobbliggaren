using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
// The Application type clashes with the Jobbliggaren.Application namespace; the integration project
// has no global alias, so it is declared per file (parity GetApplicationsQueryHandlerIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #444 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — the per-employer application-history
/// projection. The handler JOINs applications to public job_ads on the nullable-struct FK, reads the
/// STORED org.nr shadow column, and value-converts the Status SmartEnum — none of which EF InMemory
/// can translate, so this is by definition an integration test (Npgsql / Testcontainers), per the
/// GetApplicationsQueryHandlerIntegrationTests precedent. Handler constructed directly with an
/// NSubstitute ICurrentUser (auth-semantics 1:1 with the pipeline). Each test seeds a unique userId
/// (random Guid), so the result is naturally isolated per JobSeeker against the shared
/// [Collection("Api")] Postgres.
/// </summary>
[Collection("Api")]
public class GetEmployerApplicationHistoryQueryHandlerIntegrationTests(ApiFactory factory)
{
    private static readonly GetEmployerApplicationHistoryQuery Query = new();

    // --- seeding helpers ------------------------------------------------------------------------

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static async Task<JobAd> SeedJobAdAsync(
        AppDbContext db, IDateTimeProvider clock, string orgNr, string companyName, CancellationToken ct,
        int publishedDaysAgo = 2)
    {
        var externalId = $"eah-{Guid.NewGuid():N}";
        // org.nr is a STORED generated column from raw_payload->'employer'->>'organization_number'
        // (ADR 0087 D1) — it MUST live in the payload, not just on the Company VO. That is also why it
        // dies with the payload: see Handle_ActiveButPurgedAd_IsNotAttributed (#824).
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Testannons",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-publishedDaysAgo),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd;
    }

    /// <summary>
    /// Applies the purge's EXACT production write (<c>raw_payload := null</c>, the same
    /// <c>ExecuteUpdateAsync</c> shape as <see cref="PurgeStaleRawPayloadsJob"/>) to ONE ad, and first
    /// asserts that the real job's own cutoff predicate would in fact select that ad — reading the
    /// BOUND <see cref="JobSourceRetentionOptions"/> from DI, not a compile-time default. So if anyone
    /// raises <c>JobTech:RawPayloadRetentionDays</c>, this fails loudly instead of quietly pinning a
    /// horizon production no longer uses.
    ///
    /// <para>
    /// <b>Why not simply call <c>job.RunAsync()</c>?</b> Because it is a TABLE-WIDE
    /// <c>ExecuteUpdateAsync</c> over every ad past the cutoff, and this suite runs against a SHARED
    /// <c>[Collection("Api")]</c> Postgres in which several classes seed ads at FIXED dates far beyond
    /// the horizon (e.g. <c>2026-01-01</c> in ListJobAdsStatusFilterOracleTests / MyMatchesSurfaceTests /
    /// JobsWatermarkSurfaceTests). Running the real job would silently null THEIR raw_payload — and thus
    /// their generated columns — with the damage depending on execution order
    /// (<c>reference_api_integration_shared_db_contamination</c>). No cutoff can exclude them: they are
    /// OLDER than this test's ad, not younger.
    /// </para>
    ///
    /// <para>
    /// <b>This is NOT the fiction we just deleted.</b> The removed test hand-wrote
    /// <c>UPDATE job_ads SET deleted_at = …</c> — a state with ZERO writers in <c>src/</c>, which
    /// production can never reach. The write below is <b>byte-for-byte the write the purge job performs</b>
    /// (<c>SetProperty(j =&gt; j.RawPayload, _ =&gt; null)</c>); only its SCOPE is narrowed, from "every ad past
    /// the cutoff" to "this ad, which the cutoff provably covers". The state is production-reachable, the
    /// horizon is production-bound, and the job's own predicate is separately pinned by
    /// <c>PurgeStaleRawPayloadsJobTests</c>.
    /// </para>
    /// </summary>
    private static async Task PurgeThisAdsPayloadAsync(
        IServiceProvider sp, AppDbContext db, IDateTimeProvider clock, JobAd ad, CancellationToken ct)
    {
        var retentionDays = sp.GetRequiredService<IOptions<JobSourceRetentionOptions>>()
            .Value.RawPayloadRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        ad.PublishedAt.ShouldBeLessThan(
            cutoff,
            $"the real purge job selects PublishedAt < cutoff (retention {retentionDays}d); if this fails "
            + "the seeded ad no longer sits past the horizon and the test would prove nothing");

        await db.JobAds
            .Where(j => j.Id == ad.Id && j.RawPayload != null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.RawPayload, _ => null), ct);
    }

    /// <summary>Reads the ad's status and STORED org.nr shadow column. (No soft-delete tombstone to
    /// read: JobAd has no such axis - #821 retired the dead column.)</summary>
    private static async Task<(string Status, string? OrgNr)> ReadAdFactsAsync(
        AppDbContext db, JobAdId adId, CancellationToken ct) =>
        await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == adId)
            .Select(j => ValueTuple.Create(
                j.Status.Value, EF.Property<string?>(j, "OrganizationNumber")))
            .SingleAsync(ct);

    /// <summary>Seeds one application. finalStatus null → left as Draft (never applied).</summary>
    private static async Task<DomainApplication> SeedApplicationAsync(
        AppDbContext db, IDateTimeProvider clock, JobSeekerId seekerId, JobAdId? jobAdId,
        ApplicationStatus? finalStatus, CancellationToken ct)
    {
        var app = DomainApplication.Create(seekerId, jobAdId, null, null, clock).Value;
        if (finalStatus is not null && finalStatus != ApplicationStatus.Draft)
        {
            app.TransitionTo(ApplicationStatus.Submitted, clock);
            if (finalStatus != ApplicationStatus.Submitted)
                app.TransitionTo(finalStatus, clock);
        }

        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app;
    }

    private static GetEmployerApplicationHistoryQueryHandler CreateHandler(AppDbContext db, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetEmployerApplicationHistoryQueryHandler(db, currentUser);
    }

    /// <summary>
    /// Pins an application's applied_at at the DB level so ordering / most-recent assertions are
    /// deterministic. The registered real clock stamps ~identical timestamps on fast successive submits
    /// (why <see cref="Handle_EntriesCarryAppliedAtAndCurrentStatusName_MostRecentFirst"/> uses
    /// ignoreOrder), and no domain method backdates AppliedAt — so this uses the same ExecuteSqlRaw seam
    /// as the retract test.
    /// </summary>
    private static async Task SetAppliedAtAsync(
        AppDbContext db, Jobbliggaren.Domain.Applications.ApplicationId appId, DateTimeOffset appliedAt,
        CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE applications SET applied_at = {0} WHERE id = {1}", [appliedAt, appId.Value], ct);

    // --- auth guards ----------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await CreateHandler(db, userId: null).Handle(Query, ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A user id with no JobSeeker row.
        var result = await CreateHandler(db, userId: Guid.NewGuid()).Handle(Query, ct);

        result.ShouldBeEmpty();
    }

    // --- grouping + counting --------------------------------------------------------------------

    [Fact]
    public async Task Handle_GroupsSubmittedApplicationsByEmployer_WithCountAndEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        var orgY = "5590123456";
        var adX1 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var adX2 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var adY = await SeedJobAdAsync(db, clock, orgY, "Beta AB", ct);

        // Two submissions to employer X, one to employer Y.
        await SeedApplicationAsync(db, clock, seeker.Id, adX1.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, adX2.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, adY.Id, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.Count.ShouldBe(2);

        var x = result.Single(e => e.OrganizationNumber == orgX);
        x.ApplicationCount.ShouldBe(2);
        x.Applications.Count.ShouldBe(2);
        x.CompanyName.ShouldBe("Acme AB");
        x.IsProtectedIdentity.ShouldBeFalse();

        var y = result.Single(e => e.OrganizationNumber == orgY);
        y.ApplicationCount.ShouldBe(1);
        y.CompanyName.ShouldBe("Beta AB");
    }

    [Fact]
    public async Task Handle_EntriesCarryAppliedAtAndCurrentStatusName_MostRecentFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);
        var ad = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);

        var rejected = await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Rejected, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        var employer = result.ShouldHaveSingleItem();
        employer.Applications.Count.ShouldBe(2);
        // Both submitted at ~the same clock; assert the status names are the CURRENT statuses, and the
        // rejected one's stamped AppliedAt round-trips (Postgres truncates ticks → tolerance).
        employer.Applications.Select(a => a.StatusName).ShouldBe(["Rejected", "Submitted"], ignoreOrder: true);
        var rejectedEntry = employer.Applications.Single(a => a.StatusName == "Rejected");
        rejectedEntry.AppliedAt.ShouldBe(rejected.AppliedAt!.Value, TimeSpan.FromSeconds(1));
    }

    // --- ordering (deterministic, distinct applied_at) ------------------------------------------

    [Fact]
    public async Task Handle_OrdersEmployersAndEntriesByMostRecentApplication()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        var orgY = "5590123456";
        var adX = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var adY = await SeedJobAdAsync(db, clock, orgY, "Beta AB", ct);

        // Two submissions to X, one to Y — then pin distinct, well-separated applied_at (the real clock
        // stamps ~identical values). X's newest (base+2d) is the newest overall; Y's only one is base+1d.
        var xOld = await SeedApplicationAsync(db, clock, seeker.Id, adX.Id, ApplicationStatus.Submitted, ct);
        var xNew = await SeedApplicationAsync(db, clock, seeker.Id, adX.Id, ApplicationStatus.Submitted, ct);
        var y = await SeedApplicationAsync(db, clock, seeker.Id, adY.Id, ApplicationStatus.Submitted, ct);

        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await SetAppliedAtAsync(db, xOld.Id, baseTime, ct);
        await SetAppliedAtAsync(db, y.Id, baseTime.AddDays(1), ct);
        await SetAppliedAtAsync(db, xNew.Id, baseTime.AddDays(2), ct);
        db.ChangeTracker.Clear();

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.Count.ShouldBe(2);
        // Employers ordered by their most-recent application: X (base+2d) before Y (base+1d).
        result[0].OrganizationNumber.ShouldBe(orgX);
        result[1].OrganizationNumber.ShouldBe(orgY);
        // Within X, entries are most-recent-first (base+2d before base).
        result[0].Applications.Count.ShouldBe(2);
        result[0].Applications[0].AppliedAt.ShouldBeGreaterThan(result[0].Applications[1].AppliedAt);
    }

    [Fact]
    public async Task Handle_TakesCompanyNameFromMostRecentAd_WhenSameOrgNrCarriesTwoNames()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // Same legal entity (one org.nr), two ads whose employer NAME differs (e.g. a rebrand). The
        // handler groups on org.nr and takes the name from the most-recently-applied ad.
        var org = "5560360793";
        var adOld = await SeedJobAdAsync(db, clock, org, "Gamla Namnet AB", ct);
        var adNew = await SeedJobAdAsync(db, clock, org, "Nya Namnet AB", ct);
        var appOld = await SeedApplicationAsync(db, clock, seeker.Id, adOld.Id, ApplicationStatus.Submitted, ct);
        var appNew = await SeedApplicationAsync(db, clock, seeker.Id, adNew.Id, ApplicationStatus.Submitted, ct);

        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await SetAppliedAtAsync(db, appOld.Id, baseTime, ct);
        await SetAppliedAtAsync(db, appNew.Id, baseTime.AddDays(1), ct);
        db.ChangeTracker.Clear();

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        var employer = result.ShouldHaveSingleItem();
        employer.OrganizationNumber.ShouldBe(org);
        employer.ApplicationCount.ShouldBe(2);
        // One legal entity = one name: the most-recently-applied ad's employer name wins.
        employer.CompanyName.ShouldBe("Nya Namnet AB");
    }

    // --- exclusions -----------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ExcludesDraftApplications()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);
        var ad = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);

        // A draft (never submitted → AppliedAt null) is intent, not history.
        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, finalStatus: null, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.ShouldBeEmpty();
    }

    // --- what actually governs attribution: the ad's AGE, not its status (#824) ------------------
    //
    // These two tests replace a single `Handle_ExcludesApplicationsToRetractedAds`, which soft-deleted
    // the ad with raw SQL (`UPDATE job_ads SET deleted_at = ...`) and asserted the application was not
    // attributed. That test was green forever and proved nothing: JobAd has NO soft-delete axis
    // anywhere in src/ (#821), so it pinned a state production can never reach — and the false model it
    // encoded was then written into the handler docs and DPIA #456 as fact. See #843.
    //
    // The real mechanism: `organization_number` is a STORED generated column derived from raw_payload,
    // and PurgeStaleRawPayloadsJob nulls raw_payload 30 days after PublishedAt -> Postgres recomputes
    // the column to NULL -> the row is dropped. Both tests below reach their state through PRODUCTION
    // writes: the real Archive() domain method, and the purge job's own ExecuteUpdate (scoped to one
    // provably-eligible ad — see PurgeThisAdsPayloadAsync for why the table-wide job cannot be run
    // against the shared Api Postgres). Never a state that has no writer in src/.

    [Fact]
    public async Task Handle_ArchivedButRecentAd_IsStillAttributed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);
        var ad = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Submitted, ct);

        // Archive via the REAL domain method (what RetainPlatsbankenJobAdsJob / ExpireJobAdsJob call).
        ad.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        // Archival hides NOTHING: the row still joins, org.nr still resolves, the application IS
        // attributed. This is the opposite of what the handler docs and DPIA #456 used to claim.
        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe("5560360793");
        result[0].ApplicationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ActiveButPurgedAd_IsNotAttributed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // Published PAST the 30-day RawPayloadRetentionDays horizon, but the deadline is 30 days out:
        // the ad is ACTIVE and perfectly applicable. Nothing archives it, nothing deletes it.
        var ad = await SeedJobAdAsync(
            db, clock, "5560360794", "Stale AB", ct, publishedDaysAgo: 40);
        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Submitted, ct);

        (await CreateHandler(db, userId).Handle(Query, ct)).Count
            .ShouldBe(1, "precondition: before the purge the application IS attributed");

        // Apply the purge's exact production write, scoped to this ad (see the helper for why the
        // table-wide job cannot be run against the shared Api Postgres).
        await PurgeThisAdsPayloadAsync(scope.ServiceProvider, db, clock, ad, ct);
        db.ChangeTracker.Clear();

        // The ad is untouched as a row — it still exists and is still Active. (The old third leg
        // here asserted deleted_at IS NULL, to rule out soft-delete as the cause of the drop. That
        // leg is gone because the CAUSE cannot exist: #821 retired the axis. SingleAsync below is
        // itself the "row still exists" proof.)
        var (status, orgNr) = await ReadAdFactsAsync(db, ad.Id, ct);
        status.ShouldBe("Active");
        // ...but Postgres recomputed the STORED generated column to NULL, because its base is gone.
        orgNr.ShouldBeNull();

        // ...and so the user's own submitted application silently vanishes from her history.
        // This is the drop DPIA #456 §8 forbade ("must degrade honestly ... never fabricate").
        // Pinned as the CURRENT, honestly-recorded behaviour; #824 PR 3 replaces it with an honest
        // bucket, and #841 removes the root cause. When either lands, this assertion SHOULD fail —
        // that is the point of it.
        var result = await CreateHandler(db, userId).Handle(Query, ct);
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesApplicationsWithNoLinkedJobAd()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // A submitted application with no JobAdId (manual/bare) carries no employer org.nr.
        await SeedApplicationAsync(db, clock, seeker.Id, jobAdId: null, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesApplicationsToLiveAdWithoutOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // A LIVE ad whose payload carries no employer.organization_number -> the STORED shadow column
        // is NULL -> the application cannot be attributed to an employer and is excluded (a realistic
        // Platsbanken ad without an org.nr, distinct from a retracted ad or a manual application).
        var externalId = $"eah-noorg-{Guid.NewGuid():N}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"employer\":{{\"name\":\"No Org AB\"}}}}";
        var ad = JobAd.Import(
            title: "Testannons",
            company: Company.Create("No Org AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-2),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(ct);

        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.ShouldBeEmpty();
    }

    // --- personnummer guard (FORK C1 / D8(c)) ---------------------------------------------------

    [Fact]
    public async Task Handle_MasksPersonnummerShapedOrgNr_AndFlags_ButKeepsLegalEntityVisible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // "1901012384": third digit 0 < 2 → personnummer-shaped (enskild firma). "5560360793": legal.
        var pnrOrg = "1901012384";
        var legalOrg = "5560360793";
        var pnrAd = await SeedJobAdAsync(db, clock, pnrOrg, "Sole Trader", ct);
        var legalAd = await SeedJobAdAsync(db, clock, legalOrg, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, pnrAd.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, legalAd.Id, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, userId).Handle(Query, ct);

        result.Count.ShouldBe(2);

        // The pnr-shaped employer: raw number NEVER surfaced, flagged, still identifiable by name.
        var masked = result.Single(e => e.IsProtectedIdentity);
        masked.OrganizationNumber.ShouldBeNull();
        masked.CompanyName.ShouldBe("Sole Trader");
        masked.ApplicationCount.ShouldBe(1);

        // The legal entity: full number surfaced, not flagged.
        var visible = result.Single(e => !e.IsProtectedIdentity);
        visible.OrganizationNumber.ShouldBe(legalOrg);
    }

    // --- owner-scoping (M2 / IDOR, handler level) -----------------------------------------------

    [Fact]
    public async Task Handle_DoesNotReturnOtherJobSeekersHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var orgShared = "5560360793";
        var ad = await SeedJobAdAsync(db, clock, orgShared, "Acme AB", ct);

        var userA = Guid.NewGuid();
        var seekerA = await SeedSeekerAsync(db, clock, userA, ct);
        await SeedApplicationAsync(db, clock, seekerA.Id, ad.Id, ApplicationStatus.Submitted, ct);

        var userB = Guid.NewGuid();
        var seekerB = await SeedSeekerAsync(db, clock, userB, ct);
        // B applied to the SAME employer three times.
        await SeedApplicationAsync(db, clock, seekerB.Id, ad.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seekerB.Id, ad.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seekerB.Id, ad.Id, ApplicationStatus.Submitted, ct);

        var resultA = await CreateHandler(db, userA).Handle(Query, ct);

        var employer = resultA.ShouldHaveSingleItem();
        // A's count reflects ONLY A's own application — never B's 3 (no cross-user surface).
        employer.ApplicationCount.ShouldBe(1);
    }
}
