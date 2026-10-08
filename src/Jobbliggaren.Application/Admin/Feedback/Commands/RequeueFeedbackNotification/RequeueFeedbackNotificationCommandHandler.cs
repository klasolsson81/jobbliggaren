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

        var profiles = db.JobSeekers;
        if (!await db.FeedbackSubmissions.AsNoTracking().AnyAsync(
                submission => submission.Id == submissionId
                    && profiles.Any(seeker => seeker.Id == submission.JobSeekerId), cancellationToken))
            return Result.Failure(DomainError.Gone(
                "Feedback.ReporterUnavailable", "Kontoägaren saknar en aktiv profil. Aviseringen skickas inte igen."));

        return notice.Requeue(command.AcknowledgeDuplicateRisk, clock.UtcNow);
    }
}
