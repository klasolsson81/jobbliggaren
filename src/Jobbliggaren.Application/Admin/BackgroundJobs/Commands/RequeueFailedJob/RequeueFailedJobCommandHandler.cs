using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// Requeues a failed job via the <see cref="IBackgroundJobController"/> port. The
/// port resolves the existence + Failed-state precondition (it owns the Hangfire
/// monitoring read) and returns a BCL <see cref="RequeueOutcome"/>; this handler
/// maps that to a domain-correct result: NotFound (404) for an unknown job,
/// Conflict (409) for a job that exists but is not Failed. A failed Result means
/// AuditBehavior writes no audit row (no false "requeued" record).
/// </summary>
public sealed class RequeueFailedJobCommandHandler(IBackgroundJobController controller)
    : ICommandHandler<RequeueFailedJobCommand, Result<bool>>
{
    public async ValueTask<Result<bool>> Handle(
        RequeueFailedJobCommand command, CancellationToken cancellationToken)
    {
        var outcome = await controller.RequeueAsync(command.JobId, cancellationToken);

        return outcome switch
        {
            RequeueOutcome.Requeued => Result.Success(true),
            RequeueOutcome.JobNotFound => Result.Failure<bool>(DomainError.NotFound(
                "RequeueFailedJob.NotFound",
                "Inget jobb med det id:t finns i Hangfire-storage.")),
            RequeueOutcome.NotInFailedState => Result.Failure<bool>(DomainError.Conflict(
                "RequeueFailedJob.NotFailed",
                "Jobbet är inte i Failed-state och kan inte köras om.")),
            _ => Result.Failure<bool>(DomainError.Validation(
                "RequeueFailedJob.UnknownOutcome",
                "Okänt utfall vid omkörning av jobbet.")),
        };
    }
}
