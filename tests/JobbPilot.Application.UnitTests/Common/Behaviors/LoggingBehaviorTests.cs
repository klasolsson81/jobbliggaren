using JobbPilot.Application.Common.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Behaviors;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, string>> _logger =
        Substitute.For<ILogger<LoggingBehavior<TestCommand, string>>>();

    [Fact]
    public async Task Handle_WithSuccessfulNext_ReturnsResponseAndDoesNotThrow()
    {
        var behavior = new LoggingBehavior<TestCommand, string>(_logger);
        var command = new TestCommand("test");
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("ok");

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.ShouldBe("ok");
    }

    [Fact]
    public async Task Handle_WithExceptionFromNext_RethrowsException()
    {
        var behavior = new LoggingBehavior<TestCommand, string>(_logger);
        var command = new TestCommand("test");
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => throw new InvalidOperationException("boom");

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(command, next, CancellationToken.None).AsTask());
    }
}
