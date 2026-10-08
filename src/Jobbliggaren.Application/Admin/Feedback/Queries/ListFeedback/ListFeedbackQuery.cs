using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

/// <summary>
/// The admin's feedback list (#1979), newest first, filtered by status and page. Each item carries
/// an excerpt cut here, so the list never carries a full text, and no reporter address: that is read
/// one at a time in the detail.
/// </summary>
public sealed record ListFeedbackQuery(
    FeedbackStatus? Status = null,
    string? PageKey = null,
    int PageNumber = 1,
    int PageSize = 25)
    : IQuery<FeedbackListDto>, IAdminRequest
{
    public const int MaxPageSize = 100;
    public const int ExcerptLength = 90;
}
