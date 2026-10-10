using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;

/// <summary>
/// Saves the reply as Sending with its audit row and commits, sends without database locks, then
/// records the one outcome the send had (ADR 0156, #2069 amendment).
/// </summary>
public sealed partial class SendFeedbackReplyCommandHandler(
    IAppDbContext db,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    IAccountAccessCoordinator coordinator,
    IAccountAccessReader access,
    IEmailSender emailSender,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    ILogger<SendFeedbackReplyCommandHandler> logger)
    : ICommandHandler<SendFeedbackReplyCommand, Result<FeedbackReplyOutcome>>
{
    public async ValueTask<Result<FeedbackReplyOutcome>> Handle(
        SendFeedbackReplyCommand command, CancellationToken cancellationToken)
    {
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("A feedback reply must own its split-phase transactions.");
        if (!emailSender.CanDeliver)
            return Failure(DomainError.Validation(
                "Feedback.ReplyUnavailable", "E-post kan inte skickas härifrån. Svaret sparades inte."));
        var text = FeedbackReplyText.Create(command.Text);
        if (text.IsFailure)
            return Failure(text.Error);
        var actorId = currentUser.UserId ?? throw new ReauthenticationFailedException();

        var submissionId = new FeedbackSubmissionId(command.Id);
        var owner = await db.FeedbackSubmissions.AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new { submission.JobSeekerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null)
            return Failure(DomainError.NotFound("Feedback", command.Id));
        var reporterId = await db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
            .Where(seeker => seeker.Id == owner.JobSeekerId)
            .Select(seeker => (Guid?)seeker.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (reporterId is null)
            return ReporterUnavailable();

        FeedbackReplyId replyId;
        string recipient;
        FeedbackReplyEmail content;
        await using (var scope = await coordinator.BeginAsync([actorId, reporterId.Value], false, cancellationToken))
        {
            RequireOwner(scope);
            var actor = await access.ReadAsync(actorId, cancellationToken);
            var actorProof = await access.ReadCurrentProofAsync(currentUser, cancellationToken);
            if (actor is null || !actor.IsAdmin || actorProof is null)
                throw new ReauthenticationFailedException();

            // A recorded outcome is a fact about a send that already happened, so it replays even if the
            // reporter has gone since.
            var earlier = await db.FeedbackReplies.AsNoTracking()
                .Where(reply => reply.SubmissionId == submissionId && reply.ReplyKey == command.ReplyKey)
                .Select(reply => new { reply.Id, reply.State, reply.CreatedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (earlier is not null)
                return Result.Success(new FeedbackReplyOutcome(
                    earlier.Id.Value, FeedbackReply.Effective(earlier.State, earlier.CreatedAt, clock.UtcNow), true));

            var reporter = await access.ReadAsync(reporterId.Value, cancellationToken);
            if (reporter is not { HasLiveProfile: true, InboxConfirmed: true }
                || !EmailAddressRules.IsUsableInboxAddress(reporter.Email))
                return ReporterUnavailable();
            var profiles = db.JobSeekers;
            var submission = await db.FeedbackSubmissions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == submissionId && s.JobSeekerId == owner.JobSeekerId
                    && profiles.Any(seeker => seeker.Id == s.JobSeekerId && seeker.UserId == reporterId), cancellationToken);
            if (submission is null)
                return ReporterUnavailable();

            var existing = await db.FeedbackReplies.CountAsync(reply => reply.SubmissionId == submissionId, cancellationToken);
            var started = FeedbackReply.Start(submission, existing, command.ReplyKey, text.Value, clock.UtcNow);
            if (started.IsFailure)
                return Failure(started.Error);

            db.FeedbackReplies.Add(started.Value);
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow, correlationId: correlationId.Current, userId: actorId,
                eventType: command.EventType, aggregateType: command.AggregateType, aggregateId: command.Id,
                ipAddress: requestContext.IpAddress, userAgent: requestContext.UserAgent));
            await db.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            replyId = started.Value.Id;
            recipient = reporter.Email;
            content = new FeedbackReplyEmail(submission.Page, submission.SubmittedAt, text.Value);
        }
        db.ClearTracking();

        // From the commit on, the request's token no longer applies: an administrator who closes the tab
        // must not leave a mail the provider may have taken recorded as still sending. The provider
        // client's own timeout bounds the wait.
        var outcome = await SendAsync(recipient, content, replyId);

        await using (var scope = await coordinator.BeginAsync([reporterId.Value], false, CancellationToken.None))
        {
            RequireOwner(scope);
            var reply = await db.FeedbackReplies.FirstOrDefaultAsync(r => r.Id == replyId, CancellationToken.None);
            if (reply is null)
            {
                LogRemovedDuringSend(replyId.Value);
                return Failure(DomainError.Gone(
                    "Feedback.ReplyRemoved", "Inskicket togs bort medan svaret skickades."));
            }

            var recorded = outcome switch
            {
                FeedbackReplyState.Accepted => reply.RecordAccepted(clock.UtcNow),
                FeedbackReplyState.Failed => reply.RecordNotAccepted(clock.UtcNow),
                _ => reply.RecordUnknown(clock.UtcNow),
            };
            if (recorded.IsFailure)
                return Failure(recorded.Error);
            await db.SaveChangesAsync(CancellationToken.None);
            await scope.CommitAsync(CancellationToken.None);
        }

        return Result.Success(new FeedbackReplyOutcome(replyId.Value, outcome, false));
    }

    private async Task<FeedbackReplyState> SendAsync(string recipient, FeedbackReplyEmail content, FeedbackReplyId replyId)
    {
        try
        {
            await emailSender.SendFeedbackReplyAsync(recipient, content, CancellationToken.None);
            return FeedbackReplyState.Accepted;
        }
        catch (EmailDeliveryException failure)
        {
            LogSendFailed(replyId.Value, failure.UnderlyingErrorType, failure.Disposition);
            return failure.Disposition == EmailDeliveryDisposition.NotAccepted
                ? FeedbackReplyState.Failed
                : FeedbackReplyState.Unknown;
        }
    }

    private static void RequireOwner(IAccountAccessScope scope)
    {
        if (!scope.OwnsCommit)
            throw new InvalidOperationException("A feedback reply cannot borrow a commit.");
    }

    private static Result<FeedbackReplyOutcome> ReporterUnavailable() => Failure(DomainError.Gone(
        "Feedback.ReporterUnavailable", "Kontoägaren saknar en aktiv profil eller en adress att svara till."));

    private static Result<FeedbackReplyOutcome> Failure(DomainError error) => Result.Failure<FeedbackReplyOutcome>(error);

    [LoggerMessage(3108, LogLevel.Warning,
        "Feedback reply {ReplyId}: the send failed ({ErrorType}, {Disposition})")]
    private partial void LogSendFailed(Guid replyId, string errorType, EmailDeliveryDisposition disposition);

    [LoggerMessage(3109, LogLevel.Warning,
        "Feedback reply {ReplyId}: the row was removed while it was being sent; its outcome is not recorded")]
    private partial void LogRemovedDuringSend(Guid replyId);
}
