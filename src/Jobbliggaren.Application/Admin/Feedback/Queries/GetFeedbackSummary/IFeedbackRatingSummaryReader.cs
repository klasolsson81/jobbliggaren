namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

/// <summary>
/// The statistics read (senior-cto-advisor M5): counting the LATEST rating per user and page needs
/// <c>DISTINCT ON</c>, a provider feature, so it sits behind this port.
/// </summary>
public interface IFeedbackRatingSummaryReader
{
    Task<IReadOnlyList<FeedbackPageSummary>> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
