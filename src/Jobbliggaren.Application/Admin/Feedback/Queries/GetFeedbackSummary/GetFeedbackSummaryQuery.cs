using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

/// <summary>
/// Per page over the last 7, 30 or 90 days (#1979): how many users rated it, how their latest rating
/// is spread over 1–5, the mean of those ratings, and how many submissions arrived. A submission
/// without a rating counts as a submission and never as a zero.
/// </summary>
public sealed record GetFeedbackSummaryQuery(int Days) : IQuery<FeedbackSummaryDto>, IAdminRequest
{
    public static readonly IReadOnlySet<int> AllowedDays = new HashSet<int> { 7, 30, 90 };
}
