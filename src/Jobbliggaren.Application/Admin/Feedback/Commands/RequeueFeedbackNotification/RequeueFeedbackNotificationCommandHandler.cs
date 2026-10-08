using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;

public sealed class RequeueFeedbackNotificationCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    : ICommandHandler<RequeueFeedbackNotificationCommand, Result>
{
    public async ValueTask<Result> Handle(RequeueFeedbackNotificationCommand command, CancellationToken cancellationToken)
    {
        var submissionId = new FeedbackSubmissionId(command.Id);
        var notice = await db.FeedbackNotifications
            .FirstOrDefaultAsync(n => n.SubmissionId == submissionId, cancellationToken);
        if (notice is null)
            return Result.Failure(DomainError.NotFound("Feedback", command.Id));

        return notice.Requeue(command.AcknowledgeDuplicateRisk, clock.UtcNow);
    }
}
