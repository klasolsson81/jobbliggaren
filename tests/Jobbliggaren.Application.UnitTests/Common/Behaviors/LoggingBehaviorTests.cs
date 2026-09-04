using Jobbliggaren.Application.Common.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, string>> _logger =
        Substitute.For<ILogger<LoggingBehavior<TestCommand, string>>>();

    public LoggingBehaviorTests() =>
        // Without this, the substitute answers IsEnabled=false, the [LoggerMessage]-generated
        // methods return before calling Log at all, and BOTH level assertions below pass no
        // matter what the behaviour does — the positive one on a stub that never logs, the
        // negative one vacuously. Stubbing it makes the level the only thing under test.
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

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

    [Fact]
    public async Task Handle_WithExceptionFromNext_LogsAtErrorWithTheException()
    {
        // #1633 — this is the Error channel a genuine database failure now arrives on. EF Core's
        // own CommandError/SaveChangesFailed events were moved to Information in AddPersistence,
        // because they fired on every duplicate key the ingest path absorbs BY DESIGN. That fix
        // is only safe while this behaviour keeps reporting a real failure at Error, with the
        // exception attached — so the guarantee it leans on is pinned here rather than assumed.
        var behavior = new LoggingBehavior<TestCommand, string>(_logger);
        var boom = new InvalidOperationException("boom");
        MessageHandlerDelegate<TestCommand, string> next = (_, _) => throw boom;

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand("test"), next, CancellationToken.None).AsTask());

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            boom,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_WithSuccessfulNext_LogsNothingAtError()
    {
        // The discriminator. Without it, the assertion above could not tell "failures are logged
        // at Error" from "everything is". The absorbed duplicate-key path takes exactly this
        // route — the handler swallows the DbUpdateException, so this behaviour sees a SUCCESS
        // and must leave the Error channel untouched.
        var behavior = new LoggingBehavior<TestCommand, string>(_logger);
        MessageHandlerDelegate<TestCommand, string> next = (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        _logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }
}
