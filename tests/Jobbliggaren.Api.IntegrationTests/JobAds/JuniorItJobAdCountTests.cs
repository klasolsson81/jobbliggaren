using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0188 (#2265) — the count behind the junior quick link in the /jobb hero is <c>/junior</c>'s own number: the
/// signed-in handler and the open list's handler over the same real port, at the same instant, on real Postgres. Every
/// ad comes from the actor <see cref="JuniorClassificationHarness"/> names, and every arm of the listed membership is
/// seeded, each arm's ad counted while it is listed and gone once its own actor removes it.
/// </summary>
/// <remarks>
/// The ad in a kommun the taxonomy cannot name carries a concept id the snapshot lacks. Its actor is the ACL:
/// <c>PlatsbankenJobSource.MapFacets</c> copies the wire's kommun id without reading the taxonomy, pinned by
/// <c>PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_PassesAKommunTheSnapshotCannotNameThroughAsTheWireStatesIt</c>.
/// That ad and the one without a kommun are the two the page's <c>Places</c> cannot offer, so a count summed from
/// <c>Places</c> is smaller than the total, and this class would catch it.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class JuniorItJobAdCountTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    // Shaped like a concept id, and in no taxonomy snapshot (PublicJuniorItJobAdsEndpointTests.NoSuchKommun).
    private const string UnnamedKommun = "Zz00_zzz_zzz";

    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task SignedInCount_ShouldEqualTheOpenListsTotal_OverEveryArmOfTheListedMembership()
    {
        var placed = await _h.ImportAsync("Junior utvecklare");
        var unplaced = await _h.ImportAsync("Junior testare", municipality: null, region: null);
        var unnamed = await _h.ImportAsync("Trainee inom IT", municipality: UnnamedKommun, region: null);
        await _h.ImportAsync("Junior utvecklare", expiresAt: Now.AddMinutes(1));
        var archived = await _h.ImportAsync("Junior utvecklare");
        await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(NeutralText));
        var decidedJunior = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        var vetoed = await _h.ImportAsync("Junior utvecklare");
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt);

        // Seven juniors by the rules, one Unclear in the queue; the ad outside Data/IT is never classified.
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(7, 0, 1, 0, 0));
        await ShouldAgreeAsync(expected: 7, placesSum: 5);

        (await _h.DecideAsync(decidedJunior, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(vetoed, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        await _h.ArchiveThroughTheStreamAsync(archived);
        (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(1);
        // The clock passes the last day of the ad that had one.
        _h.Clock.UtcNow = Now.AddMinutes(2);

        var list = await ShouldAgreeAsync(expected: 4, placesSum: 2);
        list.Rows.Items.Select(r => r.Id).ShouldBe([placed, unplaced, unnamed, decidedJunior], ignoreOrder: true);
        list.Places.ShouldBe([new PublicJuniorItPlaceDto(Stockholm, "Stockholm", 2)]);
    }

    [Fact]
    public async Task SignedInCount_ShouldBeZero_WhenNoListedAdIsJunior()
    {
        var vetoed = await _h.ImportAsync("Junior utvecklare");
        await _h.ImportAsync("Systemutvecklare");
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt);
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 1, 0, 0, 0));
        (await _h.DecideAsync(vetoed, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();

        (await _h.SignedInCountAsync()).Count.ShouldBe(0);
        (await _h.PublicListAsync()).Rows.TotalCount.ShouldBe(0);
    }

    /// <summary>
    /// The two reads agree, and the page's kommun options sum to <paramref name="placesSum"/>, below the total whenever
    /// a listed ad has no kommun or one the taxonomy cannot name.
    /// </summary>
    private async Task<PublicJuniorItJobAdsPage.Available> ShouldAgreeAsync(int expected, int placesSum)
    {
        var count = (await _h.SignedInCountAsync()).Count;
        var list = await _h.PublicListAsync();

        count.ShouldBe(list.Rows.TotalCount, "the hero's count is /junior's own total");
        count.ShouldBe(expected);
        list.Places.Sum(p => p.Count).ShouldBe(placesSum);
        placesSum.ShouldBeLessThan(count, "a count summed from the kommun options would miss the unplaced ads");
        return list;
    }
}
