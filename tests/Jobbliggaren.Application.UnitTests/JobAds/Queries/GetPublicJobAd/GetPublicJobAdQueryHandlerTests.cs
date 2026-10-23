using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAd;

// CA2012: NSubstitute stubs and receive-checks on a ValueTask-returning port member are the
// library's own idiom (the same waiver as TaxonomyQueryHandlersTests and DeriveOccupationCodesQueryHandlerTests).
#pragma warning disable CA2012

/// <summary>
/// The anonymous detail read (ADR 0160). Premises come from the production factories: an imported
/// ad through <c>JobAd.Import</c>, a never-imported one through <c>JobAd.Create</c> (what
/// <c>POST /api/v1/job-ads</c> calls), and lifecycle changes through <c>Archive</c>/<c>Erase</c>.
/// The taxonomy port is a substitute that keeps the real adapter's contract (one row per asked id,
/// in input order, a null label for an id the snapshot does not know, never a throw).
/// </summary>
public class GetPublicJobAdQueryHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    /// <summary>What the snapshot knows in these tests. Three different names, so a swapped member fails.</summary>
    private static readonly Dictionary<string, string> SnapshotNames = new(StringComparer.Ordinal)
    {
        ["mun_sthlm"] = "Stockholm",
        ["reg_ab"] = "Stockholms län",
        ["grp_dev"] = "Mjukvaru- och systemutvecklare m.fl.",
    };

    private static readonly string[] AllThreeIds = ["mun_sthlm", "reg_ab", "grp_dev"];
    private static readonly string[] RegionIdOnly = ["reg_ab"];
    private static readonly string[] GroupIdOnly = ["grp_dev"];

    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();

    public GetPublicJobAdQueryHandlerTests()
    {
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>()
                    .Select(id => new TaxonomyLabelDto(id, SnapshotNames.GetValueOrDefault(id)))
                    .ToList()));
    }

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
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
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

    private ValueTask<Domain.Common.Result<PublicJobAdDto>> HandleAsync(AppDbContext db, Guid id) =>
        new GetPublicJobAdQueryHandler(db, _taxonomy, Clock).Handle(new GetPublicJobAdQuery(id), CancellationToken.None);

    private ValueTask<IReadOnlyList<TaxonomyLabelDto>> ThePortWasNotAskedAsync() =>
        _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Handle_ListedAd_ReturnsTheAdWithItsConceptIdsAndTheKommunLanAndGroupNames()
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
            Id: id,
            Title: "Backend-utvecklare",
            CompanyName: "Klarna",
            Description: "Vi söker en backend-utvecklare.",
            Url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            PublishedAt: publishedAt,
            ExpiresAt: expiresAt,
            MunicipalityConceptId: "mun_sthlm",
            MunicipalityName: "Stockholm",
            RegionConceptId: "reg_ab",
            RegionName: "Stockholms län",
            OccupationGroupConceptId: "grp_dev",
            OccupationGroupName: "Mjukvaru- och systemutvecklare m.fl.",
            EmploymentTypeConceptId: "et_fast",
            WorktimeExtentConceptId: "wt_full"));
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(AllThreeIds)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ListedAdWithAGroupIdTheSnapshotDoesNotKnow_LeavesTheGroupNameNullAndKeepsTheId()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddDays(20),
            TestFacets.From(occupationGroup: "grp_drifted", municipality: "mun_sthlm")));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroupConceptId.ShouldBe("grp_drifted");
        result.Value.OccupationGroupName.ShouldBeNull();
        result.Value.MunicipalityName.ShouldBe("Stockholm");
    }

    [Fact]
    public async Task Handle_ListedAdWithOnlyAGroup_AsksThePortForThatIdAlone()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), null, TestFacets.From(occupationGroup: "grp_dev")));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationGroupName.ShouldBe("Mjukvaru- och systemutvecklare m.fl.");
        result.Value.MunicipalityName.ShouldBeNull();
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(GroupIdOnly)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ListedAdWithAKommunIdTheSnapshotDoesNotKnow_LeavesThatNameNullAndKeepsTheOther()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddDays(20),
            TestFacets.From(municipality: "mun_drifted", region: "reg_ab")));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityConceptId.ShouldBe("mun_drifted");
        result.Value.MunicipalityName.ShouldBeNull();
        result.Value.RegionName.ShouldBe("Stockholms län");
    }

    [Fact]
    public async Task Handle_ListedAdWithOnlyALan_AsksThePortForThatIdAlone()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), null, TestFacets.From(region: "reg_ab")));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityName.ShouldBeNull();
        result.Value.RegionName.ShouldBe("Stockholms län");
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(RegionIdOnly)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ListedAdWithoutAnyTaxonomyId_DoesNotAskThePort()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddDays(20)));

        var result = await HandleAsync(db, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityName.ShouldBeNull();
        result.Value.RegionName.ShouldBeNull();
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();

        var result = await HandleAsync(db, Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.NotFound);
        await ThePortWasNotAskedAsync();
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
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_ErasedAd_ReturnsGone()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-2), null,
            TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")), ad => ad.Erase(Clock).IsSuccess);

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone);
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_ArchivedAd_ReturnsTheSameGoneAsAnErasedOne()
    {
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(
            db, Import(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddDays(20),
                TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")), ad => ad.Archive(Clock).IsSuccess);

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone,
            "an ended ad answers exactly like an erased one, so the public surface never shows which "
            + "ads someone asked to have erased.");
        await ThePortWasNotAskedAsync();
    }

    [Fact]
    public async Task Handle_ActiveAdPastItsDeadline_ReturnsGone()
    {
        // The clock, not a writer, makes this state: the ad is still Active until ExpireJobAdsJob runs.
        await using var db = TestAppDbContextFactory.Create();
        var id = await SeedAsync(db, Import(Clock.UtcNow.AddDays(-10), Clock.UtcNow.AddDays(-1),
            TestFacets.From(municipality: "mun_sthlm", region: "reg_ab")));

        var result = await HandleAsync(db, id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone);
        await ThePortWasNotAskedAsync();
    }
}
#pragma warning restore CA2012
