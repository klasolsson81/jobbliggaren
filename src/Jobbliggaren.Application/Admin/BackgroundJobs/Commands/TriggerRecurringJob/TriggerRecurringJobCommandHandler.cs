using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// Triggers an ad-hoc run of a recurring job via the <see cref="IBackgroundJobController"/>
/// port (Api-implemented; keeps Application Hangfire-free). Allowlist membership is
/// already enforced by <c>TriggerRecurringJobCommandValidator</c> (runs before this
/// handler in the Mediator pipeline), so the handler only delegates and echoes the id.
/// </summary>
public sealed class TriggerRecurringJobCommandHandler(IBackgroundJobController controller)
    : ICommandHandler<TriggerRecurringJobCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(
        TriggerRecurringJobCommand command, CancellationToken cancellationToken)
    {
        var jobId = await controller.TriggerRecurringAsync(command.RecurringJobId, cancellationToken);
        return Result.Success(jobId);
    }
}
