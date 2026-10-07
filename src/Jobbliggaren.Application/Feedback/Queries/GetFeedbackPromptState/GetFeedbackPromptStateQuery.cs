using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;

/// <summary>
/// Whether feedback is open, and the pages where this user's inline prompt stays hidden because
/// they already gave feedback there (#1979). Shared by every device, since it lives on the server.
/// </summary>
public sealed record GetFeedbackPromptStateQuery : IQuery<FeedbackPromptStateDto>, IAuthenticatedRequest;

public sealed record FeedbackPromptStateDto(bool Open, IReadOnlyList<string> AnsweredPages);

public sealed class GetFeedbackPromptStateQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, FeedbackGate gate)
    : IQueryHandler<GetFeedbackPromptStateQuery, FeedbackPromptStateDto>
{
    public async ValueTask<FeedbackPromptStateDto> Handle(
        GetFeedbackPromptStateQuery query, CancellationToken cancellationToken)
    {
        if (!gate.AcceptsSubmissions || currentUser.UserId is not { } userId)
            return new FeedbackPromptStateDto(Open: false, AnsweredPages: []);

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
