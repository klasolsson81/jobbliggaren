using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Time;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — the per-kind query: an admin request that refuses a missing or undefined period, reads every
/// Swedish date the period touches, and asks the provider for the same period only when its own total is zero, never
/// for the sentinel.
/// </summary>
public sealed class GetEmailSendCountsQueryHandlerTests
{
    // 10:00 UTC on 23 October 2026 is 12:00 in Stockholm (CEST).
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCounts(params EmailSendCountRow[] rows) : IEmailSendCountsSource
    {
        public List<(DateOnly First, DateOnly Last)> Windows { get; } = [];

        public ValueTask<EmailSendCountsRead> ReadAsync(
            DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
        {
            Windows.Add((firstDay, lastDay));
            return ValueTask.FromResult<EmailSendCountsRead>(new EmailSendCountsRead.Observed(rows, LongAgo));
        }
    }

    private sealed class FakeStatistics(EmailDeliveryStatisticsRead answer) : IEmailDeliveryStatisticsSource
    {
        public List<EmailDeliveryPeriod> Periods { get; } = [];

        public int SentinelReads { get; private set; }

        public ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken)
        {
            Periods.Add(period);
            return ValueTask.FromResult(answer);
        }

        public ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken)
        {
            SentinelReads++;
            return ValueTask.FromResult(answer);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static GetEmailSendCountsQueryHandler Handler(IEmailSendCountsSource counts, IEmailDeliveryStatisticsSource statistics) =>
        new(counts, statistics, new FixedClock(Now), new SwedishCalendar());

    [Fact]
    public void TheQuery_IsAnAdminRequest()
        => typeof(IAdminRequest).IsAssignableFrom(typeof(GetEmailSendCountsQuery)).ShouldBeTrue();

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours, "2026-10-22")]
    [InlineData(EmailDeliveryPeriod.Last3Days, "2026-10-20")]
    [InlineData(EmailDeliveryPeriod.Last7Days, "2026-10-16")]
    public async Task Handle_ReadsEverySwedishDateThePeriodTouches_ThroughToday(EmailDeliveryPeriod period, string first)
    {
        var counts = new FakeCounts(
            new EmailSendCountRow(new DateOnly(2026, 10, 23), EmailKind.LoginChallenge, EmailSendOutcome.Accepted, 1));

        var dto = await Handler(counts, new FakeStatistics(new EmailDeliveryStatisticsRead.NotConfigured()))
            .Handle(new GetEmailSendCountsQuery(period), Ct);

        counts.Windows.ShouldBe([(DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 10, 23))]);
        dto.From.ShouldBe(counts.Windows[0].First);
    }

    [Fact]
    public async Task Handle_WithACount_NeverAsksTheProvider()
    {
        var statistics = new FakeStatistics(new EmailDeliveryStatisticsRead.Observed(9, 9, 0, Now));

        await Handler(
                new FakeCounts(new EmailSendCountRow(new DateOnly(2026, 10, 23), EmailKind.FeedbackReply, EmailSendOutcome.Refused, 1)),
                statistics)
            .Handle(new GetEmailSendCountsQuery(EmailDeliveryPeriod.Last7Days), Ct);

        statistics.Periods.ShouldBeEmpty();
        statistics.SentinelReads.ShouldBe(0);
    }

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours)]
    [InlineData(EmailDeliveryPeriod.Last7Days)]
    public async Task Handle_WithAZeroTotal_AsksTheProviderForTheSamePeriodAndNeverForTheSentinel(EmailDeliveryPeriod period)
    {
        var statistics = new FakeStatistics(new EmailDeliveryStatisticsRead.Observed(3, 3, 0, Now));

        var dto = await Handler(new FakeCounts(), statistics).Handle(new GetEmailSendCountsQuery(period), Ct);

        statistics.Periods.ShouldBe([period]);
        statistics.SentinelReads.ShouldBe(0);
        dto.Status.ShouldBe(EmailSendCountsStatus.NotObservable);
    }

    [Fact]
    public async Task Handle_WithoutAPeriod_Throws_BecauseTheValidatorRefusesIt()
        => await Should.ThrowAsync<ArgumentException>(async () =>
            await Handler(new FakeCounts(), new FakeStatistics(new EmailDeliveryStatisticsRead.NotConfigured()))
                .Handle(new GetEmailSendCountsQuery(null), Ct));

    [Theory]
    [InlineData(null)]
    [InlineData((EmailDeliveryPeriod)42)]
    public void TheValidator_RefusesAMissingOrUndefinedPeriod(EmailDeliveryPeriod? period)
        => new GetEmailSendCountsQueryValidator().Validate(new GetEmailSendCountsQuery(period)).IsValid.ShouldBeFalse();

    [Fact]
    public void TheValidator_AcceptsEachPeriod()
        => Enum.GetValues<EmailDeliveryPeriod>()
            .ShouldAllBe(period => new GetEmailSendCountsQueryValidator().Validate(new GetEmailSendCountsQuery(period)).IsValid);
}
