using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Common.Behaviors;

public sealed partial class UnitOfWorkBehavior<TCommand, TResponse>(
    IAppDbContext dbContext,
    ILogger<UnitOfWorkBehavior<TCommand, TResponse>> logger)
    : IPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    // ADR 0146 — attempts in all for an IReplayOnConcurrencyConflict command. security-auditor's
    // signature requires at least three, one cap for a withdrawal and a grant alike.
    private const int MaxAttempts = 3;

    public async ValueTask<TResponse> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is not IReplayOnConcurrencyConflict)
        {
            var response = await next(message, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return response;
        }

        for (var attempt = 1; ; attempt++)
        {
            var response = await next(message, cancellationToken);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return response;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Clear and re-run is the only resolution. Refreshing the original values and saving
                // again ("client wins") would write this attempt's stale document over the commit that
                // beat it.
                dbContext.ClearTracking();
                if (attempt == MaxAttempts)
                    throw new ConcurrencyConflictException();
                LogReplay(typeof(TCommand).Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{CommandName} lost a concurrency race; re-running it on a fresh read")]
    private partial void LogReplay(string commandName);
}
