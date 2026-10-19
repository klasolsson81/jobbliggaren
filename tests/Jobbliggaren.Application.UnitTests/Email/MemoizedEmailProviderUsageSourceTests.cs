using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The memo in front of the provider read (#1981, CTO addendum B): how long each outcome is reused, that one read is
/// in flight at a time, and that one caller's cancellation never cancels the read the others are waiting on.
/// </summary>
public class MemoizedEmailProviderUsageSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 19, 30, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Clock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class FakeAttempts(
        Func<int, ValueTask<ProviderAttempt<EmailProviderUsageRead>>> next,
        Func<int, ValueTask<ProviderAttempt<EmailProviderPlanRead>>>? nextPlan = null) : IProviderUsageAttemptSource
    {
        public int Attempts { get; private set; }

        public int PlanAttempts { get; private set; }

        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask<ProviderAttempt<EmailProviderUsageRead>> AttemptUsageAsync(CancellationToken cancellationToken)
        {
            Tokens.Add(cancellationToken);
            return next(++Attempts);
        }

        public ValueTask<ProviderAttempt<EmailProviderPlanRead>> AttemptPlanAsync(CancellationToken cancellationToken)
        {
            PlanAttempts++;
            return nextPlan?.Invoke(PlanAttempts)
                ?? ValueTask.FromResult(new ProviderAttempt<EmailProviderPlanRead>(
                    new EmailProviderPlanRead.Observed(EmailPlan.Essential, T0)));
        }
    }

    private static ProviderAttempt<EmailProviderUsageRead> ObservedAt(DateTimeOffset at, long count = 184) =>
        new(new EmailProviderUsageRead.Observed(count, 0, at));

    private static ProviderAttempt<EmailProviderUsageRead> FailedWith(TimeSpan? retryAfter = null) =>
        new(new EmailProviderUsageRead.Failed(EmailProviderReadReason.RateLimited), retryAfter);

    // ---- success ----

    [Fact]
    public async Task ASuccess_ShouldBeReused_UntilSixtySecondsAfterItsObservedAt_AndNotAfter()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(n => ValueTask.FromResult(ObservedAt(clock.UtcNow, count: n)));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        var first = (EmailProviderUsageRead.Observed)await memo.ReadUsageAsync(Ct);
        clock.UtcNow = T0.AddSeconds(59);
        var reused = (EmailProviderUsageRead.Observed)await memo.ReadUsageAsync(Ct);
        clock.UtcNow = T0.AddSeconds(60);
        var fresh = (EmailProviderUsageRead.Observed)await memo.ReadUsageAsync(Ct);

        reused.ShouldBeSameAs(first);
        fresh.MonthlyEmailsCount.ShouldBe(2);
        inner.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ASuccess_ShouldBeReusedFromItsOwnObservedAt_NotFromWhenItWasAskedFor()
    {
        // The provider's answer arrived 40 s before the clock reads now, so it has 20 s of reuse left.
        var clock = new Clock(T0);
        var inner = new FakeAttempts(n => ValueTask.FromResult(ObservedAt(T0.AddSeconds(-40), count: n)));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        await memo.ReadUsageAsync(Ct);
        clock.UtcNow = T0.AddSeconds(21);
        await memo.ReadUsageAsync(Ct);

        inner.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ASynchronouslyCompletedRead_ShouldStillExpire()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(n => ValueTask.FromResult(ObservedAt(clock.UtcNow, count: n)));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        for (var minute = 0; minute < 3; minute++)
        {
            clock.UtcNow = T0.AddMinutes(minute);
            await memo.ReadUsageAsync(Ct);
        }

        inner.Attempts.ShouldBe(3);
    }

    // ---- failure ----

    [Fact]
    public async Task AFailure_ShouldBeReusedForSixtySeconds_WhenTheProviderSentNoRetryAfter()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(_ => ValueTask.FromResult(FailedWith()));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        await memo.ReadUsageAsync(Ct);
        clock.UtcNow = T0.AddSeconds(59);
        await memo.ReadUsageAsync(Ct);
        inner.Attempts.ShouldBe(1);

        clock.UtcNow = T0.AddSeconds(60);
        await memo.ReadUsageAsync(Ct);
        inner.Attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(10, 60)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    [InlineData(3_600, 900)]
    public async Task AFailure_ShouldBeHeldForTheProvidersRetryAfter_WithinItsFloorAndCap(int retryAfterSeconds, int heldSeconds)
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(_ => ValueTask.FromResult(FailedWith(TimeSpan.FromSeconds(retryAfterSeconds))));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        await memo.ReadUsageAsync(Ct);
        clock.UtcNow = T0.AddSeconds(heldSeconds - 1);
        await memo.ReadUsageAsync(Ct);
        inner.Attempts.ShouldBe(1);

        clock.UtcNow = T0.AddSeconds(heldSeconds);
        await memo.ReadUsageAsync(Ct);
        inner.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task AReadThatThrows_ShouldReachTheCaller_AndNotBeReused()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(n => n == 1
            ? throw new InvalidOperationException("adapter broke its contract")
            : ValueTask.FromResult(ObservedAt(clock.UtcNow)));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        await Should.ThrowAsync<InvalidOperationException>(async () => await memo.ReadUsageAsync(Ct));
        var next = await memo.ReadUsageAsync(Ct);

        next.ShouldBeOfType<EmailProviderUsageRead.Observed>();
        inner.Attempts.ShouldBe(2);
    }

    // ---- two reads, remembered apart ----

    [Fact]
    public async Task TheCountAndThePlan_ShouldBeRememberedApart_SoOneFailingNeverBlanksTheOther()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(
            _ => ValueTask.FromResult(FailedWith()),
            _ => ValueTask.FromResult(new ProviderAttempt<EmailProviderPlanRead>(
                new EmailProviderPlanRead.Observed(EmailPlan.Essential, clock.UtcNow))));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        (await memo.ReadUsageAsync(Ct)).ShouldBeOfType<EmailProviderUsageRead.Failed>();
        (await memo.ReadPlanAsync(Ct)).ShouldBeOfType<EmailProviderPlanRead.Observed>();
        clock.UtcNow = T0.AddSeconds(30);
        await memo.ReadUsageAsync(Ct);
        await memo.ReadPlanAsync(Ct);

        inner.Attempts.ShouldBe(1);
        inner.PlanAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task APlanSuccess_ShouldExpireSixtySecondsAfterItsOwnObservedAt()
    {
        var clock = new Clock(T0);
        var inner = new FakeAttempts(
            _ => ValueTask.FromResult(ObservedAt(clock.UtcNow)),
            _ => ValueTask.FromResult(new ProviderAttempt<EmailProviderPlanRead>(
                new EmailProviderPlanRead.Observed(EmailPlan.Essential, clock.UtcNow))));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        await memo.ReadPlanAsync(Ct);
        clock.UtcNow = T0.AddSeconds(59);
        await memo.ReadPlanAsync(Ct);
        clock.UtcNow = T0.AddSeconds(60);
        await memo.ReadPlanAsync(Ct);

        inner.PlanAttempts.ShouldBe(2);
    }

    // ---- one read in flight ----

    [Fact]
    public async Task ConcurrentCallers_ShouldShareOneRead()
    {
        var clock = new Clock(T0);
        var release = new TaskCompletionSource<ProviderAttempt<EmailProviderUsageRead>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeAttempts(_ => new ValueTask<ProviderAttempt<EmailProviderUsageRead>>(release.Task));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);

        var callers = Enumerable.Range(0, 5).Select(_ => memo.ReadUsageAsync(Ct).AsTask()).ToArray();
        release.SetResult(ObservedAt(T0));
        var reads = await Task.WhenAll(callers);

        inner.Attempts.ShouldBe(1);
        reads.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OneCallersCancellation_ShouldEndOnlyItsOwnWait_AndTheSharedReadRunsOnNone()
    {
        var clock = new Clock(T0);
        var release = new TaskCompletionSource<ProviderAttempt<EmailProviderUsageRead>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeAttempts(_ => new ValueTask<ProviderAttempt<EmailProviderUsageRead>>(release.Task));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);
        using var leaving = new CancellationTokenSource();

        var leaver = memo.ReadUsageAsync(leaving.Token).AsTask();
        var stayer = memo.ReadUsageAsync(Ct).AsTask();
        await leaving.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await leaver);

        release.SetResult(ObservedAt(T0));
        (await stayer).ShouldBeOfType<EmailProviderUsageRead.Observed>();

        inner.Attempts.ShouldBe(1);
        inner.Tokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
    }

    [Fact]
    public async Task ACallerWhoLeftEarly_ShouldStillHaveFilledTheMemo_ForTheNextCaller()
    {
        var clock = new Clock(T0);
        var release = new TaskCompletionSource<ProviderAttempt<EmailProviderUsageRead>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeAttempts(_ => new ValueTask<ProviderAttempt<EmailProviderUsageRead>>(release.Task));
        var memo = new MemoizedEmailProviderUsageSource(inner, clock);
        using var leaving = new CancellationTokenSource();

        var leaver = memo.ReadUsageAsync(leaving.Token).AsTask();
        await leaving.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await leaver);
        release.SetResult(ObservedAt(T0));

        // The abandoned read completes on its own. The next caller either joins it or finds it remembered; both are one read.
        var next = await memo.ReadUsageAsync(Ct);

        next.ShouldBeOfType<EmailProviderUsageRead.Observed>();
        inner.Attempts.ShouldBe(1);
    }
}
