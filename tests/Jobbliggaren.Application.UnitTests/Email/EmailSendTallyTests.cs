using System.Diagnostics;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email.Counting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181, security-auditor 5d and condition 4) — the count can never change the send it follows: every
/// way the write can fail ends inside <see cref="EmailSendTally.RecordAsync"/>, within its budget, as one Warning that
/// carries the exception's type name and a SQLSTATE and nothing the exception says.
/// </summary>
public sealed class EmailSendTallyTests
{
    private const string Sentinel = "SENTINEL-recipient@example.com";

    /// <summary>The budget plus a margin for a loaded CI runner. The bound itself is the tally's own timer.</summary>
    private static readonly TimeSpan WithinTheBudget = EmailSendTally.Budget + TimeSpan.FromSeconds(2);

    private readonly CapturingLogger _logger = new();

    public static TheoryData<string, Func<CancellationToken, Task>> FailingWrites => new()
    {
        { "throws before it returns", _ => throw SentinelException() },
        { "throws after an await", async _ => { await Task.Yield(); throw SentinelException(); } },
        { "faults its task", _ => Task.FromException(SentinelException()) },
        { "never finishes and ignores its token", _ => new TaskCompletionSource().Task },
        {
            "is cancelled by its own budget",
            async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        },
    };

    [Theory]
    [MemberData(nameof(FailingWrites))]
    public async Task RecordAsync_WhateverTheWriteDoes_ReturnsWithinTheBudgetAndLogsOneWarning(
        string because, Func<CancellationToken, Task> write)
    {
        var sut = new ScriptedEmailSendTally(_logger, write);
        var clock = Stopwatch.StartNew();

        await Should.NotThrowAsync(async () => await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted));

        clock.Elapsed.ShouldBeLessThan(WithinTheBudget, because);
        var warning = _logger.Records.ShouldHaveSingleItem(because);
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventId.Id.ShouldBe(3009);
        warning.Message.ShouldContain("login-challenge");
        warning.Message.ShouldContain(nameof(EmailSendOutcome.Accepted));
    }

    [Theory]
    [MemberData(nameof(FailingWrites))]
    public async Task RecordAsync_AFailedWrite_NeverHandsTheExceptionOrItsMessageToTheLogger(
        string because, Func<CancellationToken, Task> write)
    {
        var sut = new ScriptedEmailSendTally(_logger, write);

        await sut.RecordAsync(EmailKind.FeedbackReply, EmailSendOutcome.Refused);

        var warning = _logger.Records.ShouldHaveSingleItem(because);
        warning.Exception.ShouldBeNull("the exception object would put its message, inner messages and stack in the sink");
        warning.Surface.ShouldNotContain(Sentinel, Case.Insensitive, because);
        warning.Surface.ShouldNotContain("INNER", Case.Sensitive, because);
    }

    [Fact]
    public async Task RecordAsync_APostgresError_LogsItsSqlStateAndTypeButNotItsText()
    {
        var sut = new ScriptedEmailSendTally(
            _logger, _ => throw new PostgresException(Sentinel, "ERROR", "ERROR", "23514"));

        await sut.RecordAsync(EmailKind.MatchNotification, EmailSendOutcome.Timeout);

        var warning = _logger.Records.ShouldHaveSingleItem();
        warning.Message.ShouldContain("23514");
        warning.Message.ShouldContain(nameof(PostgresException));
        warning.Surface.ShouldNotContain(Sentinel, Case.Insensitive);
    }

    [Fact]
    public async Task RecordAsync_AnyOtherError_LogsAFixedPlaceholderForTheSqlState()
    {
        var sut = new ScriptedEmailSendTally(_logger, _ => throw SentinelException());

        await sut.RecordAsync(EmailKind.MatchNotification, EmailSendOutcome.Accepted);

        _logger.Records.ShouldHaveSingleItem().Message.ShouldContain("(SQLSTATE -)");
    }

    [Fact]
    public async Task RecordAsync_AWriteThatSucceeds_LogsNothing()
    {
        var sut = new ScriptedEmailSendTally(_logger, _ => Task.CompletedTask);

        await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);

        sut.Writes.ShouldBe(1);
        _logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecordAsync_AnUndefinedKind_StillLogsWithoutThrowing()
    {
        var sut = new ScriptedEmailSendTally(_logger, _ => throw SentinelException());

        await Should.NotThrowAsync(async () => await sut.RecordAsync((EmailKind)99, EmailSendOutcome.Accepted));

        _logger.Records.ShouldHaveSingleItem().Message.ShouldContain("99");
    }

    /// <summary>The budget's token reaches the write, so a write that honours it is stopped rather than abandoned.</summary>
    [Fact]
    public async Task RecordAsync_HandsTheWriteATokenThatFiresAtTheBudget()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new ScriptedEmailSendTally(_logger, async ct =>
        {
            await using var registration = ct.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);

        await cancelled.Task.WaitAsync(WithinTheBudget, TestContext.Current.CancellationToken);
    }

    /// <summary>The sender outlives its host's provider at shutdown: the write's scope cannot be created.</summary>
    [Fact]
    public async Task RecordAsync_OverADisposedProvider_ReturnsAndLogsTheDisposal()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        await provider.DisposeAsync();
        var sut = new SqlEmailSendTally(scopes, new TypedLogger<SqlEmailSendTally>(_logger));

        await Should.NotThrowAsync(async () => await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted));

        var warning = _logger.Records.ShouldHaveSingleItem();
        warning.EventId.Id.ShouldBe(3009);
        warning.Message.ShouldContain(nameof(ObjectDisposedException));
        warning.Exception.ShouldBeNull();
    }

    private sealed class TypedLogger<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private static InvalidOperationException SentinelException() =>
        new($"write failed for {Sentinel}", new InvalidOperationException($"INNER {Sentinel}"));
}
