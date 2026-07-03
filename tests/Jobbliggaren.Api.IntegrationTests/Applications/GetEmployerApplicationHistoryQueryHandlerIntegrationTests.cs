using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        AppDbContext db, IDateTimeProvider clock, string orgNr, string companyName, CancellationToken ct)
    {
        var externalId = $"eah-{Guid.NewGuid():N}";
        // org.nr is a STORED generated column from raw_payload->'employer'->>'organization_number'
        // (ADR 0087 D1) — it MUST live in the payload, not just on the Company VO.
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
            publishedAt: clock.UtcNow.AddDays(-2),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd;
    }

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

    [Fact]
    public async Task Handle_ExcludesApplicationsToRetractedAds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);
        var ad = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, ad.Id, ApplicationStatus.Submitted, ct);

        // Soft-delete (retract) the ad at DB level — no domain method exists (test seam, parity
        // AttachResumeVersionHandlerIntegrationTests). The global soft-delete query filter then hides
        // the ad, so its org.nr is unresolvable and the application is not attributed to an employer
        // (the honestly-documented archived-ad gap: AdSnapshot lacks org.nr in v1).
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE job_ads SET deleted_at = {0} WHERE id = {1}", [clock.UtcNow, ad.Id.Value], ct);
        db.ChangeTracker.Clear();

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
