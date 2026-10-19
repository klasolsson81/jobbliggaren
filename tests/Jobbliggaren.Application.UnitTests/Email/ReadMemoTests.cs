using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.UnitTests.Sessions;
using Jobbliggaren.Infrastructure.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The memo's two additions for #1981 PR 2, tested once and generically: the opt-in eviction that leaves a read
/// carrying personal data unreferenced at its reuse deadline (security-auditor 4f), and the keyed memo with a fixed
/// key set, one hold and one single flight per key. The holds themselves are pinned in
/// <see cref="MemoizedEmailProviderUsageSourceTests"/>.
/// </summary>
public class ReadMemoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private enum Key
    {
        A,
        B,
    }

    private sealed record Answer(int N, DateTimeOffset? ObservedAt);

    /// <summary>Hands out a fresh answer per attempt and records which key each attempt was for.</summary>
    private sealed class Attempts(Func<int, Answer> answer)
    {
        public List<Key> Keys { get; } = [];

        public int Count => Keys.Count;

        public ValueTask<ProviderAttempt<Answer>> Next(Key key, CancellationToken cancellationToken)
        {
            Keys.Add(key);
            return ValueTask.FromResult(new ProviderAttempt<Answer>(answer(Keys.Count)));
        }

        public ValueTask<ProviderAttempt<Answer>> Next(CancellationToken cancellationToken) => Next(Key.A, cancellationToken);
    }

    /// <summary>
    /// Records each requested delay and lets the test decide when it has elapsed. With <paramref name="dropsInline"/> the
    /// delay's task runs its continuations synchronously, so completed off any synchronization context,
    /// <see cref="TaskCompletionSource.SetResult"/> returns only after the memo's drop has run.
    /// </summary>
    private sealed class Evictions(bool dropsInline = false)
    {
        public List<(TimeSpan Delay, TaskCompletionSource Elapsed)> Requested { get; } = [];

        public Task After(TimeSpan delay)
        {
            var elapsed = new TaskCompletionSource(
                dropsInline ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Requested)
            {
                Requested.Add((delay, elapsed));
            }

            return elapsed.Task;
        }
    }

    private static DateTimeOffset? ObservedAtOf(Answer answer) => answer.ObservedAt;

    // ---- eviction ----

    [Fact]
    public async Task AnObservedRead_ShouldBeDroppedAtItsReuseDeadline_WhenEvictionIsOn()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, clock.UtcNow));
        var evictions = new Evictions();
        var memo = new ReadMemo<Answer>(clock, attempts.Next, ObservedAtOf, evictions.After);

        (await memo.ReadAsync(Ct)).N.ShouldBe(1);
        var (delay, elapsed) = evictions.Requested.ShouldHaveSingleItem();
        delay.ShouldBe(EmailProviderReadLimits.ReuseFor);

        clock.UtcNow = T0.AddSeconds(10);
        (await memo.ReadAsync(Ct)).N.ShouldBe(1, "within the hold and before the deadline the read is reused");

        elapsed.SetResult();
        await WaitUntilAsync(async () => (await memo.ReadAsync(Ct)).N == 2);
        attempts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TheEvictionDelay_ShouldRunFromTheProvidersAnswer_NotFromWhenItWasAskedFor()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var evictions = new Evictions();
        var stale = new ReadMemo<Answer>(clock, new Attempts(n => new Answer(n, T0.AddSeconds(-40))).Next, ObservedAtOf, evictions.After);
        var older = new ReadMemo<Answer>(clock, new Attempts(n => new Answer(n, T0.AddMinutes(-5))).Next, ObservedAtOf, evictions.After);

        await stale.ReadAsync(Ct);
        await older.ReadAsync(Ct);

        evictions.Requested.Select(r => r.Delay).ShouldBe([TimeSpan.FromSeconds(20), TimeSpan.Zero]);
    }

    [Fact]
    public async Task AFailure_ShouldNeverBeEvicted()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, null));
        var evictions = new Evictions();
        var memo = new ReadMemo<Answer>(clock, attempts.Next, ObservedAtOf, evictions.After);

        await memo.ReadAsync(Ct);
        clock.UtcNow = T0.AddSeconds(59);
        await memo.ReadAsync(Ct);

        evictions.Requested.ShouldBeEmpty();
        attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnEntryReplacedBeforeItsEviction_ShouldNotBeDroppedByTheOldOnesDeadline()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, clock.UtcNow));
        var evictions = new Evictions(dropsInline: true);
        var memo = new ReadMemo<Answer>(clock, attempts.Next, ObservedAtOf, evictions.After);

        // On the thread pool, so neither the memo's await nor SetResult sees the test's synchronization context.
        await Task.Run(async () =>
        {
            await memo.ReadAsync(Ct);
            clock.UtcNow = T0.AddSeconds(60);
            (await memo.ReadAsync(Ct)).N.ShouldBe(2);
            evictions.Requested.Count.ShouldBe(2);

            evictions.Requested[0].Elapsed.SetResult();
            clock.UtcNow = T0.AddSeconds(70);
            (await memo.ReadAsync(Ct)).N.ShouldBe(2, "the old entry's deadline must leave the newer entry in place");
            attempts.Count.ShouldBe(2);

            // The control: the same completion drops the newer entry before SetResult returns, so the read above ran
            // after the old entry's drop had run, not before it.
            evictions.Requested[1].Elapsed.SetResult();
            (await memo.ReadAsync(Ct)).N.ShouldBe(3, "the newer entry is dropped at its own deadline");
        }, Ct);
    }

    [Fact]
    public async Task AnAbortedDelay_ShouldStillDropTheEntry()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, clock.UtcNow));
        var evictions = new Evictions();
        var memo = new ReadMemo<Answer>(clock, attempts.Next, ObservedAtOf, evictions.After);

        await memo.ReadAsync(Ct);
        evictions.Requested.ShouldHaveSingleItem().Elapsed.SetCanceled(Ct);

        await WaitUntilAsync(async () => (await memo.ReadAsync(Ct)).N == 2);
    }

    // ---- keyed ----

    [Fact]
    public async Task EachKey_ShouldBeHeldOnItsOwn()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, clock.UtcNow));
        var memo = new KeyedReadMemo<Key, Answer>(clock, attempts.Next, ObservedAtOf);

        var a = await memo.ReadAsync(Key.A, Ct);
        var b = await memo.ReadAsync(Key.B, Ct);
        (await memo.ReadAsync(Key.A, Ct)).ShouldBeSameAs(a);
        (await memo.ReadAsync(Key.B, Ct)).ShouldBeSameAs(b);

        attempts.Keys.ShouldBe([Key.A, Key.B]);
        a.ShouldNotBeSameAs(b);
    }

    [Fact]
    public async Task OneKeysFailure_ShouldNotBlankAnother()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, n == 1 ? null : clock.UtcNow));
        var memo = new KeyedReadMemo<Key, Answer>(clock, attempts.Next, ObservedAtOf);

        (await memo.ReadAsync(Key.A, Ct)).ObservedAt.ShouldBeNull();
        (await memo.ReadAsync(Key.B, Ct)).ObservedAt.ShouldBe(T0);
        (await memo.ReadAsync(Key.A, Ct)).ObservedAt.ShouldBeNull();

        attempts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConcurrentReadsOfOneKey_ShouldShareOneAttempt_AndAnotherKeyHasItsOwn()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = new List<Key>();
        var memo = new KeyedReadMemo<Key, Answer>(clock, async (key, _) =>
        {
            lock (keys)
            {
                keys.Add(key);
            }

            await release.Task;
            return new ProviderAttempt<Answer>(new Answer(keys.Count, T0));
        }, ObservedAtOf);

        var reads = Enumerable.Range(0, 4).Select(i => memo.ReadAsync(i % 2 == 0 ? Key.A : Key.B, Ct).AsTask()).ToArray();
        release.SetResult();
        var answers = await Task.WhenAll(reads);

        keys.Order().ShouldBe([Key.A, Key.B]);
        answers[0].ShouldBeSameAs(answers[2]);
        answers[1].ShouldBeSameAs(answers[3]);
    }

    [Fact]
    public async Task AnUndefinedKey_ShouldBeRefused()
    {
        var memo = new KeyedReadMemo<Key, Answer>(
            new MutableFakeDateTimeProvider { UtcNow = T0 }, new Attempts(n => new Answer(n, T0)).Next, ObservedAtOf);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await memo.ReadAsync((Key)7, Ct));
    }

    [Fact]
    public async Task EveryKey_ShouldEvict_WhenTheKeyedMemoIsGivenEviction()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var attempts = new Attempts(n => new Answer(n, clock.UtcNow));
        var evictions = new Evictions();
        var memo = new KeyedReadMemo<Key, Answer>(clock, attempts.Next, ObservedAtOf, evictions.After);

        await memo.ReadAsync(Key.A, Ct);
        await memo.ReadAsync(Key.B, Ct);

        evictions.Requested.Count.ShouldBe(2);
        evictions.Requested[1].Elapsed.SetResult();
        await WaitUntilAsync(async () => (await memo.ReadAsync(Key.B, Ct)).N == 3);
        (await memo.ReadAsync(Key.A, Ct)).N.ShouldBe(1, "evicting one key leaves the other");
    }

    /// <summary>The eviction runs on a continuation, so the test polls briefly for its effect.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20, Ct);
        }

        (await condition()).ShouldBeTrue("the entry was never dropped");
    }
}
