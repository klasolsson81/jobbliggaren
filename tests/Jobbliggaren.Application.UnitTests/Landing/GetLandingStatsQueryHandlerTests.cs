using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Application.Landing.Queries.GetLandingStats;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Landing;

public class GetLandingStatsQueryHandlerTests
{
    [Fact]
    public async Task Handle_CacheHit_ReturnsCachedValueAsIs()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = Substitute.For<ILandingStatsCache>();
        var refreshedAt = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var cached = new LandingStatsDto(
            ActiveCount: 45_580,
            NewToday: 312,
            IsStale: false,
            RefreshedAt: refreshedAt);
        cache.GetAsync(ct).Returns(cached);
        var handler = new GetLandingStatsQueryHandler(cache);

        var result = await handler.Handle(new GetLandingStatsQuery(), ct);

        result.ShouldBe(cached);
        result.IsStale.ShouldBeFalse();
        result.RefreshedAt.ShouldBe(refreshedAt);
    }

    [Fact]
    public async Task Handle_CacheMiss_ReturnsFloorWithIsStaleTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = Substitute.For<ILandingStatsCache>();
        cache.GetAsync(ct).Returns((LandingStatsDto?)null);
        var handler = new GetLandingStatsQueryHandler(cache);

        var result = await handler.Handle(new GetLandingStatsQuery(), ct);

        result.IsStale.ShouldBeTrue();
        result.RefreshedAt.ShouldBeNull();
        // Floor enligt CTO-dom 2026-05-23: aldrig 0 aktiva (skulle tolkas som
        // "tjänsten har inga jobb"), aldrig overstate "nya idag" (kan inte
        // ljuga om data vi inte har).
        result.ActiveCount.ShouldBeGreaterThan(0);
        result.NewToday.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_NeverWritesToCache()
    {
        // Disciplinerings-test: handlern är ren read-path. Eventuell framtida
        // ändring som lägger till compute-fallback (cache-aside) skulle bryta
        // ADR 0064 Variant B-mönstret och flagga via detta test.
        var ct = TestContext.Current.CancellationToken;
        var cache = Substitute.For<ILandingStatsCache>();
        cache.GetAsync(ct).Returns((LandingStatsDto?)null);
        var handler = new GetLandingStatsQueryHandler(cache);

        await handler.Handle(new GetLandingStatsQuery(), ct);

        await cache.DidNotReceive().SetAsync(Arg.Any<LandingStatsDto>(), Arg.Any<CancellationToken>());
    }
}
