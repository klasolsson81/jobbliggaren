using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1171 — the bounded dispatch channel. Three properties, and each exists because its opposite is a
/// live defect rather than a style preference:
/// <list type="bullet">
/// <item><b>Bounded and DROPPING, never blocking.</b> A blocking enqueue on a full queue puts a
/// load-dependent delay back on an unauthenticated endpoint — the same class of timing channel this
/// whole design removes, one step sideways, plus a self-inflicted latency DoS. An unbounded queue is a
/// memory-exhaustion surface behind an endpoint whose per-IP limit parallelises trivially, which is
/// the position <c>RateLimitingExtensions</c> already took with <c>QueueLimit = 0</c>.</item>
/// <item><b>The drop is observable to an operator</b> and to nobody else.</item>
/// <item><b>Enqueue never inspects the address</b>, which is what makes its cost independent of
/// whether the account exists.</item>
/// </list>
/// </summary>
public sealed class PasswordResetDispatchChannelTests
{
    private static PasswordResetDispatchChannel Sut(int capacity, ILogger<PasswordResetDispatchChannel>? logger = null)
        => new(
            Options.Create(new PasswordResetDispatchOptions { Capacity = capacity }),
            logger ?? new CapturingLogger());

    private static PasswordResetDispatch Item(string email = "a@example.se")
        => new(email, "203.0.113.0", "probe/1.0");

    [Fact]
    public void TryEnqueue_accepts_up_to_capacity()
    {
        var sut = Sut(capacity: 2);

        sut.TryEnqueue(Item("a@example.se")).ShouldBeTrue();
        sut.TryEnqueue(Item("b@example.se")).ShouldBeTrue();
    }

    [Fact]
    public async Task TryEnqueue_returns_promptly_rather_than_blocking_once_full()
    {
        // THE load-bearing property, and the assertion is about TIME, not about the return value. If a
        // full queue ever blocks, the handler's call becomes load-dependent and the endpoint has a
        // timing channel again — which is the very thing this design removes. The timeout crosses that
        // threshold: a blocking implementation never completes the task and the wait expires.
        //
        // The RETURN is deliberately not asserted false here. Under BoundedChannelFullMode.DropWrite
        // the BCL returns TRUE on a full queue and discards the new item; the drop is observable only
        // through the itemDropped callback (see the test below). An earlier draft of this class read
        // the bool as "queued" and this suite caught it.
        var sut = Sut(capacity: 1);
        sut.TryEnqueue(Item("a@example.se")).ShouldBeTrue();

        var enqueue = Task.Run(() => sut.TryEnqueue(Item("b@example.se")));
        var finished = await Task.WhenAny(
            enqueue, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        finished.ShouldBe(
            enqueue,
            "a full queue must return at once — a blocking enqueue reintroduces the latency channel");
    }

    [Fact]
    public void TryEnqueue_logs_a_full_queue_at_Warning_without_the_address()
    {
        // A dropped request sends no mail and the caller is never told, so the operator signal is the
        // only one there is. It must NOT carry the address: the line is written before any lookup, so
        // it is byte-identical for an existing and a non-existent account and leaks nothing.
        var logger = new CapturingLogger();
        var sut = Sut(capacity: 1, logger);
        sut.TryEnqueue(Item("first@example.se"));

        sut.TryEnqueue(Item("dropped@example.se"));

        var (level, message) = logger.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        message.ShouldNotContain("dropped@example.se");
        message.ShouldNotContain("first@example.se");
        message.ShouldContain("capacity 1");
    }

    [Fact]
    public void TryEnqueue_logs_nothing_on_the_ordinary_path()
    {
        // The counterfactual for the test above: without it, a channel that logged on EVERY enqueue
        // would satisfy it — and would put one Warning per forgot-password request into a durable sink.
        var logger = new CapturingLogger();
        var sut = Sut(capacity: 4, logger);

        sut.TryEnqueue(Item()).ShouldBeTrue();

        logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_lets_a_draining_reader_finish_rather_than_hang()
    {
        // Shutdown drains what is already queued. Without Complete() the consumer's ReadAllAsync would
        // wait for more items that will never come and the host would sit out its shutdown timeout.
        var sut = Sut(capacity: 4);
        sut.TryEnqueue(Item());

        sut.Complete();

        // Still buffered, so completion has NOT arrived — the drain is real, not a discard.
        sut.Reader.Completion.IsCompleted.ShouldBeFalse();

        sut.Reader.TryRead(out var read).ShouldBeTrue();
        read.ShouldNotBeNull();
        await sut.Reader.Completion.WaitAsync(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private sealed class CapturingLogger : ILogger<PasswordResetDispatchChannel>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }
}
