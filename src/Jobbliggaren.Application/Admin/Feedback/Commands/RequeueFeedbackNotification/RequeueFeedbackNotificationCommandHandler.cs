using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;

public sealed class RequeueFeedbackNotificationCommandHandler(
    IAppDbContext db,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    IAccountAccessCoordinator coordinator,
    IAccountAccessReader access,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext)
    : ICommandHandler<RequeueFeedbackNotificationCommand, Result>
{
    public async ValueTask<Result> Handle(RequeueFeedbackNotificationCommand command, CancellationToken cancellationToken)
    {
        var submissionId = new FeedbackSubmissionId(command.Id);
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("Feedback requeue must own its protected transaction.");
        var actorId = currentUser.UserId ?? throw new ReauthenticationFailedException();
        var owner = await db.FeedbackSubmissions.AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new { submission.JobSeekerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null)
            return Result.Failure(DomainError.NotFound("Feedback", command.Id));
        var reporterId = await db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
            .Where(seeker => seeker.Id == owner.JobSeekerId)
            .Select(seeker => (Guid?)seeker.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (reporterId is null)
            return ReporterUnavailable();

        try
        {
            await using var scope = await coordinator.BeginAsync([actorId, reporterId.Value], false, cancellationToken);
            if (!scope.OwnsCommit)
                throw new InvalidOperationException("Feedback requeue cannot borrow a commit.");
            var actor = await access.ReadAsync(actorId, cancellationToken);
            var actorProof = await access.ReadCurrentProofAsync(currentUser, cancellationToken);
            if (actor is null || !actor.IsAdmin || actorProof is null)
                throw new ReauthenticationFailedException();

            var profiles = db.JobSeekers;
            if (!await db.FeedbackSubmissions.AsNoTracking().AnyAsync(
                    submission => submission.Id == submissionId && submission.JobSeekerId == owner.JobSeekerId
                        && profiles.Any(seeker => seeker.Id == submission.JobSeekerId && seeker.UserId == reporterId),
                    cancellationToken))
                return ReporterUnavailable();

            var notice = await db.FeedbackNotifications
                .FirstOrDefaultAsync(n => n.SubmissionId == submissionId, cancellationToken);
            if (notice is null)
                return Result.Failure(DomainError.NotFound("Feedback", command.Id));
            var result = notice.Requeue(command.AcknowledgeDuplicateRisk, clock.UtcNow);
            if (result.IsFailure)
                return result;

            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow, correlationId: correlationId.Current, userId: actorId,
                eventType: command.EventType, aggregateType: command.AggregateType, aggregateId: command.Id,
                ipAddress: requestContext.IpAddress, userAgent: requestContext.UserAgent));
            await db.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    private static Result ReporterUnavailable() => Result.Failure(DomainError.Gone(
        "Feedback.ReporterUnavailable", "Kontoägaren saknar en aktiv profil. Aviseringen skickas inte igen."));
}
