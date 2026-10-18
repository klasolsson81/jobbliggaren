using Jobbliggaren.Application.Common.Notifications;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Notifications;

/// <summary>
/// #1576 — the window cut on its own, one arm per spec. <c>ListNewFollowedCompanyAdsQueryHandlerTests</c>
/// proves the handler reaches the cut; this suite pins what the cut does.
///
/// <para>
/// Every read here has the shape the one caller's query delivers: ordered by the hit timestamp ascending
/// (<c>orderby h.CreatedAt</c>), at most <see cref="Cap"/> + 1 rows (<c>Take(MaxRows + 1)</c>), cut at that
/// caller's own cap. Ties are what <c>CompanyWatchScanJob</c> writes when two of its per-hit clock reads
/// (<c>FollowedCompanyAdHit.Create</c> stamps <c>clock.UtcNow</c>) fall in one microsecond, the resolution
/// timestamptz keeps.
/// </para>
/// </summary>
public class AcknowledgementWindowTests
{
    private const int Cap = ListNewFollowedCompanyAdsQueryHandler.MaxRows;

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record Row(int Index, DateTimeOffset At);

    private static List<Row> Read(IEnumerable<DateTimeOffset> ascending) =>
        ascending.Select((at, i) => new Row(i, at)).ToList();

    private static IEnumerable<DateTimeOffset> Ascending(int count) =>
        Enumerable.Range(0, count).Select(i => T0.AddMinutes(i));

    private static AcknowledgementWindow.Cut<Row> Apply(IReadOnlyList<Row> read) =>
        AcknowledgementWindow.Apply(read, Cap, r => r.At);

    // ═══ Not truncated: the read holds every row above the watermark, so all of it is shown and covered.

    [Theory]
    [InlineData(1)]
    [InlineData(Cap - 1)]
    public void Apply_ReadBelowTheCap_KeepsEveryRowAndAcknowledgesTheNewest(int count)
    {
        var read = Read(Ascending(count));

        var cut = Apply(read);

        cut.Truncated.ShouldBeFalse();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, count));
        cut.AcknowledgedThrough.ShouldBe(T0.AddMinutes(count - 1));
    }

    [Fact]
    public void Apply_ReadExactlyAtTheCap_IsNotTruncatedAndAcknowledgesTheNewest()
    {
        var read = Read(Ascending(Cap));

        var cut = Apply(read);

        cut.Truncated.ShouldBeFalse("exactly the cap is not more than the cap");
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, Cap));
        cut.AcknowledgedThrough.ShouldBe(T0.AddMinutes(Cap - 1));
    }

    // Nothing lies beyond an untruncated read, so no row above the watermark is left carrying the tied
    // timestamp: the tie is shown and acknowledged whole.
    [Fact]
    public void Apply_ReadAtTheCapEndingInATie_KeepsTheTieAndAcknowledgesIt()
    {
        var tie = T0.AddMinutes(Cap - 2);
        var read = Read(Ascending(Cap - 2).Append(tie).Append(tie));

        var cut = Apply(read);

        cut.Truncated.ShouldBeFalse();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, Cap));
        cut.AcknowledgedThrough.ShouldBe(tie);
    }

    [Fact]
    public void Apply_EmptyRead_KeepsNothingAndAcknowledgesNothing()
    {
        var cut = Apply([]);

        cut.Rows.ShouldBeEmpty();
        cut.Truncated.ShouldBeFalse();
        cut.AcknowledgedThrough.ShouldBeNull("nothing was shown, so nothing may be acknowledged");
    }

    // ═══ Truncated: the extra row is the evidence, and it must stay strictly above the watermark.

    [Fact]
    public void Apply_ReadOneOverTheCapWithDistinctTimestamps_KeepsTheOldestCapAndAcknowledgesTheNewestKept()
    {
        var read = Read(Ascending(Cap + 1));

        var cut = Apply(read);

        cut.Truncated.ShouldBeTrue();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, Cap));
        var acknowledged = cut.AcknowledgedThrough.ShouldNotBeNull();
        acknowledged.ShouldBe(T0.AddMinutes(Cap - 1));
        acknowledged.ShouldBeLessThan(read[Cap].At, "the row beyond the cap comes back next visit");
    }

    // `tiedInside` rows inside the cap share the first excluded row's timestamp.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_BoundaryTieGroupStraddlesTheCap_TrimsTheWholeGroupAndAcknowledgesBelowIt(int tiedInside)
    {
        var keptCount = Cap - tiedInside;
        var tie = T0.AddMinutes(keptCount);
        var read = Read(Ascending(keptCount).Concat(Enumerable.Repeat(tie, tiedInside + 1)));

        var cut = Apply(read);

        cut.Truncated.ShouldBeTrue();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, keptCount));
        cut.Rows.ShouldNotContain(r => r.At == tie);
        var acknowledged = cut.AcknowledgedThrough.ShouldNotBeNull();
        acknowledged.ShouldBe(T0.AddMinutes(keptCount - 1));
        acknowledged.ShouldBeLessThan(tie, "the read is strict (> lastSeen), so every tied row comes back");
    }

    [Fact]
    public void Apply_TieGroupInsideTheWindowAwayFromTheBoundary_KeepsTheWholeCap()
    {
        var interiorTie = T0.AddMinutes(Cap - 3);
        var read = Read(Ascending(Cap - 3)
            .Append(interiorTie)
            .Append(interiorTie)
            .Append(T0.AddMinutes(Cap - 1))
            .Append(T0.AddMinutes(Cap)));

        var cut = Apply(read);

        cut.Truncated.ShouldBeTrue();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, Cap));
        cut.AcknowledgedThrough.ShouldBe(T0.AddMinutes(Cap - 1));
    }

    // UNREACHABLE STATE, declared (§5 Tests:): Cap + 1 hits on one timestamp would need Cap + 1 of
    // CompanyWatchScanJob's per-hit clock reads inside one microsecond, and each iteration pays a Guid and
    // an EF Add. So this asserts only that the cut degrades SAFELY: the rows are still shown, the read is
    // still flagged truncated, and the watermark does not move. It claims nothing about production.
    [Fact]
    public void Apply_WholeWindowAndTheRowBeyondShareOneTimestamp_ShowsTheWindowAndAcknowledgesNothing()
    {
        var read = Read(Enumerable.Repeat(T0, Cap + 1));

        var cut = Apply(read);

        cut.Truncated.ShouldBeTrue();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, Cap));
        cut.AcknowledgedThrough.ShouldBeNull(
            "acknowledging the shared timestamp would drop the row beyond the cap for good");
    }

    // ═══ Argument guards.

    [Fact]
    public void Apply_NullRead_Throws()
    {
        Should.Throw<ArgumentNullException>(() => AcknowledgementWindow.Apply<Row>(null!, Cap, r => r.At))
            .ParamName.ShouldBe("oldestFirst");
    }

    [Fact]
    public void Apply_NullTimestampSelector_Throws()
    {
        Should.Throw<ArgumentNullException>(() => AcknowledgementWindow.Apply(Read(Ascending(1)), Cap, null!))
            .ParamName.ShouldBe("at");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_CapBelowOne_Throws(int maxRows)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => AcknowledgementWindow.Apply(Read(Ascending(1)), maxRows, r => r.At))
            .ParamName.ShouldBe("maxRows");
    }

    [Fact]
    public void Apply_CapOfOne_KeepsTheOldestRowAndAcknowledgesIt()
    {
        var read = Read(Ascending(2));

        var cut = AcknowledgementWindow.Apply(read, 1, r => r.At);

        cut.Truncated.ShouldBeTrue();
        cut.Rows.Select(r => r.Index).ShouldBe(Enumerable.Range(0, 1));
        cut.AcknowledgedThrough.ShouldBe(T0);
    }

    // UNREACHABLE STATE, declared (§5 Tests:): the one caller orders its read ascending. This pins only that
    // a read in any other order fails loudly instead of acknowledging past the rows it would hide.
    [Fact]
    public void Apply_ReadNotOldestFirst_Throws()
    {
        var read = Read(Ascending(2).Reverse());

        Should.Throw<ArgumentException>(() => Apply(read)).ParamName.ShouldBe("oldestFirst");
    }
}
