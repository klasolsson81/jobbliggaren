using Jobbliggaren.Application.JobAds.Listing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Listing;

/// <summary>
/// The listed-ad snapshot (ADR 0174): one read's ids, split into the sitemap's parts in id order, immutable, and served
/// only inside its age bound, on both edges of it.
/// </summary>
public class ListedJobAdSnapshotTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static Guid Id(char firstDigit, int n) => Guid.Parse($"{firstDigit}{n:x7}-0000-4000-8000-000000000000");

    [Fact]
    public void Create_SplitsTheIdsIntoTheirParts_AndKeepsTheirOrderWithinEach()
    {
        Guid[] inOrder = [Id('0', 1), Id('3', 2), Id('7', 3), Id('8', 4), Id('c', 5), Id('f', 6)];

        var snapshot = ListedJobAdSnapshot.Create(AsOf, inOrder);

        snapshot.AsOf.ShouldBe(AsOf);
        snapshot.IdsIn(1).ShouldBe([Id('0', 1), Id('3', 2), Id('7', 3)]);
        snapshot.IdsIn(2).ShouldBe([Id('8', 4), Id('c', 5), Id('f', 6)]);
    }

    [Fact]
    public void Create_NoIds_IsAMeasuredZeroInEveryPart()
    {
        var snapshot = ListedJobAdSnapshot.Create(AsOf, []);

        for (var part = 1; part <= ListedJobAdSnapshotLimits.PartCount; part++)
            snapshot.IdsIn(part).ShouldBeEmpty();
    }

    [Fact]
    public void IdsIn_IsReadOnly_SoNoRequestThreadCanAlterTheSharedSnapshot()
    {
        var snapshot = ListedJobAdSnapshot.Create(AsOf, [Id('1', 1)]);

        snapshot.IdsIn(1).ShouldNotBeAssignableTo<Guid[]>();
        Should.Throw<NotSupportedException>(() => ((IList<Guid>)snapshot.IdsIn(1)).Add(Id('2', 2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ListedJobAdSnapshotLimits.PartCount + 1)]
    public void IdsIn_APartOutsideTheSitemap_Throws(int part) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ListedJobAdSnapshot.Create(AsOf, []).IdsIn(part));

    [Fact]
    public void IsFreshAt_ExactlyMaxAgeLater_IsFresh_AndOneTickMoreIsNot()
    {
        var snapshot = ListedJobAdSnapshot.Create(AsOf, []);

        snapshot.IsFreshAt(AsOf + ListedJobAdSnapshotLimits.MaxAge).ShouldBeTrue();
        snapshot.IsFreshAt(AsOf + ListedJobAdSnapshotLimits.MaxAge + TimeSpan.FromTicks(1)).ShouldBeFalse();
    }

    [Fact]
    public void IsFreshAt_AClockBehindTheStampByTheTolerance_IsFresh_AndOneTickMoreIsNot()
    {
        // A clock that stepped back must not stretch the bound: a stamp further ahead than the tolerance is refused.
        var snapshot = ListedJobAdSnapshot.Create(AsOf, []);

        snapshot.IsFreshAt(AsOf - ListedJobAdSnapshotLimits.FutureTolerance).ShouldBeTrue();
        snapshot.IsFreshAt(AsOf - ListedJobAdSnapshotLimits.FutureTolerance - TimeSpan.FromTicks(1)).ShouldBeFalse();
    }
}
