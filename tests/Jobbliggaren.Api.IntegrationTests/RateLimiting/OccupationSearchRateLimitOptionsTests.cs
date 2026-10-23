using Jobbliggaren.Api.RateLimiting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// #2198 (ADR 0180, ADR 0043 Beslut D) — the occupation search's bucket: a stable key of its own, and
/// the numbers security-auditor set in her plan read of 2026-10-23. A key that drifted into the tree
/// read's or the bransch picker's would merge two bulkheads silently.
/// </summary>
public class OccupationSearchRateLimitOptionsTests
{
    [Fact]
    public void OccupationSearchPolicyKey_IsStableAndItsOwn()
    {
        RateLimitingExtensions.OccupationSearchPolicy.ShouldBe("occupation-search");
        RateLimitingExtensions.OccupationSearchPolicy.ShouldNotBe(RateLimitingExtensions.TaxonomyReadPolicy);
        RateLimitingExtensions.OccupationSearchPolicy.ShouldNotBe(RateLimitingExtensions.OccupationDivisionsPolicy);
    }

    [Fact]
    public void Defaults_OccupationSearch_Is30Per10sInSixSegments()
    {
        var sut = new RateLimitingOptions();

        sut.OccupationSearch.PermitLimit.ShouldBe(30);
        sut.OccupationSearch.WindowSeconds.ShouldBe(10);
        sut.OccupationSearch.SegmentsPerWindow.ShouldBe(6);
    }
}
