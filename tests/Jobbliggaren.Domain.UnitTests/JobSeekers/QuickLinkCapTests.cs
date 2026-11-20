using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0188 (#2265) — at most three quick links, the junior link included (Klas, 2026-11-20).
/// </summary>
public class QuickLinkCapTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    public void EnsureRoomForOne_BelowTheCap_Succeeds(bool juniorLinkOn, int markedSearches)
    {
        QuickLinkCap.EnsureRoomForOne(juniorLinkOn, markedSearches).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    public void EnsureRoomForOne_AtOrPastTheCap_IsAConflict(bool juniorLinkOn, int markedSearches)
    {
        var result = QuickLinkCap.EnsureRoomForOne(juniorLinkOn, markedSearches);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("QuickLink.MaxTotal");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void EnsureRoomForOne_CountsTheJuniorLinkWhileItIsOn()
    {
        QuickLinkCap.EnsureRoomForOne(juniorLinkOn: false, markedSearches: 2).IsSuccess.ShouldBeTrue();
        QuickLinkCap.EnsureRoomForOne(juniorLinkOn: true, markedSearches: 2).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void EnsureRoomForOne_ANegativeCount_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => QuickLinkCap.EnsureRoomForOne(true, -1));
    }

    [Theory]
    [InlineData(new int[0], 1)]
    [InlineData(new[] { 1 }, 2)]
    [InlineData(new[] { 2 }, 1)]
    [InlineData(new[] { 1, 3 }, 2)]
    [InlineData(new[] { 3, 1 }, 2)]
    public void FreeSlot_IsTheLowestSlotNoMarkedSearchHolds(int[] used, int expected)
    {
        QuickLinkCap.FreeSlot(used).ShouldBe(expected);
    }

    [Fact]
    public void FreeSlot_WhenEverySlotIsHeld_IsNull()
    {
        QuickLinkCap.FreeSlot([1, 2, 3]).ShouldBeNull();
    }

    [Fact]
    public void MaxTotal_IsBelowTheRecentSearchCap_SoTheEvictionAlwaysHasAnUnmarkedRow()
    {
        QuickLinkCap.MaxTotal.ShouldBeLessThan(RecentJobSearch.MaxPerSeeker);
    }
}
