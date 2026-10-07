using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;

public sealed class ChangeFeedbackStatusCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    : ICommandHandler<ChangeFeedbackStatusCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeFeedbackStatusCommand command, CancellationToken cancellationToken)
    {
        var id = new FeedbackSubmissionId(command.Id);
        var submission = await db.FeedbackSubmissions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (submission is null)
            return Result.Failure(DomainError.NotFound("Feedback", command.Id));

        return submission.ChangeStatus(command.Status, clock.UtcNow);
    }
}
