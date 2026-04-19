using FluentValidation;
using Mediator;

namespace JobbPilot.Application.Common.Behaviors;

public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(message, cancellationToken);

        var context = new ValidationContext<TMessage>(message);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());

        if (errors.Count > 0)
            throw new Exceptions.ValidationException(errors);

        return await next(message, cancellationToken);
    }
}
