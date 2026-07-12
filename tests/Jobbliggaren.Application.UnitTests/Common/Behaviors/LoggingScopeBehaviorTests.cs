using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public class LoggingScopeBehaviorTests
{
    private readonly ICorrelationIdProvider _correlationIdProvider =
        Substitute.For<ICorrelationIdProvider>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILogger<LoggingScopeBehavior<TestCommand, string>> _logger =
        Substitute.For<ILogger<LoggingScopeBehavior<TestCommand, string>>>();

    private LoggingScopeBehavior<TestCommand, string> CreateBehavior() =>
        new(_correlationIdProvider, _currentUser, _logger);

    [Fact]
    public async Task Handle_OpensScopeWithExactlyCorrelationUserAndOperationType()
    {
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _correlationIdProvider.Current.Returns(correlationId);
        _currentUser.UserId.Returns(userId);
        // PII must NEVER be scoped (security-auditor STEG 6). The port no longer even
        // exposes Email (#822) — the exact-key assertion below is the standing guard.

        Dictionary<string, object?>? captured = null;
        _logger.BeginScope(Arg.Do<Dictionary<string, object?>>(d => captured = d))
            .Returns(_ => Substitute.For<IDisposable>());

        MessageHandlerDelegate<TestCommand, string> next = (_, _) => ValueTask.FromResult("ok");
        var result = await CreateBehavior().Handle(new TestCommand("x"), next, CancellationToken.None);

        result.ShouldBe("ok");
        captured.ShouldNotBeNull();
        captured.Keys.ShouldBe(["CorrelationId", "UserId", "OperationType"], ignoreOrder: true);
        captured["CorrelationId"].ShouldBe(correlationId);
        captured["UserId"].ShouldBe(userId);
        captured["OperationType"].ShouldBe(nameof(TestCommand));
    }

    [Fact]
    public async Task Handle_AnonymousUser_ScopesNullUserIdWithoutThrowing()
    {
        _correlationIdProvider.Current.Returns(Guid.NewGuid());
        _currentUser.UserId.Returns((Guid?)null);

        Dictionary<string, object?>? captured = null;
        _logger.BeginScope(Arg.Do<Dictionary<string, object?>>(d => captured = d))
            .Returns(_ => Substitute.For<IDisposable>());

        MessageHandlerDelegate<TestCommand, string> next = (_, _) => ValueTask.FromResult("ok");
        var result = await CreateBehavior().Handle(new TestCommand("x"), next, CancellationToken.None);

        result.ShouldBe("ok");
        captured.ShouldNotBeNull();
        captured["UserId"].ShouldBeNull();
    }

    [Fact]
    public async Task Handle_PropagatesExceptionFromNext()
    {
        _correlationIdProvider.Current.Returns(Guid.NewGuid());
        _currentUser.UserId.Returns(Guid.NewGuid());

        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => throw new InvalidOperationException("boom");

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateBehavior().Handle(new TestCommand("x"), next, CancellationToken.None).AsTask());
    }
}
