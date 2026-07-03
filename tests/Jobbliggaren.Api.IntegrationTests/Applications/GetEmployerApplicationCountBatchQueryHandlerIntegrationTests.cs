using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// The Application type clashes with the Jobbliggaren.Application namespace; the integration project has
// no global alias, so it is declared per file (parity GetEmployerApplicationHistoryQueryHandlerIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #446 (#311; ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — the /jobb card "tidigare ansökningar
/// till detta företag" overlay. The handler resolves the page ads' org.nr via the real
/// <see cref="IJobAdEmployerReader"/> (<c>= ANY</c> raw SQL) and GroupJoins the caller's applications to
/// the STORED org.nr shadow column — neither of which EF InMemory can translate, so this is by
/// definition an integration test (Npgsql / Testcontainers), the ORACLE for the SQL, parity
/// <see cref="GetEmployerApplicationHistoryQueryHandlerIntegrationTests"/>. Handler constructed directly
/// with the DI-resolved reader + an NSubstitute ICurrentUser; each test seeds a unique userId, so the
/// result is naturally isolated per JobSeeker against the shared [Collection("Api")] Postgres.
/// </summary>
[Collection("Api")]
public class GetEmployerApplicationCountBatchQueryHandlerIntegrationTests(ApiFactory factory)
{
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
        AppDbContext db, IDateTimeProvider clock, string? orgNr, string companyName, CancellationToken ct)
    {
        var externalId = $"eacb-{Guid.NewGuid():N}";
        // org.nr is a STORED generated column from raw_payload->'employer'->>'organization_number'
        // (ADR 0087 D1). A null orgNr seeds an ad whose employer carries no organization_number.
        var employerJson = orgNr is null
            ? $"{{\"name\":\"{companyName}\"}}"
            : $"{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"employer\":{employerJson}}}";

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

    /// <summary>Seeds one application. finalStatus null → left as Draft (never applied, AppliedAt null).</summary>
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

    private static GetEmployerApplicationCountBatchQueryHandler CreateHandler(
        AppDbContext db, IJobAdEmployerReader employerReader, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetEmployerApplicationCountBatchQueryHandler(db, currentUser, employerReader);
    }

    // --- auth / empty guards --------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();

        var result = await CreateHandler(db, reader, userId: null)
            .Handle(new GetEmployerApplicationCountBatchQuery([Guid.NewGuid()]), ct);

        result.CountsByJobAdId.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();

        var result = await CreateHandler(db, reader, userId: Guid.NewGuid())
            .Handle(new GetEmployerApplicationCountBatchQuery([Guid.NewGuid()]), ct);

        result.CountsByJobAdId.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoJobAdIds_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        await SeedSeekerAsync(db, clock, userId, ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([]), ct);

        result.CountsByJobAdId.ShouldBeEmpty();
    }

    // --- counting + keying ----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CountsCallersOwnSubmittedApplicationsToEmployer_KeyedByPageJobAdId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        // The caller previously applied to TWO ads of employer X.
        var priorX1 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var priorX2 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX1.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX2.Id, ApplicationStatus.Submitted, ct);

        // The /jobb page shows a THIRD, different ad of the same employer X.
        var pageAdX = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAdX.Id.Value]), ct);

        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAdX.Id.Value, 2);
    }

    [Fact]
    public async Task Handle_MapsCountToEveryPageAdSharingTheEmployer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        var prior = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, prior.Id, ApplicationStatus.Submitted, ct);

        // TWO different page ads of the SAME employer both carry the caller's count (grouped by org.nr).
        var pageAd1 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var pageAd2 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);

        var result = await CreateHandler(db, reader, userId).Handle(
            new GetEmployerApplicationCountBatchQuery([pageAd1.Id.Value, pageAd2.Id.Value]), ct);

        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAd1.Id.Value, 1);
        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAd2.Id.Value, 1);
    }

    [Fact]
    public async Task Handle_PageAdWithNoPriorApplications_IsAbsentFromMap()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // The caller applied to employer X, but the page shows an ad of employer Y (never applied to).
        var priorX = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX.Id, ApplicationStatus.Submitted, ct);
        var pageAdY = await SeedJobAdAsync(db, clock, "5590123456", "Beta AB", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAdY.Id.Value]), ct);

        // Positive-only: an ad with zero prior applications never appears (the FE renders no badge).
        result.CountsByJobAdId.ShouldNotContainKey(pageAdY.Id.Value);
        result.CountsByJobAdId.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_PageAdWithoutOrgNr_IsAbsentFromMap()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // A page ad whose employer carries no org.nr (B2 not-yet-re-ingested) can never be attributed.
        var pageAdNoOrg = await SeedJobAdAsync(db, clock, orgNr: null, "No Org AB", ct);
        // Even if the caller has some unrelated submitted history, the null-org page ad is absent.
        var otherPrior = await SeedJobAdAsync(db, clock, "5560360793", "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, otherPrior.Id, ApplicationStatus.Submitted, ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAdNoOrg.Id.Value]), ct);

        result.CountsByJobAdId.ShouldNotContainKey(pageAdNoOrg.Id.Value);
    }

    // --- multi-employer / mixed heterogeneous batch (one page = many ads) -----------------------

    [Fact]
    public async Task Handle_MultipleDistinctEmployersInOnePage_EachGetsItsOwnCount()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // Employer X: TWO prior submitted applications. Employer Y: ONE. Both appear on the same page.
        var orgX = "5560360793";
        var orgY = "5590123456";
        var priorX1 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var priorX2 = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var priorY1 = await SeedJobAdAsync(db, clock, orgY, "Beta AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX1.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX2.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorY1.Id, ApplicationStatus.Submitted, ct);

        var pageAdX = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var pageAdY = await SeedJobAdAsync(db, clock, orgY, "Beta AB", ct);

        var result = await CreateHandler(db, reader, userId).Handle(
            new GetEmployerApplicationCountBatchQuery([pageAdX.Id.Value, pageAdY.Id.Value]), ct);

        // Each ad reflects ITS OWN employer's count — the two employers never cross-contaminate (the
        // count is grouped per org.nr, not a single page-wide total sprayed onto every ad).
        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAdX.Id.Value, 2);
        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAdY.Id.Value, 1);
    }

    [Fact]
    public async Task Handle_MixedBatch_CountedPlusNeverAppliedPlusNullOrg_ProducesPartialPositiveOnlyMap()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // The page mixes three ad shapes in ONE round-trip (the real FE call): one whose employer the
        // caller applied to, one whose employer they never applied to, one carrying no org.nr at all.
        var orgX = "5560360793";
        var priorX = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorX.Id, ApplicationStatus.Submitted, ct);

        var pageAdCounted = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        var pageAdNeverApplied = await SeedJobAdAsync(db, clock, "5590123456", "Beta AB", ct);
        var pageAdNullOrg = await SeedJobAdAsync(db, clock, orgNr: null, "No Org AB", ct);

        var result = await CreateHandler(db, reader, userId).Handle(
            new GetEmployerApplicationCountBatchQuery(
                [pageAdCounted.Id.Value, pageAdNeverApplied.Id.Value, pageAdNullOrg.Id.Value]), ct);

        // Positive-only partial map: only the counted ad appears; the never-applied and null-org ads are
        // absent (no "zero" branch — the FE renders no badge). The null-org ad does not disturb the count.
        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAdCounted.Id.Value, 1);
        result.CountsByJobAdId.ShouldNotContainKey(pageAdNeverApplied.Id.Value);
        result.CountsByJobAdId.ShouldNotContainKey(pageAdNullOrg.Id.Value);
        result.CountsByJobAdId.Count.ShouldBe(1);
    }

    // --- exclusions (parity #444 semantics) -----------------------------------------------------

    [Fact]
    public async Task Handle_ExcludesDraftApplications()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        var prior = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        // A draft (never submitted → AppliedAt null) is intent, not history.
        await SeedApplicationAsync(db, clock, seeker.Id, prior.Id, finalStatus: null, ct);
        var pageAd = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]), ct);

        result.CountsByJobAdId.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesApplicationsToRetractedAds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        // A prior application whose ad is later retracted (soft-deleted): its org.nr becomes unresolvable
        // (global soft-delete filter) so it is NOT counted — the honestly-documented archived-ad gap
        // (#444 / #445), not a silent miscount.
        var priorRetracted = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorRetracted.Id, ApplicationStatus.Submitted, ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE job_ads SET deleted_at = {0} WHERE id = {1}", [clock.UtcNow, priorRetracted.Id.Value], ct);
        db.ChangeTracker.Clear();

        var pageAd = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]), ct);

        result.CountsByJobAdId.ShouldBeEmpty();
    }

    // --- personnummer / M1 (no org.nr ever surfaced) --------------------------------------------

    [Fact]
    public async Task Handle_CountsPersonnummerShapedEmployer_ButNeverSurfacesOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        // "1901012384": third digit 0 < 2 → personnummer-shaped (enskild firma). The count still works
        // (it is the caller's own application); the DTO carries only a JobAdId → int, never the org.nr,
        // so there is nothing to mask (M1 satisfied by construction — the value is not surfaced).
        var pnrOrg = "1901012384";
        var prior = await SeedJobAdAsync(db, clock, pnrOrg, "Sole Trader", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, prior.Id, ApplicationStatus.Submitted, ct);
        var pageAd = await SeedJobAdAsync(db, clock, pnrOrg, "Sole Trader", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]), ct);

        result.CountsByJobAdId.ShouldContainKeyAndValue(pageAd.Id.Value, 1);
        // The result is a plain map of Guid → int: no org.nr (pnr-shaped or otherwise) can appear in it.
        result.CountsByJobAdId.Values.ShouldAllBe(v => v > 0);
    }

    // --- owner-scoping (M2 / IDOR, handler level) -----------------------------------------------

    [Fact]
    public async Task Handle_CountsOnlyCallersOwnApplications_NeverAnotherUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var orgShared = "5560360793";
        var prior = await SeedJobAdAsync(db, clock, orgShared, "Acme AB", ct);
        var pageAd = await SeedJobAdAsync(db, clock, orgShared, "Acme AB", ct);

        var userA = Guid.NewGuid();
        var seekerA = await SeedSeekerAsync(db, clock, userA, ct);
        await SeedApplicationAsync(db, clock, seekerA.Id, prior.Id, ApplicationStatus.Submitted, ct);

        var userB = Guid.NewGuid();
        var seekerB = await SeedSeekerAsync(db, clock, userB, ct);
        // B applied to the SAME employer three times — must NEVER leak into A's count.
        await SeedApplicationAsync(db, clock, seekerB.Id, prior.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seekerB.Id, prior.Id, ApplicationStatus.Submitted, ct);
        await SeedApplicationAsync(db, clock, seekerB.Id, prior.Id, ApplicationStatus.Submitted, ct);

        var resultA = await CreateHandler(db, reader, userA)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]), ct);

        // A's badge reflects ONLY A's own single application — never B's 3 (no cross-user surface, M2).
        resultA.CountsByJobAdId.ShouldContainKeyAndValue(pageAd.Id.Value, 1);
    }
}
