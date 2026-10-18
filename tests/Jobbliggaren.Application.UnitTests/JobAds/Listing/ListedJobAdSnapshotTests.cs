using Jobbliggaren.Application.JobAds.Listing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Listing;

/// <summary>
/// The listed-ad snapshot (ADR 0174, ADR 0176): one read's ids, split into the sitemap's parts in id order and held newest
/// first for the catalogue, immutable, and served only inside its age bound, on both edges of it.
/// </summary>
public class ListedJobAdSnapshotTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static Guid Id(char firstDigit, int n) => Guid.Parse($"{firstDigit}{n:x7}-0000-4000-8000-000000000000");

    private static ListedJobAdRow[] Rows(params Guid[] idsInIdOrder) =>
        Array.ConvertAll(idsInIdOrder, id => new ListedJobAdRow(id, AsOf));

    [Fact]
    public void Create_SplitsTheIdsIntoTheirParts_AndKeepsTheirOrderWithinEach()
    {
        Guid[] inOrder = [Id('0', 1), Id('3', 2), Id('7', 3), Id('8', 4), Id('c', 5), Id('f', 6)];

        var snapshot = ListedJobAdSnapshot.Create(AsOf, Rows(inOrder));

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
        var snapshot = ListedJobAdSnapshot.Create(AsOf, Rows(Id('1', 1)));

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

    [Fact]
    public void CatalogueOrder_IsNewestFirst_ByPublicationDate()
    {
        var oldest = new ListedJobAdRow(Id('0', 1), AsOf.AddDays(-3));
        var newest = new ListedJobAdRow(Id('8', 2), AsOf.AddDays(-1));
        var middle = new ListedJobAdRow(Id('c', 3), AsOf.AddDays(-2));

        var snapshot = ListedJobAdSnapshot.Create(AsOf, [oldest, newest, middle]);

        snapshot.CatalogueOrder.ShouldBe([newest.Id, middle.Id, oldest.Id]);
    }

    [Fact]
    public void CatalogueOrder_AdsPublishedAtTheSameInstant_KeepTheReadsIdOrder_AcrossBothParts()
    {
        // The rows arrive in the read's id order. A tie must keep that order and never a second, in-memory id order,
        // so the oracle is the input order itself.
        Guid[] inIdOrder = [Id('1', 1), Id('5', 2), Id('9', 3), Id('d', 4)];
        var later = new ListedJobAdRow(Id('e', 5), AsOf.AddHours(1));

        var snapshot = ListedJobAdSnapshot.Create(AsOf, [.. Rows(inIdOrder), later]);

        snapshot.CatalogueOrder.ShouldBe([later.Id, .. inIdOrder]);
    }

    [Fact]
    public void CatalogueOrder_HoldsExactlyTheIdsOfTheParts_AndCountIsTheirNumber()
    {
        Guid[] inIdOrder = [Id('0', 1), Id('3', 2), Id('7', 3), Id('8', 4), Id('c', 5), Id('f', 6)];

        var snapshot = ListedJobAdSnapshot.Create(AsOf, Rows(inIdOrder));

        var inParts = Enumerable.Range(1, ListedJobAdSnapshotLimits.PartCount).SelectMany(snapshot.IdsIn);
        snapshot.CatalogueOrder.ShouldBe(inParts, ignoreOrder: true);
        snapshot.Count.ShouldBe(inIdOrder.Length);
    }

    [Fact]
    public void Create_NoRows_IsAMeasuredZeroInTheCatalogueToo()
    {
        var snapshot = ListedJobAdSnapshot.Create(AsOf, []);

        snapshot.CatalogueOrder.ShouldBeEmpty();
        snapshot.Count.ShouldBe(0);
    }

    [Fact]
    public void CatalogueOrder_IsReadOnly_SoNoRequestThreadCanAlterTheSharedSnapshot()
    {
        var snapshot = ListedJobAdSnapshot.Create(AsOf, Rows(Id('1', 1)));

        snapshot.CatalogueOrder.ShouldNotBeAssignableTo<Guid[]>();
        Should.Throw<NotSupportedException>(() => ((IList<Guid>)snapshot.CatalogueOrder).Add(Id('2', 2)));
    }
}
