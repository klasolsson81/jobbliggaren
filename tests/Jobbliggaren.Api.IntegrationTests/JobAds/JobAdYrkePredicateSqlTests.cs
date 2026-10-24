using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2199 — the Yrke predicate's SQL, pinned before the occupation-name arm joins it. A search with no
/// occupation-name pick must compose exactly the query it composed before: the group step a single
/// <c>IN</c> on <c>occupation_group_concept_id</c>, and no Yrke step at all without a group. Each
/// expectation is the canonical query written out by hand, so the comparison is byte for byte.
/// </summary>
[Collection("Api")]
public class JobAdYrkePredicateSqlTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task AGroupOnlySearch_ComposesOneInOnTheGroupColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, Substitute.For<IOccupationSynonymExpander>());
        IReadOnlyList<string> groupValues = ["DJh5_yyF_hEM", "oQUQ_D11_HPx"];

        var resolved = await resolver.ResolveAsync(Criteria(occupationGroup: groupValues), ct);
        var actual = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved).ToQueryString();

        var expected = db.JobAds.AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(j => groupValues.Contains(j.OccupationGroupConceptId))
            .ToQueryString();
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task ASearchWithoutYrke_ComposesNoYrkeStep()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, Substitute.For<IOccupationSynonymExpander>());

        var resolved = await resolver.ResolveAsync(Criteria(occupationGroup: []), ct);
        var actual = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved).ToQueryString();

        var expected = db.JobAds.AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .ToQueryString();
        actual.ShouldBe(expected);
    }

    private static JobAdFilterCriteria Criteria(IReadOnlyList<string> occupationGroup) => new(
        OccupationGroup: occupationGroup,
        OccupationName: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        Q: null);
}
