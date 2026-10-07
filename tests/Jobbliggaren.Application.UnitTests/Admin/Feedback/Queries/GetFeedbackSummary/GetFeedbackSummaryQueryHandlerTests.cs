using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;
using Jobbliggaren.Application.UnitTests.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.GetFeedbackSummary;

/// <summary>
/// #1979 — the handler only turns the window into an instant and hands it to the port. What the port counts (the
/// latest rating per user and page, unrated submissions as submissions only) is SQL, proven against Postgres in
/// <c>AdminFeedbackTests</c>.
/// </summary>
public sealed class GetFeedbackSummaryQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public async Task Handle_ReadsTheWindowEndingNowAndReturnsThePortsPages(int days)
    {
        var reader = Substitute.For<IFeedbackRatingSummaryReader>();
        IReadOnlyList<FeedbackPageSummary> pages = [new FeedbackPageSummary("jobs", 3, 2, 0, 0, 0, 1, 1, 4.5m)];
        reader.ReadAsync(Now.AddDays(-days), Arg.Any<CancellationToken>()).Returns(pages);

        var summary = await new GetFeedbackSummaryQueryHandler(reader, new FakeDateTimeProvider(Now))
            .Handle(new GetFeedbackSummaryQuery(days), TestContext.Current.CancellationToken);

        summary.Days.ShouldBe(days);
        summary.Pages.ShouldBeSameAs(pages);
        await reader.Received(1).ReadAsync(Now.AddDays(-days), Arg.Any<CancellationToken>());
    }
}
