using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The two delivery queries (#1981 PR 2): each is an admin request, refuses a missing or undefined period, reads what
/// its evaluator needs at once, and passes the caller's token to every read.
/// </summary>
public class GetEmailDeliveryQueryHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeStatistics(
        Func<EmailDeliveryPeriod, ValueTask<EmailDeliveryStatisticsRead>>? read = null,
        Func<ValueTask<EmailDeliveryStatisticsRead>>? sentinel = null) : IEmailDeliveryStatisticsSource
    {
        public List<EmailDeliveryPeriod> Periods { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int SentinelReads { get; private set; }

        public ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken)
        {
            Periods.Add(period);
            Tokens.Add(cancellationToken);
            return read?.Invoke(period)
                ?? ValueTask.FromResult<EmailDeliveryStatisticsRead>(new EmailDeliveryStatisticsRead.Observed(12, 10, 2, At));
        }

        public ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken)
        {
            SentinelReads++;
            Tokens.Add(cancellationToken);
            return sentinel?.Invoke()
                ?? ValueTask.FromResult<EmailDeliveryStatisticsRead>(new EmailDeliveryStatisticsRead.Observed(149, 140, 9, At));
        }
    }

    private sealed class FakeFailures(Func<ValueTask<EmailRecentFailuresRead>>? read = null) : IEmailRecentFailuresSource
    {
        public List<EmailDeliveryPeriod> Periods { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask<EmailRecentFailuresRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken)
        {
            Periods.Add(period);
            Tokens.Add(cancellationToken);
            return read?.Invoke() ?? ValueTask.FromResult<EmailRecentFailuresRead>(new EmailRecentFailuresRead.Observed(
                [new EmailFailure(At.AddHours(-1), MaskedEmailAddress.Of("zelda@example.com"), EmailFailureReason.HardBounce)], At));
        }
    }

    // ---- the queries ----

    [Fact]
    public void BothQueries_ShouldBeAdminRequests()
    {
        new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last24Hours).ShouldBeAssignableTo<IAdminRequest>();
        new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last24Hours).ShouldBeAssignableTo<IAdminRequest>();
    }

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours)]
    [InlineData(EmailDeliveryPeriod.Last3Days)]
    [InlineData(EmailDeliveryPeriod.Last7Days)]
    public void TheValidators_ShouldAcceptEveryPeriod(EmailDeliveryPeriod period)
    {
        new GetEmailDeliveryQueryValidator().Validate(new GetEmailDeliveryQuery(period)).IsValid.ShouldBeTrue();
        new GetEmailRecentFailuresQueryValidator().Validate(new GetEmailRecentFailuresQuery(period)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    [InlineData(-1)]
    public void TheValidators_ShouldRefuseAMissingOrUndefinedPeriod(int? period)
    {
        var value = (EmailDeliveryPeriod?)period;

        var delivery = new GetEmailDeliveryQueryValidator().Validate(new GetEmailDeliveryQuery(value));
        var failures = new GetEmailRecentFailuresQueryValidator().Validate(new GetEmailRecentFailuresQuery(value));

        delivery.IsValid.ShouldBeFalse();
        delivery.Errors.ShouldContain(e => e.PropertyName == nameof(GetEmailDeliveryQuery.Period));
        failures.IsValid.ShouldBeFalse();
        failures.Errors.ShouldContain(e => e.PropertyName == nameof(GetEmailRecentFailuresQuery.Period));
    }

    // ---- delivery ----

    [Fact]
    public async Task Delivery_ShouldReadThePeriodAndTheSentinel_AndShowTheTotalsTheCanaryPasses()
    {
        var statistics = new FakeStatistics();

        var dto = await new GetEmailDeliveryQueryHandler(statistics)
            .Handle(new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last3Days), Ct);

        statistics.Periods.ShouldBe([EmailDeliveryPeriod.Last3Days]);
        statistics.SentinelReads.ShouldBe(1);
        dto.Period.ShouldBe(EmailDeliveryPeriod.Last3Days);
        dto.Status.ShouldBe(EmailDeliveryStatus.Observed);
        dto.Sent.ShouldBe(10);
        dto.Failed.ShouldBe(2);
    }

    [Fact]
    public async Task Delivery_ShouldBeNotObservable_WhenTheSentinelCountsNothing()
    {
        var statistics = new FakeStatistics(sentinel: () =>
            ValueTask.FromResult<EmailDeliveryStatisticsRead>(new EmailDeliveryStatisticsRead.Observed(0, 0, 0, At)));

        var dto = await new GetEmailDeliveryQueryHandler(statistics)
            .Handle(new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last7Days), Ct);

        dto.Status.ShouldBe(EmailDeliveryStatus.NotObservable);
        dto.Sent.ShouldBeNull();
    }

    [Fact]
    public async Task Delivery_ShouldStartTheSentinel_BeforeThePeriodReadCompletes()
    {
        var gate = new TaskCompletionSource<EmailDeliveryStatisticsRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statistics = new FakeStatistics(read: _ => new ValueTask<EmailDeliveryStatisticsRead>(gate.Task));

        var handling = new GetEmailDeliveryQueryHandler(statistics)
            .Handle(new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last24Hours), Ct).AsTask();

        statistics.SentinelReads.ShouldBe(1, "the sentinel must not wait for the period read");
        gate.SetResult(new EmailDeliveryStatisticsRead.Observed(1, 1, 0, At));
        (await handling).Sent.ShouldBe(1);
    }

    [Fact]
    public async Task Delivery_ShouldPassTheCallersToken_ToBothReads()
    {
        var statistics = new FakeStatistics();
        using var cancellation = new CancellationTokenSource();

        await new GetEmailDeliveryQueryHandler(statistics)
            .Handle(new GetEmailDeliveryQuery(EmailDeliveryPeriod.Last24Hours), cancellation.Token);

        statistics.Tokens.Count.ShouldBe(2);
        statistics.Tokens.ShouldAllBe(token => token == cancellation.Token);
    }

    [Fact]
    public async Task Delivery_ShouldRefuseAQueryWithoutAPeriod_IfTheValidatorWasBypassed() =>
        await Should.ThrowAsync<ArgumentException>(async () =>
            await new GetEmailDeliveryQueryHandler(new FakeStatistics()).Handle(new GetEmailDeliveryQuery(null), Ct));

    // ---- recent failures ----

    [Fact]
    public async Task Failures_ShouldReadTheListThePeriodsStatisticsAndTheSentinel_AndShowTheMaskedList()
    {
        var statistics = new FakeStatistics();
        var failures = new FakeFailures();

        var dto = await new GetEmailRecentFailuresQueryHandler(failures, statistics)
            .Handle(new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last7Days), Ct);

        failures.Periods.ShouldBe([EmailDeliveryPeriod.Last7Days]);
        statistics.Periods.ShouldBe([EmailDeliveryPeriod.Last7Days]);
        statistics.SentinelReads.ShouldBe(1);
        dto.Status.ShouldBe(EmailDeliveryStatus.Observed);
        var failure = dto.Failures.ShouldNotBeNull().ShouldHaveSingleItem();
        failure.Recipient.ShouldBe("z•••@example.com");
        failure.Reason.ShouldBe(EmailFailureReason.HardBounce);
    }

    [Fact]
    public async Task Failures_ShouldNotTrustAnEmptyList_WhenThePeriodsStatisticsCountAFailure()
    {
        var failures = new FakeFailures(() =>
            ValueTask.FromResult<EmailRecentFailuresRead>(new EmailRecentFailuresRead.Observed([], At)));

        var dto = await new GetEmailRecentFailuresQueryHandler(failures, new FakeStatistics())
            .Handle(new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last24Hours), Ct);

        dto.Status.ShouldBe(EmailDeliveryStatus.NotObservable);
        dto.Failures.ShouldBeNull();
    }

    [Fact]
    public async Task Failures_ShouldStartAllThreeReads_BeforeTheListCompletes()
    {
        var gate = new TaskCompletionSource<EmailRecentFailuresRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statistics = new FakeStatistics();
        var failures = new FakeFailures(() => new ValueTask<EmailRecentFailuresRead>(gate.Task));

        var handling = new GetEmailRecentFailuresQueryHandler(failures, statistics)
            .Handle(new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last3Days), Ct).AsTask();

        statistics.Periods.Count.ShouldBe(1);
        statistics.SentinelReads.ShouldBe(1);
        gate.SetResult(new EmailRecentFailuresRead.Failed(EmailProviderReadReason.Timeout));
        (await handling).Reason.ShouldBe(EmailProviderReadReason.Timeout);
    }

    [Fact]
    public async Task Failures_ShouldPassTheCallersToken_ToEveryRead()
    {
        var statistics = new FakeStatistics();
        var failures = new FakeFailures();
        using var cancellation = new CancellationTokenSource();

        await new GetEmailRecentFailuresQueryHandler(failures, statistics)
            .Handle(new GetEmailRecentFailuresQuery(EmailDeliveryPeriod.Last24Hours), cancellation.Token);

        failures.Tokens.ShouldHaveSingleItem().ShouldBe(cancellation.Token);
        statistics.Tokens.Count.ShouldBe(2);
        statistics.Tokens.ShouldAllBe(token => token == cancellation.Token);
    }

    [Fact]
    public async Task Failures_ShouldRefuseAQueryWithoutAPeriod_IfTheValidatorWasBypassed() =>
        await Should.ThrowAsync<ArgumentException>(async () =>
            await new GetEmailRecentFailuresQueryHandler(new FakeFailures(), new FakeStatistics())
                .Handle(new GetEmailRecentFailuresQuery(null), Ct));
}
