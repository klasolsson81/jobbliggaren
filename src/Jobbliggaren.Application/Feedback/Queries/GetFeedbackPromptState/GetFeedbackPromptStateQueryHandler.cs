using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;

public sealed class GetFeedbackPromptStateQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, FeedbackGate gate)
    : IQueryHandler<GetFeedbackPromptStateQuery, FeedbackPromptStateDto>
{
    private static readonly FeedbackPromptStateDto Closed = new(Open: false, AnsweredPages: []);

    public async ValueTask<FeedbackPromptStateDto> Handle(
        GetFeedbackPromptStateQuery query, CancellationToken cancellationToken)
    {
        if (!gate.AcceptsSubmissions || currentUser.UserId is not { } userId)
            return Closed;

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (jobSeekerId == default)
            return Closed;

        var answered = await db.FeedbackPromptSuppressions
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId)
            .Select(s => s.Page)
            .ToListAsync(cancellationToken);

        return new FeedbackPromptStateDto(
            Open: true,
            AnsweredPages: [.. answered.Select(p => p.Name).Order(StringComparer.Ordinal)]);
    }
}
