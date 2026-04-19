using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.Common.Behaviors;

public sealed partial class LoggingBehavior<TMessage, TResponse>(ILogger<LoggingBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var messageName = typeof(TMessage).Name;
        LogHandling(messageName);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next(message, cancellationToken);
            sw.Stop();
            LogHandled(messageName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogFailed(messageName, sw.ElapsedMilliseconds, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {MessageName}")]
    private partial void LogHandling(string messageName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {MessageName} in {ElapsedMs}ms")]
    private partial void LogHandled(string messageName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed handling {MessageName} after {ElapsedMs}ms")]
    private partial void LogFailed(string messageName, long elapsedMs, Exception ex);
}
