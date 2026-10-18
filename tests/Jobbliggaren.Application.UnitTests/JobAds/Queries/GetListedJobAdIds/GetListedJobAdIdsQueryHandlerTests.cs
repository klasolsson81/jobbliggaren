using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;
using Jobbliggaren.Application.UnitTests.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetListedJobAdIds;

/// <summary>
/// One sitemap part from the snapshot (ADR 0174): served only inside the bound, judged at read time on the same
/// snapshot the ids come from.
/// </summary>
public class GetListedJobAdIdsQueryHandlerTests
{
    private static readonly DateTimeOffset Now = FakeDateTimeProvider.Default.UtcNow;
    private static readonly Guid InPartOne = Guid.Parse("10000000-0000-4000-8000-000000000000");
    private static readonly Guid InPartTwo = Guid.Parse("90000000-0000-4000-8000-000000000000");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ListedJobAdRow[] Rows(params Guid[] idsInIdOrder) =>
        Array.ConvertAll(idsInIdOrder, id => new ListedJobAdRow(id, Now));

    private static async Task<ListedJobAdPart> Answer(ListedJobAdSnapshot? snapshot, int part = 1)
    {
        var reader = Substitute.For<IListedJobAdSnapshotReader>();
        reader.Current.Returns(snapshot);
        return await new GetListedJobAdIdsQueryHandler(reader, FakeDateTimeProvider.Default)
            .Handle(new GetListedJobAdIdsQuery(part, ListedJobAdSnapshotLimits.PartCount), Ct);
    }

    [Fact]
    public async Task Handle_BeforeAnyReadHasSucceeded_IsUnavailable() =>
        (await Answer(null)).ShouldBeOfType<ListedJobAdPart.Unavailable>();

    [Fact]
    public async Task Handle_AFreshSnapshot_AnswersTheIdsOfTheRequestedPart()
    {
        var snapshot = ListedJobAdSnapshot.Create(Now.AddMinutes(-1), Rows(InPartOne, InPartTwo));

        (await Answer(snapshot, part: 1)).ShouldBeOfType<ListedJobAdPart.Available>().Ids.ShouldBe([InPartOne]);
        (await Answer(snapshot, part: 2)).ShouldBeOfType<ListedJobAdPart.Available>().Ids.ShouldBe([InPartTwo]);
    }

    [Fact]
    public async Task Handle_AFreshSnapshotWithNoIdInThePart_AnswersAMeasuredZero() =>
        (await Answer(ListedJobAdSnapshot.Create(Now, Rows(InPartOne)), part: 2))
            .ShouldBeOfType<ListedJobAdPart.Available>().Ids.ShouldBeEmpty();

    [Fact]
    public async Task Handle_ASnapshotOlderThanItsBound_IsUnavailable() =>
        (await Answer(ListedJobAdSnapshot.Create(
                Now - ListedJobAdSnapshotLimits.MaxAge - TimeSpan.FromTicks(1), Rows(InPartOne))))
            .ShouldBeOfType<ListedJobAdPart.Unavailable>();

    [Fact]
    public async Task Handle_ASnapshotStampedAheadOfTheClockBeyondTheTolerance_IsUnavailable() =>
        (await Answer(ListedJobAdSnapshot.Create(
                Now + ListedJobAdSnapshotLimits.FutureTolerance + TimeSpan.FromTicks(1), Rows(InPartOne))))
            .ShouldBeOfType<ListedJobAdPart.Unavailable>();

    [Fact]
    public async Task Handle_ReadsTheSnapshotOnce_SoTheAgeAndTheIdsComeFromTheSameRead()
    {
        var reader = Substitute.For<IListedJobAdSnapshotReader>();
        reader.Current.Returns(ListedJobAdSnapshot.Create(Now, Rows(InPartOne)));

        await new GetListedJobAdIdsQueryHandler(reader, FakeDateTimeProvider.Default)
            .Handle(new GetListedJobAdIdsQuery(1, ListedJobAdSnapshotLimits.PartCount), Ct);

        _ = reader.Received(1).Current;
    }
}
