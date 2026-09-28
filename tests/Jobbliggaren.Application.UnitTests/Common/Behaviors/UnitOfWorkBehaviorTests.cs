using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public class UnitOfWorkBehaviorTests
{
    private readonly IAppDbContext _dbContext = Substitute.For<IAppDbContext>();

    private UnitOfWorkBehavior<TCommand, string> BehaviorFor<TCommand>()
        where TCommand : ICommand<string> =>
        new(_dbContext, NullLogger<UnitOfWorkBehavior<TCommand, string>>.Instance);

    [Fact]
    public async Task Handle_ForCommand_CallsSaveChangesAfterNext()
    {
        var behavior = BehaviorFor<TestCommand>();
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForCommand_ReturnsSameResponseAsNext()
    {
        var behavior = BehaviorFor<TestCommand>();
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
        var behavior = BehaviorFor<TestCommand>();
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => throw new InvalidOperationException("boom");

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand("x"), next, CancellationToken.None).AsTask());

        await _dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ADR 0146 — the replay, in the order it must happen: a conflicting save is followed by a
    // tracker clear, and only then by the re-run, whose own response is the one returned.
    [Fact]
    public async Task Handle_ForMarkedCommand_WhenTheFirstSaveConflicts_ClearsTrackingThenReRunsAndReturnsTheReRunsResponse()
    {
        var calls = new List<string>();
        var attempt = 0;
        _dbContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("save");
            return attempt == 1
                ? Task.FromException<int>(new DbUpdateConcurrencyException("conflict"))
                : Task.FromResult(1);
        });
        _dbContext.When(db => db.ClearTracking()).Do(_ => calls.Add("clear"));
        MessageHandlerDelegate<TestReplayableCommand, string> next = (_, _) =>
        {
            attempt++;
            calls.Add("next");
            return ValueTask.FromResult($"attempt {attempt}");
        };

        var result = await BehaviorFor<TestReplayableCommand>()
            .Handle(new TestReplayableCommand("x"), next, CancellationToken.None);

        result.ShouldBe("attempt 2");
        calls.ShouldBe(["next", "save", "clear", "next", "save"]);
    }

    // E4/E5 of security-auditor's signature: at least three attempts in all, and on exhaustion the
    // tracker is cleared once more so nothing of the last attempt is left to commit.
    [Fact]
    public async Task Handle_ForMarkedCommand_WhenEverySaveConflicts_ThrowsConcurrencyConflictAfterThreeAttempts()
    {
        var calls = new List<string>();
        _dbContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("save");
            return Task.FromException<int>(new DbUpdateConcurrencyException("conflict"));
        });
        _dbContext.When(db => db.ClearTracking()).Do(_ => calls.Add("clear"));
        MessageHandlerDelegate<TestReplayableCommand, string> next = (_, _) =>
        {
            calls.Add("next");
            return ValueTask.FromResult("ok");
        };

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => BehaviorFor<TestReplayableCommand>()
                .Handle(new TestReplayableCommand("x"), next, CancellationToken.None).AsTask());

        calls.ShouldBe(["next", "save", "clear", "next", "save", "clear", "next", "save", "clear"]);
    }

    [Fact]
    public async Task Handle_ForUnmarkedCommand_WhenTheSaveConflicts_PropagatesTheConflictWithoutReRunning()
    {
        _dbContext.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict"));
        var runs = 0;
        MessageHandlerDelegate<TestCommand, string> next = (_, _) =>
        {
            runs++;
            return ValueTask.FromResult("ok");
        };

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => BehaviorFor<TestCommand>().Handle(new TestCommand("x"), next, CancellationToken.None).AsTask());

        runs.ShouldBe(1);
        _dbContext.DidNotReceive().ClearTracking();
    }

    // Only a lost optimistic-concurrency race is replayed. Any other failed save (a constraint
    // violation, say) would fail the same way on a fresh read.
    [Fact]
    public async Task Handle_ForMarkedCommand_WhenTheSaveFailsForAnotherReason_PropagatesItWithoutReRunning()
    {
        _dbContext.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("unique violation"));
        var runs = 0;
        MessageHandlerDelegate<TestReplayableCommand, string> next = (_, _) =>
        {
            runs++;
            return ValueTask.FromResult("ok");
        };

        await Should.ThrowAsync<DbUpdateException>(
            () => BehaviorFor<TestReplayableCommand>()
                .Handle(new TestReplayableCommand("x"), next, CancellationToken.None).AsTask());

        runs.ShouldBe(1);
        _dbContext.DidNotReceive().ClearTracking();
    }
}
