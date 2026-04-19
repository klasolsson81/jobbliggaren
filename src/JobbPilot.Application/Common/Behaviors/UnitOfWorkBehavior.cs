using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Common.Behaviors;

public sealed class UnitOfWorkBehavior<TCommand, TResponse>(IAppDbContext dbContext)
    : IPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }
}
