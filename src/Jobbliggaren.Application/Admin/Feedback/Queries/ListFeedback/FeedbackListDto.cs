using Jobbliggaren.Application.Common;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

public sealed record FeedbackListDto(PagedResult<FeedbackListItemDto> Items, FeedbackStatusCountsDto Counts);
