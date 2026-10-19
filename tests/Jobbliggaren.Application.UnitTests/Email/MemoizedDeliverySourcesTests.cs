using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.UnitTests.Sessions;
using Jobbliggaren.Infrastructure.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The two delivery memos (#1981 PR 2): statistics held per window, the sentinel as a window of its own; failures held
/// per period and, being masked recipients, dropped at their reuse deadline; counts never dropped early.
/// </summary>
public class MemoizedDeliverySourcesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeStatistics(MutableFakeDateTimeProvider clock) : IDeliveryStatisticsAttemptSource
    {
        public List<StatisticsWindow> Windows { get; } = [];

        public ValueTask<ProviderAttempt<EmailDeliveryStatisticsRead>> AttemptAsync(
            StatisticsWindow window, CancellationToken cancellationToken)
        {
            Windows.Add(window);
            return ValueTask.FromResult(new ProviderAttempt<EmailDeliveryStatisticsRead>(
                new EmailDeliveryStatisticsRead.Observed(Windows.Count, 0, 0, clock.UtcNow)));
        }
    }

    private sealed class FakeFailures(MutableFakeDateTimeProvider clock, bool fail = false) : IRecentFailuresAttemptSource
    {
        public List<EmailDeliveryPeriod> Periods { get; } = [];

        public ValueTask<ProviderAttempt<EmailRecentFailuresRead>> AttemptAsync(
            EmailDeliveryPeriod period, CancellationToken cancellationToken)
        {
            Periods.Add(period);
            EmailRecentFailuresRead read = fail
                ? new EmailRecentFailuresRead.Failed(EmailProviderReadReason.RateLimited)
                : new EmailRecentFailuresRead.Observed(
                    [new EmailFailure(clock.UtcNow.AddMinutes(-Periods.Count), MaskedEmailAddress.Of("zelda@example.com"), EmailFailureReason.Spam)],
                    clock.UtcNow);
            return ValueTask.FromResult(new ProviderAttempt<EmailRecentFailuresRead>(read));
        }
    }

    // ---- statistics ----

    [Fact]
    public async Task Statistics_ShouldHoldOneReadPerWindow_AndTheSentinelAsAWindowOfItsOwn()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var inner = new FakeStatistics(clock);
        var memo = new MemoizedEmailDeliveryStatisticsSource(inner, clock);

        await memo.ReadAsync(EmailDeliveryPeriod.Last24Hours, Ct);
        await memo.ReadAsync(EmailDeliveryPeriod.Last3Days, Ct);
        await memo.ReadAsync(EmailDeliveryPeriod.Last7Days, Ct);
        await memo.ReadSentinelAsync(Ct);
        await memo.ReadAsync(EmailDeliveryPeriod.Last24Hours, Ct);
        await memo.ReadSentinelAsync(Ct);

        inner.Windows.ShouldBe(
            [StatisticsWindow.Last24Hours, StatisticsWindow.Last3Days, StatisticsWindow.Last7Days, StatisticsWindow.Sentinel]);
    }

    [Fact]
    public async Task Statistics_ShouldReadAgain_SixtySecondsAfterTheAnswer()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var inner = new FakeStatistics(clock);
        var memo = new MemoizedEmailDeliveryStatisticsSource(inner, clock);

        await memo.ReadSentinelAsync(Ct);
        clock.UtcNow = T0.AddSeconds(59);
        await memo.ReadSentinelAsync(Ct);
        clock.UtcNow = T0.AddSeconds(60);
        await memo.ReadSentinelAsync(Ct);

        inner.Windows.ShouldBe([StatisticsWindow.Sentinel, StatisticsWindow.Sentinel]);
    }

    [Fact]
    public async Task Statistics_ShouldRefuseAnUndefinedPeriod()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var memo = new MemoizedEmailDeliveryStatisticsSource(new FakeStatistics(clock), clock);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await memo.ReadAsync((EmailDeliveryPeriod)42, Ct));
    }

    // ---- failures ----

    [Fact]
    public async Task Failures_ShouldHoldOneReadPerPeriod()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var inner = new FakeFailures(clock);
        var memo = new MemoizedEmailRecentFailuresSource(inner, clock, _ => new TaskCompletionSource().Task);

        var day = await memo.ReadAsync(EmailDeliveryPeriod.Last24Hours, Ct);
        await memo.ReadAsync(EmailDeliveryPeriod.Last7Days, Ct);
        (await memo.ReadAsync(EmailDeliveryPeriod.Last24Hours, Ct)).ShouldBeSameAs(day);

        inner.Periods.ShouldBe([EmailDeliveryPeriod.Last24Hours, EmailDeliveryPeriod.Last7Days]);
    }

    [Fact]
    public async Task AnObservedList_ShouldBeDroppedAtItsReuseDeadline_EvenIfNoOneAsksAgain()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var inner = new FakeFailures(clock);
        var delays = new List<(TimeSpan Delay, TaskCompletionSource Elapsed)>();
        var memo = new MemoizedEmailRecentFailuresSource(inner, clock, delay =>
        {
            var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            delays.Add((delay, elapsed));
            return elapsed.Task;
        });

        await memo.ReadAsync(EmailDeliveryPeriod.Last3Days, Ct);
        var (delay, elapsed) = delays.ShouldHaveSingleItem();
        delay.ShouldBe(EmailProviderReadLimits.ReuseFor);

        elapsed.SetResult();
        clock.UtcNow = T0.AddSeconds(1);
        for (var i = 0; i < 100 && inner.Periods.Count < 2; i++)
        {
            await memo.ReadAsync(EmailDeliveryPeriod.Last3Days, Ct);
            await Task.Delay(20, Ct);
        }

        inner.Periods.Count.ShouldBe(2, "a dropped list is read again, never served from memory");
    }

    [Fact]
    public async Task AFailedRead_ShouldNotBeScheduledForEviction()
    {
        var clock = new MutableFakeDateTimeProvider { UtcNow = T0 };
        var scheduled = 0;
        var memo = new MemoizedEmailRecentFailuresSource(new FakeFailures(clock, fail: true), clock, _ =>
        {
            scheduled++;
            return Task.CompletedTask;
        });

        await memo.ReadAsync(EmailDeliveryPeriod.Last24Hours, Ct);

        scheduled.ShouldBe(0);
    }

    [Fact]
    public void TheFailuresMemo_ShouldOfferTheContainerOnePublicConstructor_WithoutTheDelay()
    {
        var constructor = typeof(MemoizedEmailRecentFailuresSource).GetConstructors().ShouldHaveSingleItem();

        constructor.GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(IRecentFailuresAttemptSource), typeof(Jobbliggaren.Domain.Common.IDateTimeProvider)]);
    }
}
