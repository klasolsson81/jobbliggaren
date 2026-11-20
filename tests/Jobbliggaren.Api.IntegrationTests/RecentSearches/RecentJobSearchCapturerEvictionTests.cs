using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// ADR 0060 Beslut 5, ADR 0188 (#2265) — the capturer's cap of <see cref="RecentJobSearch.MaxPerSeeker"/> on real
/// Postgres: a capture against a full list evicts the oldest UNMARKED rows, never a quick link, and stores the search
/// only once every row it picked was deleted. Its own database per test, because the exact set of rows left is the
/// assertion. Every row comes from the actor <see cref="RecentSearchQuickLinkHarness"/> names; the two interleavings
/// are the seams' moments, each the commit of a second request that production does not serialise against the first.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class RecentJobSearchCapturerEvictionTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly RecentSearchQuickLinkHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task CaptureAsync_ShouldEvictTheOldestUnmarkedRow_AndKeepEveryMark_WhenTheListIsFull()
    {
        (await _h.SetJuniorLinkAsync(enabled: false)).IsSuccess.ShouldBeTrue();
        var searches = await _h.CaptureSearchesAsync(RecentJobSearch.MaxPerSeeker, "sok");
        Guid[] marked = [searches[5], searches[10], searches[15]];
        foreach (var id in marked)
            (await _h.MarkAsync(id)).IsSuccess.ShouldBeTrue();

        await _h.CaptureAsync("nysokning");

        var rows = await _h.RowsAsync();
        rows.Count.ShouldBe(RecentJobSearch.MaxPerSeeker);
        rows.Select(r => r.Id).ShouldNotContain(searches[0], "the oldest unmarked row is the one evicted");
        rows.Select(r => r.Id).ShouldBe([.. searches.Skip(1), rows[^1].Id]);
        rows[^1].Q.ShouldBe("nysokning");
        rows.Where(r => r.QuickLinkSlot is not null).Select(r => (r.Id, r.QuickLinkSlot))
            .ShouldBe(new (Guid, int?)[] { (marked[0], 1), (marked[1], 2), (marked[2], 3) });
    }

    [Fact]
    public async Task CaptureAsync_ShouldEvictTheOldestUnmarkedRow_WhenTheOldestRowsAreQuickLinks()
    {
        (await _h.SetJuniorLinkAsync(enabled: false)).IsSuccess.ShouldBeTrue();
        var searches = await _h.CaptureSearchesAsync(RecentJobSearch.MaxPerSeeker, "sok");
        foreach (var id in searches.Take(3))
            (await _h.MarkAsync(id)).IsSuccess.ShouldBeTrue();

        await _h.CaptureAsync("nysokning");

        var rows = await _h.RowsAsync();
        rows.Count.ShouldBe(RecentJobSearch.MaxPerSeeker);
        rows.Select(r => r.Id).ShouldNotContain(searches[3], "the oldest unmarked row is the fourth oldest");
        rows.Take(3).Select(r => r.Id).ShouldBe(
            searches.Take(3), ignoreOrder: false, "a quick link is never evicted, however old");
        rows.Take(3).ShouldAllBe(r => r.QuickLinkSlot != null);
        rows[^1].Q.ShouldBe("nysokning");
    }

    [Fact]
    public async Task CaptureAsync_ShouldBumpAMarkedSearch_AndKeepItsSlot_WhenItIsCommittedAgain()
    {
        await _h.CaptureAsync("bevakad", currentCount: 3);
        var id = (await _h.RowsAsync()).ShouldHaveSingleItem().Id;
        (await _h.MarkAsync(id)).IsSuccess.ShouldBeTrue();
        var slot = (await _h.RowsAsync()).ShouldHaveSingleItem().QuickLinkSlot.ShouldNotBeNull();
        _h.Clock.UtcNow = RecentSearchQuickLinkHarness.Start.AddHours(1);

        await _h.CaptureAsync("bevakad", currentCount: 7);

        (await _h.RowsAsync()).ShouldHaveSingleItem().ShouldBe(new RecentSearchQuickLinkHarness.StoredSearch(
            id, "bevakad", RecentSearchQuickLinkHarness.Start.AddHours(1), LastSeenCount: 7, QuickLinkSlot: slot));
    }

    // The interleaving the delete's own slot re-check exists for: another request marks the row the capture picked,
    // after the capture's read and before its delete. The mark wins, and the capture stores nothing rather than a 21st
    // row.
    [Fact]
    public async Task CaptureAsync_ShouldKeepAMarkCommittedBetweenTheEvictionsReadAndItsDelete_AndStoreNothing()
    {
        var searches = await _h.CaptureSearchesAsync(RecentJobSearch.MaxPerSeeker, "sok");
        var raced = false;
        _h.Seams.BeforeTheEvictionDelete(async () =>
        {
            (await _h.MarkAsync(searches[0])).IsSuccess.ShouldBeTrue();
            raced = true;
        });

        await _h.CaptureAsync("nysokning");

        raced.ShouldBeTrue("the premise: the mark committed between the eviction's read and its delete");
        _h.Seams.EvictionDeletes.ShouldBe(1, "the capture picked a row and sent its delete");
        var rows = await _h.RowsAsync();
        rows.Select(r => r.Id).ShouldBe(searches, ignoreOrder: false, "nothing was evicted and nothing was stored");
        rows[0].QuickLinkSlot.ShouldBe(1);
        rows.ShouldNotContain(r => r.Q == "nysokning");
    }

    // The same race in the other order: the eviction commits first, between the mark's read of the row and its save. The
    // mark's write then meets no row, and the handler answers the 404 an unknown search gets.
    [Fact]
    public async Task Mark_ShouldBeNotFound_WhenAnEvictionDeletedTheSearchBetweenTheMarksReadAndItsSave()
    {
        var searches = await _h.CaptureSearchesAsync(RecentJobSearch.MaxPerSeeker, "sok");
        _h.Seams.AfterTheHeldSlotsRead(() => _h.CaptureAsync("nysokning"));

        var result = await _h.MarkAsync(searches[0]);

        _h.Seams.EvictionDeletes.ShouldBe(1, "the premise: a capture evicted a row while the mark was in flight");
        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("RecentJobSearch.NotFound");
        var rows = await _h.RowsAsync();
        rows.Select(r => r.Id).ShouldNotContain(searches[0]);
        rows.Count.ShouldBe(RecentJobSearch.MaxPerSeeker);
        rows[^1].Q.ShouldBe("nysokning");
        rows.ShouldAllBe(r => r.QuickLinkSlot == null);
    }

    // Two captures of different searches can both read room before either stores its row: the capturer does not
    // serialise its count against its insert, so the list overshoots the cap. The next capture deletes the whole
    // surplus, not one row.
    [Fact]
    public async Task CaptureAsync_ShouldTrimAnOvershootBackToTheCap_AfterTwoCapturesRacedPastTheCount()
    {
        var searches = await _h.CaptureSearchesAsync(RecentJobSearch.MaxPerSeeker - 1, "sok");
        _h.Seams.AfterTheCapTally(() => _h.CaptureAsync("racande"));

        await _h.CaptureAsync("forsta");

        var overshoot = await _h.RowsAsync();
        overshoot.Count.ShouldBe(RecentJobSearch.MaxPerSeeker + 1, "the premise: both racing captures stored a row");
        _h.Clock.UtcNow = _h.Clock.UtcNow.AddMinutes(1);

        await _h.CaptureAsync("efterat");

        var rows = await _h.RowsAsync();
        rows.Count.ShouldBe(RecentJobSearch.MaxPerSeeker);
        rows.Select(r => r.Id).ShouldNotContain(searches[0]);
        rows.Select(r => r.Id).ShouldNotContain(searches[1]);
        rows.Select(r => r.Q).ShouldContain("racande");
        rows.Select(r => r.Q).ShouldContain("forsta");
        rows[^1].Q.ShouldBe("efterat");
    }
}
