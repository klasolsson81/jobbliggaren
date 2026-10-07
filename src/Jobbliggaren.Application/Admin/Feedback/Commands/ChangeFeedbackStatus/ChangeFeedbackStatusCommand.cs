using FluentValidation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;

/// <summary>Moves a submission to Ny, Pågår, Åtgärdad or Avstår (#1979). Audited: the row carries no actor.</summary>
public sealed record ChangeFeedbackStatusCommand(Guid Id, FeedbackStatus Status)
    : ICommand<Result>, IAdminRequest, IAuditableCommand<Result>
{
    public string EventType => "Admin.FeedbackStatusChanged";
    public string AggregateType => "Feedback";
    public Guid ExtractAggregateId(Result response) => Id;
}

public sealed class ChangeFeedbackStatusCommandValidator : AbstractValidator<ChangeFeedbackStatusCommand>
{
    public ChangeFeedbackStatusCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Status).IsInEnum();
    }
}

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
