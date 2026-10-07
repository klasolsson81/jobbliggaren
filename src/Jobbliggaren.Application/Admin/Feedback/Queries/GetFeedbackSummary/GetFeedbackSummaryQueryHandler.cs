using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

public sealed class GetFeedbackSummaryQueryHandler(IFeedbackRatingSummaryReader reader, IDateTimeProvider clock)
    : IQueryHandler<GetFeedbackSummaryQuery, FeedbackSummaryDto>
{
    public async ValueTask<FeedbackSummaryDto> Handle(GetFeedbackSummaryQuery query, CancellationToken cancellationToken)
    {
        var pages = await reader.ReadAsync(clock.UtcNow.AddDays(-query.Days), cancellationToken);
        return new FeedbackSummaryDto(query.Days, pages);
    }
}
