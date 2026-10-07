using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

public sealed class ListFeedbackQueryHandler(IAppDbContext db) : IQueryHandler<ListFeedbackQuery, FeedbackListDto>
{
    public async ValueTask<FeedbackListDto> Handle(ListFeedbackQuery query, CancellationToken cancellationToken)
    {
        var scoped = db.FeedbackSubmissions.AsNoTracking();
        if (query.PageKey is not null && FeedbackPage.TryFromKey(query.PageKey, out var page))
            scoped = scoped.Where(s => s.Page == page);

        var perStatus = await scoped
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int CountOf(FeedbackStatus status) => perStatus.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        var counts = new FeedbackStatusCountsDto(
            perStatus.Sum(c => c.Count),
            CountOf(FeedbackStatus.New),
            CountOf(FeedbackStatus.InProgress),
            CountOf(FeedbackStatus.Resolved),
            CountOf(FeedbackStatus.Declined));

        var filtered = query.Status is { } status ? scoped.Where(s => s.Status == status) : scoped;
        var total = query.Status is { } chosen ? CountOf(chosen) : counts.All;

        var pageOfSubmissions = filtered
            .OrderByDescending(s => s.SubmittedAt)
            .ThenBy(s => s.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize);

        // A left join rather than Contains over the typed ids, which EF Core cannot translate.
        var rows = await (
                from s in pageOfSubmissions
                join n in db.FeedbackNotifications.AsNoTracking() on s.Id equals n.SubmissionId into notices
                from n in notices.DefaultIfEmpty()
                orderby s.SubmittedAt descending, s.Id
                select new
                {
                    s.Id,
                    s.Page,
                    s.Rating,
                    s.Comment,
                    s.Status,
                    s.SubmittedAt,
                    State = n == null ? (FeedbackNotificationState?)null : n.State,
                })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new FeedbackListItemDto(
                r.Id.Value,
                r.Page.Name,
                r.Rating?.Value,
                Excerpt(r.Comment),
                r.Status,
                r.SubmittedAt,
                r.State))
            .ToList();

        return new FeedbackListDto(
            new PagedResult<FeedbackListItemDto>(items, total, query.PageNumber, query.PageSize),
            counts);
    }

    private static string? Excerpt(FeedbackComment? comment)
    {
        if (comment is null)
            return null;

        var text = comment.Value;
        if (text.Length <= ListFeedbackQuery.ExcerptLength)
            return text;

        var cut = char.IsHighSurrogate(text[ListFeedbackQuery.ExcerptLength - 1])
            ? ListFeedbackQuery.ExcerptLength - 1
            : ListFeedbackQuery.ExcerptLength;
        return text[..cut].TrimEnd() + "…";
    }
}
