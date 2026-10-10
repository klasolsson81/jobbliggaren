using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// The anonymous detail read (ADR 0160). Premises come from the production factories: an imported
/// ad through <c>JobAd.Import</c>, a never-imported one through <c>JobAd.Create</c> (what
/// <c>POST /api/v1/job-ads</c> calls), and lifecycle changes through <c>Archive</c>/<c>Erase</c>.
/// </summary>
public class GetPublicJobAdQueryHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static JobAd Import(DateTimeOffset publishedAt, DateTimeOffset? expiresAt, JobAdFacets? facets = null)
    {
        var externalId = TestIds.ExternalId();
        var payload = $"{{\"id\":\"{externalId}\"}}";
        return JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Klarna").Value,
            description: "Vi söker en backend-utvecklare.",
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: facets ?? TestFacets.None,
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: Clock,
            extractTerms: TestKeywordExtraction.None).Value;
    }

    /// <summary>Saves the ad, then applies an optional lifecycle change to the saved row, as production does.</summary>
    private static async Task<Guid> SeedAsync(AppDbContext db, JobAd jobAd, Func<JobAd, bool>? then = null)
    {
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);
        if (then is not null)
        {
            then(jobAd).ShouldBeTrue();
            await db.SaveChangesAsync(CancellationToken.None);
        }
        db.ChangeTracker.Clear();
        return jobAd.Id.Value;
    }

    private static ValueTask<Domain.Common.Result<PublicJobAdDto>> HandleAsync(AppDbContext db, Guid id) =>
        new GetPublicJobAdQueryHandler(db, Clock).Handle(new GetPublicJobAdQuery(id), CancellationToken.None);

    [Fact]
    public async Task Handle_ListedAd_ReturnsTheAdWithItsConceptIds()
    {
        await using var db = TestAppDbContextFactory.Create();
        var publishedAt = Clock.UtcNow.AddDays(-2);
        var expiresAt = Clock.UtcNow.AddDays(20);
        var id = await SeedAsync(db, Import(publishedAt, expiresAt, TestFacets.From(
            occupationGroup: "grp_dev", municipality: "mun_sthlm", region: "reg_ab",
            employmentType: "et_fast", worktimeExtent: "wt_full", organizationNumber: "5560000000")));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new PublicJobAdDto(
            id,
            "Backend-utvecklare",
            "Klarna",
            "Vi söker en backend-utvecklare.",
            "https://arbetsformedlingen.se/platsbanken/annonser/1",
            publishedAt,
            expiresAt,
            MunicipalityConceptId: "mun_sthlm",
            RegionConceptId: "reg_ab",
            OccupationGroupConceptId: "grp_dev",
            EmploymentTypeConceptId: "et_fast",
            WorktimeExtentConceptId: "wt_full"));
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();

        var result = await HandleAsync(db, Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WhenTheReadThrows_LetsTheExceptionThrough()
    {
        // The read throws because the context is disposed.
        var db = TestAppDbContextFactory.Create();
        await db.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(async () => await HandleAsync(db, Guid.NewGuid()));
    }

    [Fact]
    public async Task Handle_AdNeverImported_ReturnsTheUnknownIdAnswer()
    {
        // JobAd.Create accepts any source: POST /api/v1/job-ads can store Source=Platsbanken with no
        // External. Such a row must not be published, and must not be told apart from a missing one.
        await using var db = TestAppDbContextFactory.Create();
        var manual = JobAd.Create(
            "Backend-utvecklare", Company.Create("Klarna").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Clock.UtcNow.AddDays(-1), null, Clock).Value;
        var id = await SeedAsync(db, manual);

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.NotFound);
    }

    [Fact]
    public async Task Handle_ErasedAd_ReturnsGone()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), null), ad => ad.Erase(Clock).IsSuccess);

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone);
    }

    [Fact]
    public async Task Handle_ArchivedAd_ReturnsTheSameGoneAsAnErasedOne()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(
            db, Import(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddDays(20)), ad => ad.Archive(Clock).IsSuccess);

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone,
            "an ended ad answers exactly like an erased one, so the public surface never shows which "
            + "ads someone asked to have erased.");
    }

    [Fact]
    public async Task Handle_ActiveAdPastItsDeadline_ReturnsGone()
    {
        // The clock, not a writer, makes this state: the ad is still Active until ExpireJobAdsJob runs.
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-10), Clock.UtcNow.AddDays(-1)));

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone);
    }
}
