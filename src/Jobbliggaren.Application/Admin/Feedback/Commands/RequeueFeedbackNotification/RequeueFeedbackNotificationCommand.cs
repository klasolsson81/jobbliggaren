using FluentValidation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// An administrator sends a submission's notice again (#1979). From Failed nothing was ever sent;
/// from Unknown the earlier mail may have arrived, so <see cref="AcknowledgeDuplicateRisk"/> must be
/// set — the domain refuses otherwise. Audited, and replayed on a race with the dispatch job.
/// </summary>
public sealed record RequeueFeedbackNotificationCommand(Guid Id, bool AcknowledgeDuplicateRisk)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>, IReplayOnConcurrencyConflict
{
    public string EventType => "Admin.FeedbackNotificationRequeued";
    public string AggregateType => "Feedback";
    public Guid ExtractAggregateId(Result response) => Id;
}

public sealed class RequeueFeedbackNotificationCommandValidator : AbstractValidator<RequeueFeedbackNotificationCommand>
{
    public RequeueFeedbackNotificationCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

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
