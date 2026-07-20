using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4, D5d) — the LIST projection for a BRAND_GROUP watch, against Testcontainers
/// Postgres (only Postgres computes the STORED <c>organization_number</c> generated column + the GROUP BY
/// #447 count). The handler is CONSTRUCTED DIRECTLY with a SYNTHETIC catalogue (the shipped one is empty,
/// D5b), a stubbed <see cref="ICurrentUser"/>, and an empty-SSYK profile so <c>MatchingAdCount</c> is the
/// honest not-assessed null and the #452 port is never touched — isolating the #447 SUM + the catalogue
/// name.
/// </summary>
[Collection("Api")]
public sealed class BrandGroupListTests(ApiFactory factory)
{
    // Unique AB (non-pnr, 3rd digit 2) org.nrs per test — the shared container runs suites in parallel.
    private static string UniqueAbOrgNr() =>
        "552" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    private static FullCandidateMatchProfile EmptySsykProfile() =>
        new(new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    [Fact]
    public async Task Group_ActiveAdCount_SumsOverDistinctMembers_AndNameComesFromCatalogue()
    {
        var ct = TestContext.Current.CancellationToken;
        var m1 = UniqueAbOrgNr();
        var m2 = UniqueAbOrgNr();
        var nonMember = UniqueAbOrgNr();
        var userId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<IDateTimeProvider>();

        // Member m1: 2 active + 1 archived; member m2: 1 active; non-member: 5 active. The group count
        // must be 2 + 1 = 3 (archived excluded by status, non-member excluded by membership) — NOT 8, NOT 0.
        await SeedAdAsync(db, clock, m1, "Volvo Cars AB", ct);
        await SeedAdAsync(db, clock, m1, "Volvo Cars AB", ct);
        await SeedAdAsync(db, clock, m1, "Volvo Cars AB", ct, archived: true);
        await SeedAdAsync(db, clock, m2, "Volvo Trucks AB", ct);
        for (var i = 0; i < 5; i++)
            await SeedAdAsync(db, clock, nonMember, "Random AB", ct);

        db.CompanyWatches.Add(
            CompanyWatch.FollowBrandGroup(userId, BrandGroupId.Create("volvo").Value, clock).Value);
        await db.SaveChangesAsync(ct);

        var dto = (await BuildHandler(sp, userId, ("volvo", "Volvo (koncern)", [m1, m2]))
            .Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.TargetType.ShouldBe(CompanyWatchTargetType.BrandGroup);
        dto.BrandGroupId.ShouldBe("volvo");
        dto.OrganizationNumber.ShouldBeNull();
        // Name is the CATALOGUE display name, never the job_ads employer name ("Volvo Cars AB").
        dto.CompanyName.ShouldBe("Volvo (koncern)");
        dto.ActiveAdCount.ShouldBe(3);
        dto.MatchingAdCount.ShouldBeNull(); // empty-SSYK → not assessed
    }

    private static ListCompanyWatchesQueryHandler BuildHandler(
        IServiceProvider sp, Guid userId, (string Slug, string DisplayName, string[] Members) group)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(EmptySsykProfile());

        var catalogue = new BrandGroupCatalog("test.v1",
            new Dictionary<string, BrandGroup>(StringComparer.Ordinal)
            {
                [group.Slug] = new BrandGroup(group.Slug, group.DisplayName, group.Members),
            });

        return new ListCompanyWatchesQueryHandler(
            sp.GetRequiredService<AppDbContext>(),
            currentUser,
            profileBuilder,
            sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
            sp.GetRequiredService<IProtectedIdentityTokenizer>(),
            new StubProvider(catalogue),
            sp.GetRequiredService<ICompanyRegisterNameReader>());
    }

    private sealed class StubProvider(BrandGroupCatalog catalog) : IBrandGroupProvider
    {
        public BrandGroupCatalog Catalog { get; } = catalog;
    }

    private static async Task SeedAdAsync(
        AppDbContext db, IDateTimeProvider clock, string orgNr, string companyName, CancellationToken ct,
        bool archived = false)
    {
        var externalId = $"bg-list-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;
        if (archived)
            jobAd.Archive(clock);
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }
}
