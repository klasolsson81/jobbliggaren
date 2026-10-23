using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — what the per-kind region may show: every kind in a fixed order, the latest failing date with
/// every class of that date, the coverage measured against the recording start, and the gate that refuses a zero the
/// provider's own count for the same period contradicts.
/// </summary>
public sealed class EmailSendCountsEvaluatorTests
{
    private static readonly DateOnly From = new(2026, 10, 16);
    private static readonly DateTimeOffset FromStart = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedLongBefore = FromStart.AddDays(-30);
    private static readonly DateTimeOffset At = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    private static EmailSendCountsRead.Observed Counts(params EmailSendCountRow[] rows) => new(rows, StartedLongBefore);

    private static EmailSendCountRow Row(int day, EmailKind kind, EmailSendOutcome outcome, int attempts) =>
        new(new DateOnly(2026, 10, day), kind, outcome, attempts);

    private static EmailSendCountsDto Evaluate(EmailSendCountsRead own, EmailDeliveryStatisticsRead? witness = null) =>
        EmailSendCountsEvaluator.Evaluate(EmailDeliveryPeriod.Last7Days, From, FromStart, own, witness);

    [Fact]
    public void KindOrder_IsEveryKindOnce_AccountMailsFirst()
    {
        EmailSendCountsEvaluator.KindOrder.ShouldBe(
        [
            EmailKind.LoginChallenge,
            EmailKind.AccountEmailChangeRequestedNotification,
            EmailKind.EmailChangedNotification,
            EmailKind.MatchNotification,
            EmailKind.FollowedCompanyNotification,
            EmailKind.FeedbackReceivedNotification,
            EmailKind.FeedbackReply,
        ]);
        EmailSendCountsEvaluator.KindOrder.ShouldBe(Enum.GetValues<EmailKind>(), ignoreOrder: true);
    }

    [Fact]
    public void NothingCounted_IsNotObserved_AndAsksNoWitness()
    {
        EmailSendCountsEvaluator.NeedsWitness(new EmailSendCountsRead.NotConfigured()).ShouldBeFalse();

        var dto = Evaluate(new EmailSendCountsRead.NotConfigured());

        dto.Status.ShouldBe(EmailSendCountsStatus.NotObserved);
        dto.From.ShouldBeNull();
        dto.Kinds.ShouldBeNull();
    }

    [Fact]
    public void Counts_SumAcceptedAndFailedPerKind_AndNameTheLatestFailingDateWithEveryClassOfIt()
    {
        var own = Counts(
            Row(17, EmailKind.LoginChallenge, EmailSendOutcome.Accepted, 5),
            Row(18, EmailKind.LoginChallenge, EmailSendOutcome.Unreachable, 1),
            Row(20, EmailKind.LoginChallenge, EmailSendOutcome.Timeout, 2),
            Row(20, EmailKind.LoginChallenge, EmailSendOutcome.Refused, 1),
            Row(21, EmailKind.LoginChallenge, EmailSendOutcome.Accepted, 3),
            Row(19, EmailKind.MatchNotification, EmailSendOutcome.Accepted, 4));

        EmailSendCountsEvaluator.NeedsWitness(own).ShouldBeFalse();
        var dto = Evaluate(own);

        dto.Status.ShouldBe(EmailSendCountsStatus.Observed);
        dto.From.ShouldBe(From);
        dto.Kinds.ShouldNotBeNull().Select(k => k.Kind).ShouldBe(EmailSendCountsEvaluator.KindOrder);
        var login = dto.Kinds![0];
        login.Accepted.ShouldBe(8);
        login.Failed.ShouldBe(4);
        login.LatestFailure.ShouldNotBeNull().Day.ShouldBe(new DateOnly(2026, 10, 20));
        login.LatestFailure.Outcomes.ShouldBe([EmailSendOutcome.Refused, EmailSendOutcome.Timeout], "display order");
        var match = dto.Kinds[3];
        match.Accepted.ShouldBe(4);
        match.Failed.ShouldBe(0);
        match.LatestFailure.ShouldBeNull();
        dto.Kinds.Where(k => k.Kind is not EmailKind.LoginChallenge and not EmailKind.MatchNotification)
            .ShouldAllBe(k => k.Accepted == 0 && k.Failed == 0 && k.LatestFailure == null);
    }

    [Theory]
    [InlineData(-30, "Complete")]
    [InlineData(0, "Complete")]
    [InlineData(1, "Partial")]
    public void Coverage_IsMeasuredAgainstTheStartOfTheFirstDate(int startedMinutesAfterFromStart, string coverage)
    {
        var started = FromStart.AddMinutes(startedMinutesAfterFromStart);
        var own = new EmailSendCountsRead.Observed([Row(22, EmailKind.FeedbackReply, EmailSendOutcome.Accepted, 1)], started);

        var dto = Evaluate(own);

        dto.Coverage.ShouldBe(Enum.Parse<EmailSendCountsCoverage>(coverage));
        dto.CoveredFrom.ShouldBe(coverage == "Partial" ? started : null);
    }

    // ---- the gate ----

    [Fact]
    public void AZeroTotal_NeedsAWitness_AndRefusesToBeShownWithout()
    {
        EmailSendCountsEvaluator.NeedsWitness(Counts()).ShouldBeTrue();

        Should.Throw<ArgumentException>(() => Evaluate(Counts(), witness: null));
    }

    [Fact]
    public void AZeroTotal_IsNotObservable_WhenTheProviderCountedASendInThePeriod()
    {
        var dto = Evaluate(Counts(), new EmailDeliveryStatisticsRead.Observed(1, 1, 0, At));

        dto.Status.ShouldBe(EmailSendCountsStatus.NotObservable);
        dto.From.ShouldBe(From);
        dto.Kinds.ShouldBeNull();
        dto.Coverage.ShouldBeNull();
    }

    public static TheoryData<string, EmailDeliveryStatisticsRead> NoWitness => new()
    {
        { "the provider sent nothing", new EmailDeliveryStatisticsRead.Observed(2, 0, 2, At) },
        { "no read key", new EmailDeliveryStatisticsRead.ReadKeyMissing() },
        { "the read failed", new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.Timeout) },
    };

    [Theory]
    [MemberData(nameof(NoWitness))]
    public void AZeroTotal_IsShownAsZero_WhenTheProviderIsNoWitnessAgainstIt(
        string because, EmailDeliveryStatisticsRead witness)
    {
        var dto = Evaluate(Counts(), witness);

        dto.Status.ShouldBe(EmailSendCountsStatus.Observed, because);
        dto.Kinds.ShouldNotBeNull().ShouldAllBe(k => k.Accepted == 0 && k.Failed == 0 && k.LatestFailure == null);
    }

    [Fact]
    public void AnyCount_IsShownWithoutAskingTheProvider()
    {
        var own = Counts(Row(23, EmailKind.FeedbackReceivedNotification, EmailSendOutcome.Other, 1));

        EmailSendCountsEvaluator.NeedsWitness(own).ShouldBeFalse();
        Evaluate(own, witness: null).Status.ShouldBe(EmailSendCountsStatus.Observed);
    }

    // ---- the shapes refuse what cannot be true ----

    [Fact]
    public void ACountRow_RefusesAnUnknownKindOrOutcomeAndANonPositiveCount()
    {
        Should.Throw<ArgumentException>(() => new EmailSendCountRow(From, (EmailKind)99, EmailSendOutcome.Accepted, 1));
        Should.Throw<ArgumentException>(() => new EmailSendCountRow(From, EmailKind.LoginChallenge, (EmailSendOutcome)99, 1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new EmailSendCountRow(From, EmailKind.LoginChallenge, EmailSendOutcome.Accepted, 0));
    }

    [Fact]
    public void AKindCount_HasALatestFailureExactlyWhenItHasAFailure()
    {
        var latest = new EmailLatestFailureDto(From, [EmailSendOutcome.Other]);

        Should.Throw<ArgumentException>(() => new EmailKindCountDto(EmailKind.LoginChallenge, 1, 0, latest));
        Should.Throw<ArgumentException>(() => new EmailKindCountDto(EmailKind.LoginChallenge, 1, 1, null));
    }

    [Fact]
    public void ALatestFailure_RefusesNoClassARepeatedClassAndAccepted()
    {
        Should.Throw<ArgumentException>(() => new EmailLatestFailureDto(From, []));
        Should.Throw<ArgumentException>(() =>
            new EmailLatestFailureDto(From, [EmailSendOutcome.Refused, EmailSendOutcome.Refused]));
        Should.Throw<ArgumentException>(() => new EmailLatestFailureDto(From, [EmailSendOutcome.Accepted]));
    }

    [Fact]
    public void TheObservedShape_RefusesKindsOutOfOrder()
        => Should.Throw<ArgumentException>(() => EmailSendCountsDto.Observed(
            EmailDeliveryPeriod.Last24Hours,
            From,
            null,
            [.. EmailSendCountsEvaluator.KindOrder.Reverse().Select(k => new EmailKindCountDto(k, 0, 0, null))]));
}
