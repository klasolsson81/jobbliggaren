using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Behaviors;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public class UnitOfWorkBehaviorTests
{
    private readonly IAppDbContext _dbContext = Substitute.For<IAppDbContext>();

    [Fact]
    public async Task Handle_ForCommand_CallsSaveChangesAfterNext()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_dbContext);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForCommand_ReturnsSameResponseAsNext()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_dbContext);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("expected");

        var result = await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        result.ShouldBe("expected");
    }

    // Reconciler-port atomicity, F2 leg (CTO bind 2026-07-17, ADR 0093 §D5(b)
    // amendment): the handler-level throw witnesses pin that a reconciler throw
    // PROPAGATES out of Handle; THIS test pins the other leg of the composed rollback
    // guarantee — a throwing next() reaches the caller and the unconditional save never
    // runs, so tracked mutations die with the scope. It lives here, at the behavior,
    // because the handler unit tests bypass the pipeline and cannot prove it.
    [Fact]
    public async Task Handle_WhenNextThrows_DoesNotCallSaveChangesAsync()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_dbContext);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => throw new InvalidOperationException("boom");

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand("x"), next, CancellationToken.None).AsTask());

        await _dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
