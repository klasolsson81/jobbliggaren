using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ReadTimeMatching builds the profile from the request's <c>ICurrentUser</c>; a Worker caller has no user, so it would
/// get <c>Assessed: false</c> and admit every id.
/// </summary>
public class ReadTimeMatchingConsumerTests
{
    private const string ReadTimeMatching = "Jobbliggaren.Application.Matching.Queries.ReadTimeMatching";

    [Fact]
    public void Only_the_new_ad_handlers_and_the_follow_feed_source_reference_it()
    {
        var consumers = Consumers();

        consumers.ShouldBe(
            [
                typeof(GetNewFollowedCompanyAdCountQueryHandler).FullName!,
                typeof(ListNewFollowedCompanyAdsQueryHandler).FullName!,
                typeof(ListNewCriterionAdsQueryHandler).FullName!,
                "Jobbliggaren.Application.NotificationFeed.Sources.FollowedCompanyAdFeedSource",
            ],
            ignoreOrder: true,
            $"Types found: {string.Join(", ", consumers)}");
    }

    [Fact]
    public void No_type_under_a_Jobs_namespace_references_it()
    {
        var consumers = Consumers();

        consumers.Where(owner => owner.Contains(".Jobs.", StringComparison.Ordinal)).ShouldBeEmpty(
            $"Types found: {string.Join(", ", consumers)}");
    }

    private static SortedSet<string> Consumers() =>
        ProductionTypeSweep.OwnersReferencing(
            typeof(ListNewFollowedCompanyAdsQueryHandler).Assembly.GetType(ReadTimeMatching, throwOnError: true)!.FullName!);
}
