using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.ListJobAds;

// F4-14 (ADR 0076 Decision 4/5) — den read-side sort-ytan ListJobAdsSort och dess
// härledning till det rena Domain-värdet (JobAdSortBy) + match-sort-intentet.
//
// Två invarianter pinnas här (rena CPU-tabeller, ingen DB):
//   1. ListJobAdsSortExtensions.ToDomainSort — alla 6 värden mappar deterministiskt;
//      MatchDesc → PublishedAtDesc (honest fallback + recent-search-hash, Decision 5).
//   2. ListJobAdsQuery.SortBy/SortByMatch — härledda properties: SortBy ser ALLTID
//      ett rent Domain-värde (ICapturesRecentSearch/FilterHash), SortByMatch är true
//      ENDAST för MatchDesc.
public class ListJobAdsSortTests
{
    // -----------------------------------------------------------------
    // ToDomainSort — hela mappnings-tabellen (alla 6 enum-värden)
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(ListJobAdsSort.PublishedAtDesc, JobAdSortBy.PublishedAtDesc)]
    [InlineData(ListJobAdsSort.PublishedAtAsc, JobAdSortBy.PublishedAtAsc)]
    [InlineData(ListJobAdsSort.ExpiresAtDesc, JobAdSortBy.ExpiresAtDesc)]
    [InlineData(ListJobAdsSort.ExpiresAtAsc, JobAdSortBy.ExpiresAtAsc)]
    [InlineData(ListJobAdsSort.Relevance, JobAdSortBy.Relevance)]
    // MatchDesc har INGEN anonym, persisterbar motsvarighet → mappar till den rena
    // default-sorten (honest fallback + recent-search-hash, Decision 5/7).
    [InlineData(ListJobAdsSort.MatchDesc, JobAdSortBy.PublishedAtDesc)]
    public void ToDomainSort_MapsEachValue_ToExpectedDomainSort(
        ListJobAdsSort sort, JobAdSortBy expected)
    {
        sort.ToDomainSort().ShouldBe(expected);
    }

    [Fact]
    public void ToDomainSort_CoversEveryDefinedEnumValue()
    {
        // Säkerhetsnät: ToDomainSort kastar ArgumentOutOfRangeException på ett
        // odefinierat värde → varje DEFINIERAT värde måste mappa utan att kasta.
        // Fångar ett framtida ListJobAdsSort-tillägg som glöms i switch:en.
        foreach (var sort in Enum.GetValues<ListJobAdsSort>())
        {
            Should.NotThrow(() => sort.ToDomainSort());
        }
    }

    [Fact]
    public void ToDomainSort_UnknownEnumValue_Throws()
    {
        // Defense-in-depth: validatorn avvisar redan okända värden (IsInEnum), men
        // mappningen fail-fast:ar hellre än att tyst falla till default.
        Should.Throw<ArgumentOutOfRangeException>(
            () => ((ListJobAdsSort)999).ToDomainSort());
    }

    // -----------------------------------------------------------------
    // ListJobAdsQuery.SortBy — härledd, ser ALLTID ett rent Domain-värde
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(ListJobAdsSort.PublishedAtDesc, JobAdSortBy.PublishedAtDesc)]
    [InlineData(ListJobAdsSort.PublishedAtAsc, JobAdSortBy.PublishedAtAsc)]
    [InlineData(ListJobAdsSort.ExpiresAtDesc, JobAdSortBy.ExpiresAtDesc)]
    [InlineData(ListJobAdsSort.ExpiresAtAsc, JobAdSortBy.ExpiresAtAsc)]
    [InlineData(ListJobAdsSort.Relevance, JobAdSortBy.Relevance)]
    [InlineData(ListJobAdsSort.MatchDesc, JobAdSortBy.PublishedAtDesc)]
    public void SortBy_DerivesPureDomainSort_ForEachSortValue(
        ListJobAdsSort sort, JobAdSortBy expected)
    {
        new ListJobAdsQuery(Sort: sort).SortBy.ShouldBe(expected);
    }

    [Fact]
    public void SortBy_DefaultQuery_IsPublishedAtDesc()
    {
        new ListJobAdsQuery().SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
    }

    // -----------------------------------------------------------------
    // ListJobAdsQuery.SortByMatch — true ENDAST för MatchDesc
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(ListJobAdsSort.PublishedAtDesc, false)]
    [InlineData(ListJobAdsSort.PublishedAtAsc, false)]
    [InlineData(ListJobAdsSort.ExpiresAtDesc, false)]
    [InlineData(ListJobAdsSort.ExpiresAtAsc, false)]
    [InlineData(ListJobAdsSort.Relevance, false)]
    [InlineData(ListJobAdsSort.MatchDesc, true)]
    public void SortByMatch_IsTrueOnlyForMatchDesc(ListJobAdsSort sort, bool expected)
    {
        new ListJobAdsQuery(Sort: sort).SortByMatch.ShouldBe(expected);
    }

    [Fact]
    public void SortByMatch_DefaultQuery_IsFalse()
    {
        new ListJobAdsQuery().SortByMatch.ShouldBeFalse();
    }
}
