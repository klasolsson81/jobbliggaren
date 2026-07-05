using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

// A re-auth-gated test message (marker). Non-marker coverage reuses TestCommand (TestMessages.cs).
public sealed record ReauthenticatingTestCommand(string? Password)
    : ICommand<string>, IReauthenticatingRequest;

/// <summary>
/// <see cref="ReauthenticationBehavior{TMessage,TResponse}"/> gates every
/// <see cref="IReauthenticatingRequest"/> in the shared Mediator pipeline (PR2c/C5, epik #481). Pins:
/// a marker + Success flows to <c>next</c>; a marker + failure throws
/// <see cref="ReauthenticationFailedException"/> and SKIPS <c>next</c> (so handler/UoW/audit never
/// run); a non-marker passes straight through untouched; and a marker in a host with NO
/// <see cref="IReauthenticationService"/> registered (Worker shape) is a hard
/// <see cref="InvalidOperationException"/>, never a silent pass.
/// </summary>
public class ReauthenticationBehaviorTests
{
    private readonly IReauthenticationService _service = Substitute.For<IReauthenticationService>();

    private static (MessageHandlerDelegate<TMessage, string> Next, Func<bool> WasCalled) TrackingNext<TMessage>()
        where TMessage : IMessage
    {
        var called = false;
        MessageHandlerDelegate<TMessage, string> next = (_, _) =>
        {
            called = true;
            return ValueTask.FromResult("ok");
        };
        return (next, () => called);
    }

    [Fact]
    public async Task Handle_WhenMarkerAndReauthSucceeds_InvokesNextAndFlowsResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        _service.VerifyCurrentUserPasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var behavior = new ReauthenticationBehavior<ReauthenticatingTestCommand, string>([_service]);
        var (next, wasCalled) = TrackingNext<ReauthenticatingTestCommand>();

        var result = await behavior.Handle(new ReauthenticatingTestCommand("pwd"), next, ct);

        result.ShouldBe("ok");
        wasCalled().ShouldBeTrue();
        await _service.Received(1).VerifyCurrentUserPasswordAsync("pwd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenMarkerAndReauthFails_ThrowsAndDoesNotInvokeNext()
    {
        var ct = TestContext.Current.CancellationToken;
        _service.VerifyCurrentUserPasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt.")));
        var behavior = new ReauthenticationBehavior<ReauthenticatingTestCommand, string>([_service]);
        var (next, wasCalled) = TrackingNext<ReauthenticatingTestCommand>();

        await Should.ThrowAsync<ReauthenticationFailedException>(
            () => behavior.Handle(new ReauthenticatingTestCommand("wrong"), next, ct).AsTask());

        // Handler / UnitOfWork commit / audit row are all skipped on a failed re-auth.
        wasCalled().ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenNonMarkerMessage_InvokesNextAndNeverCallsService()
    {
        var ct = TestContext.Current.CancellationToken;
        var behavior = new ReauthenticationBehavior<TestCommand, string>([_service]);
        var (next, wasCalled) = TrackingNext<TestCommand>();

        var result = await behavior.Handle(new TestCommand("x"), next, ct);

        result.ShouldBe("ok");
        wasCalled().ShouldBeTrue();
        await _service.DidNotReceive().VerifyCurrentUserPasswordAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenMarkerAndNoServiceRegistered_ThrowsInvalidOperation()
    {
        var ct = TestContext.Current.CancellationToken;
        // Worker-shape composition: the behavior is constructed for every message in both hosts, but
        // the Worker registers no IReauthenticationService. A marker reaching that host is a
        // misconfiguration, not a silent pass — it must throw.
        var behavior = new ReauthenticationBehavior<ReauthenticatingTestCommand, string>([]);
        var (next, wasCalled) = TrackingNext<ReauthenticatingTestCommand>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new ReauthenticatingTestCommand("pwd"), next, ct).AsTask());
        wasCalled().ShouldBeFalse();
    }
}
