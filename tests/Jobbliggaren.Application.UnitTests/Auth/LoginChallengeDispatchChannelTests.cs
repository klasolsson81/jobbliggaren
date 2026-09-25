using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The login challenge's bounded queue, and through it the shared <c>BoundedDispatchChannel&lt;T&gt;</c>
/// mechanism: it takes items up to its capacity, drops without blocking once full, logs the drop by capacity
/// alone, and lets a draining reader finish after <c>Complete</c>. The shutdown drain is pinned by
/// LoginChallengeDispatchServiceShutdownTests.
/// </summary>
public sealed class LoginChallengeDispatchChannelTests
{
    private static LoginChallengeDispatchChannel Sut(int capacity, CapturingLogger logger) =>
        new(Options.Create(new LoginChallengeDispatchOptions { Capacity = capacity }), logger);

    private static LoginChallengeDispatch Item(string email) =>
        new(ChallengeId.Generate(), email, CodeBudgetState.Admitted, "203.0.113.0", "probe/1.0");

    [Fact]
    public void Enqueue_accepts_up_to_capacity_and_drops_nothing()
    {
        var logger = new CapturingLogger();
        var sut = Sut(2, logger);

        sut.Enqueue(Item("a@example.se"));
        sut.Enqueue(Item("b@example.se"));

        // Under DropWrite the write is accepted either way, so the queue's own count and the absent drop line
        // are what show that both items are held.
        sut.Reader.Count.ShouldBe(2);
        logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_lets_a_draining_reader_finish_rather_than_hang()
    {
        // Shutdown drains what is already queued. Without Complete() the consumer's ReadAllAsync would wait for
        // items that never come and the host would sit out its shutdown timeout.
        var sut = Sut(4, new CapturingLogger());
        sut.Enqueue(Item("a@example.se"));

        sut.Complete();

        // Still buffered, so completion has NOT arrived: the drain is real, not a discard.
        sut.Reader.Completion.IsCompleted.ShouldBeFalse();

        sut.Reader.TryRead(out var read).ShouldBeTrue();
        read.ShouldNotBeNull();
        await sut.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Enqueue_returns_at_once_on_a_full_queue()
    {
        var sut = Sut(1, new CapturingLogger());
        sut.Enqueue(Item("a@example.se"));

        var enqueue = Task.Run(() => sut.Enqueue(Item("b@example.se")), TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(
            enqueue, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        finished.ShouldBe(enqueue, "a blocking enqueue would put a load-dependent delay on the request path");
    }

    [Fact]
    public void A_drop_logs_its_own_event_with_the_capacity_and_no_address()
    {
        var logger = new CapturingLogger();
        var sut = Sut(1, logger);
        sut.Enqueue(Item("first@example.se"));

        sut.Enqueue(Item("dropped@example.se"));

        var (level, eventId, message) = logger.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1009);
        message.ShouldContain("capacity 1");
        message.ShouldNotContain("@example.se");
    }

    [Fact]
    public void The_ordinary_path_logs_nothing()
    {
        var logger = new CapturingLogger();

        Sut(4, logger).Enqueue(Item("a@example.se"));

        logger.Records.ShouldBeEmpty();
    }

    private sealed class CapturingLogger : ILogger<LoginChallengeDispatchChannel>
    {
        public List<(LogLevel Level, int EventId, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, eventId.Id, formatter(state, exception)));
    }
}
