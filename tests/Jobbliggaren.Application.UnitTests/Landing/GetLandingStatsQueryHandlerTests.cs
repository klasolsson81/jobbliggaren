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
    public async Task Handle_CacheMiss_ReturnsUnknown_NeverAFabricatedNumber()
    {
        // REGRESSION (CTO-bind 2026-07-13, A′): fram till nu returnerade en cache-miss ett hårdkodat
        // GOLV (ActiveCount: 40 000) med IsStale=true, och landningssidan renderade det som ett faktum.
        // Golvets försvar ("vi ljuger inte uppåt") höll bara så länge den verkliga korpusen råkade
        // överstiga 40 000 — den var 41 475 när Klas upptäckte det, en hårsmån. En hårdkodad konstant
        // kan inte vara "konservativ" om en storhet den inte mäter.
        //
        // Testet pinnar att ett omätt tal är NULL, inte en siffra. Skulle någon återinföra ett golv
        // faller det här.
        var ct = TestContext.Current.CancellationToken;
        var cache = Substitute.For<ILandingStatsCache>();
        cache.GetAsync(ct).Returns((LandingStatsDto?)null);
        var handler = new GetLandingStatsQueryHandler(cache);

        var result = await handler.Handle(new GetLandingStatsQuery(), ct);

        result.ActiveCount.ShouldBeNull();
        result.NewToday.ShouldBeNull();
        result.IsStale.ShouldBeTrue();
        result.RefreshedAt.ShouldBeNull();
        result.ShouldBe(LandingStatsDto.Unknown);
    }

    [Fact]
    public async Task Handle_CacheHitWithMeasuredZero_ReturnsZero_NotNull()
    {
        // 0 och null är OLIKA svar. En mätt nolla ("inget publicerat än idag" — sant kl. 00:05 UTC)
        // måste renderas som 0; bara "vi vet inte" är null. Utan den här pinnen kan en välmenande
        // "förenkling" mappa 0 → null och göra en sann nolla osynlig.
        var ct = TestContext.Current.CancellationToken;
        var cache = Substitute.For<ILandingStatsCache>();
        var measured = new LandingStatsDto(
            ActiveCount: 41_475,
            NewToday: 0,
            IsStale: false,
            RefreshedAt: DateTimeOffset.UtcNow);
        cache.GetAsync(ct).Returns(measured);
        var handler = new GetLandingStatsQueryHandler(cache);

        var result = await handler.Handle(new GetLandingStatsQuery(), ct);

        result.NewToday.ShouldBe(0);
        result.NewToday.ShouldNotBeNull();
        result.IsStale.ShouldBeFalse();
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
