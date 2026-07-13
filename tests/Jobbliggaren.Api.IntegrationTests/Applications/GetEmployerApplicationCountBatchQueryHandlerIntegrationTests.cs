using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

// The Application type clashes with the Jobbliggaren.Application namespace; the integration project has
// no global alias, so it is declared per file (parity GetEmployerApplicationHistoryQueryHandlerIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #446 (#311; ADR 0087 D2 read-model; DPIA #456) — the /jobb card "tidigare ansökningar
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
        AppDbContext db, IDateTimeProvider clock, string? orgNr, string companyName, CancellationToken ct,
        int publishedDaysAgo = 2)
    {
        var externalId = $"eacb-{Guid.NewGuid():N}";
        // org.nr is a STORED generated column from raw_payload->'employer'->>'organization_number'
        // (ADR 0087 D1). A null orgNr seeds an ad whose employer carries no organization_number. It is
        // also why the value dies with the payload — see the purge tests below (#824).
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
            publishedAt: clock.UtcNow.AddDays(-publishedDaysAgo),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd;
    }

    /// <summary>
    /// Applies the purge's EXACT production write (<c>raw_payload := null</c>, the same
    /// <c>ExecuteUpdateAsync</c> shape as <see cref="PurgeStaleRawPayloadsJob"/>) to ONE ad, first
    /// asserting that the real job's cutoff predicate would select it — reading the BOUND
    /// <see cref="JobSourceRetentionOptions"/> from DI, never a compile-time default.
    ///
    /// <para>
    /// The table-wide job itself is NOT run: it would null <c>raw_payload</c> for every ad past the
    /// cutoff in the SHARED <c>[Collection("Api")]</c> Postgres, and several classes seed ads at fixed
    /// dates far beyond it (<c>2026-01-01</c> and later). No cutoff can exclude them — they are OLDER
    /// than this ad, not younger.
    /// </para>
    ///
    /// <para>
    /// <b>This is not the fiction we deleted.</b> That test hand-wrote <c>UPDATE job_ads SET deleted_at</c>
    /// — a state with ZERO writers in <c>src/</c>. This is byte-for-byte the purge job's own write, merely
    /// scoped to one provably-eligible ad. Production-reachable state, production-bound horizon; the job's
    /// predicate is pinned separately by <c>PurgeStaleRawPayloadsJobTests</c>.
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

    // --- what actually governs the count: the prior ad's AGE, not its status (#824) --------------
    //
    // These two replace `Handle_ExcludesApplicationsToRetractedAds`, which soft-deleted the prior ad
    // with raw SQL and asserted it was not counted. JobAd.DeletedAt has NO writer in src/ (#821), so
    // that test pinned an unreachable state, stayed green forever, and its false model was then written
    // into the handler docs and DPIA #456 as fact. See #843. Both tests below reach their state through
    // PRODUCTION writes: the real Archive() domain method, and the purge job's own ExecuteUpdate (scoped
    // to one provably-eligible ad — see PurgeThisAdsPayloadAsync).

    [Fact]
    public async Task Handle_CountsPriorApplication_WhenItsAdWasArchivedButIsRecent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360793";
        var priorArchived = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);
        await SeedApplicationAsync(db, clock, seeker.Id, priorArchived.Id, ApplicationStatus.Submitted, ct);

        // Archive via the REAL domain method (what RetainPlatsbankenJobAdsJob / ExpireJobAdsJob call).
        priorArchived.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var pageAd = await SeedJobAdAsync(db, clock, orgX, "Acme AB", ct);

        var result = await CreateHandler(db, reader, userId)
            .Handle(new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]), ct);

        // Archival hides nothing — the prior ad still joins, its org.nr still resolves, so it IS
        // counted. The opposite of what the docs used to claim.
        result.CountsByJobAdId[pageAd.Id.Value].ShouldBe(1);
    }

    [Fact]
    public async Task Handle_DoesNotCountPriorApplication_WhenItsAdWasPurged_EvenThoughStillActive()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IJobAdEmployerReader>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        var seeker = await SeedSeekerAsync(db, clock, userId, ct);

        var orgX = "5560360795";
        // The prior ad is published PAST the 30-day retention horizon but is still ACTIVE (deadline 30
        // days out). Nothing archives it; nothing deletes it.
        var priorStale = await SeedJobAdAsync(db, clock, orgX, "Stale AB", ct, publishedDaysAgo: 40);
        await SeedApplicationAsync(db, clock, seeker.Id, priorStale.Id, ApplicationStatus.Submitted, ct);
        var pageAd = await SeedJobAdAsync(db, clock, orgX, "Stale AB", ct);

        var query = new GetEmployerApplicationCountBatchQuery([pageAd.Id.Value]);
        (await CreateHandler(db, reader, userId).Handle(query, ct))
            .CountsByJobAdId[pageAd.Id.Value]
            .ShouldBe(1, "precondition: before the purge the prior application IS counted");

        // Apply the purge's exact production write, scoped to this ad (see the helper for why the
        // table-wide job cannot be run against the shared Api Postgres).
        await PurgeThisAdsPayloadAsync(scope.ServiceProvider, db, clock, priorStale, ct);
        db.ChangeTracker.Clear();

        // The prior ad is still Active and still present — only its org.nr was recomputed to NULL.
        var (status, orgNr) = await db.JobAds.AsNoTracking()
            .Where(j => j.Id == priorStale.Id)
            .Select(j => ValueTuple.Create(j.Status.Value, EF.Property<string?>(j, "OrganizationNumber")))
            .SingleAsync(ct);
        status.ShouldBe("Active");
        orgNr.ShouldBeNull();

        // So the badge silently undercounts: the user HAS applied to this employer, and is told nothing.
        // Pinned as the CURRENT behaviour; the copy is hedged in #824 PR 4 and the root cause removed in
        // #841 — when #841 lands this SHOULD start counting again, and this assertion should fail.
        var result = await CreateHandler(db, reader, userId).Handle(query, ct);
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
