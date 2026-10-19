using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Time;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The evaluator turns one provider read and the configured terms into the browser's closed union (#1981). It is
/// pure; the calendar is the real Stockholm one, because the month label is exactly where a hand-rolled zone would
/// be wrong.
/// </summary>
public class EmailAllowanceEvaluatorTests
{
    private static readonly SwedishCalendar Calendar = new();
    private static readonly DateTimeOffset MidMonth = new(2026, 10, 18, 19, 30, 0, TimeSpan.Zero);

    private static readonly EmailProviderPlanRead Essential = new EmailProviderPlanRead.Observed(EmailPlan.Essential, MidMonth);

    private static EmailAllowanceDto Evaluate(
        EmailProviderUsageRead read, EmailAllowanceOptions? options = null, EmailProviderPlanRead? plan = null) =>
        EmailAllowanceEvaluator.Evaluate(read, plan ?? Essential, options ?? new EmailAllowanceOptions(), Calendar);

    private static EmailProviderUsageRead.Observed Observed(long sent, int? webhooks = 0, DateTimeOffset? at = null) =>
        new EmailProviderUsageRead.Observed(sent, webhooks, at ?? MidMonth);

    // ---- terms ----

    [Fact]
    public void Evaluate_ShouldCarryTheConfiguredTerms_WhateverTheReadSays()
    {
        var options = new EmailAllowanceOptions { IncludedMonthlyEmails = 500, PricePerThousandExtra = 0.20m };

        foreach (var read in new EmailProviderUsageRead[]
                 {
                     Observed(10),
                     new EmailProviderUsageRead.NotConfigured(),
                     new EmailProviderUsageRead.Failed(EmailAllowanceReason.Timeout),
                 })
        {
            var terms = Evaluate(read, options).Terms;

            terms.Plan.ShouldBe(EmailPlan.Essential);
            terms.IncludedMonthlyEmails.ShouldBe(500);
            terms.PricePerThousandExtra.ShouldBe(0.20m);
            terms.Currency.ShouldBe("EUR");
            terms.PriceReadOn.ShouldBe(new DateOnly(2026, 10, 18));
            terms.MonthlySendLimit.ShouldBe(10_000);
            terms.CostCurrency.ShouldBe("SEK");
            terms.SekPerEur.ShouldBe(11.1450m);
            terms.ExchangeRateOn.ShouldBe(new DateOnly(2026, 10, 16));
        }
    }

    // ---- observed ----

    [Fact]
    public void Evaluate_ShouldReportZeroCost_WithinTheIncludedMails()
    {
        var usage = Evaluate(Observed(184)).Usage;

        usage.Status.ShouldBe(EmailAllowanceStatus.Observed);
        usage.Reason.ShouldBeNull();
        usage.Sent.ShouldBe(184);
        usage.Billable.ShouldBe(0);
        usage.EstimatedExtraCostOre.ShouldBe(0);
        usage.AboveHalfOfSendLimit.ShouldBe(false);
    }

    [Fact]
    public void Evaluate_ShouldComputeTheCostBeyondTheIncludedMails()
    {
        var usage = Evaluate(Observed(1_301)).Usage;

        usage.Billable.ShouldBe(1_001);
        usage.EstimatedExtraCostOre.ShouldBe(349);
    }

    [Fact]
    public void Evaluate_ShouldSayWhenTheCountIsAboveHalfTheSendLimit() =>
        Evaluate(Observed(5_001)).Usage.AboveHalfOfSendLimit.ShouldBe(true);

    [Fact]
    public void Evaluate_ShouldKeepTheAdaptersObservedAt() =>
        Evaluate(Observed(1, at: MidMonth)).Usage.ObservedAt.ShouldBe(MidMonth);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Evaluate_ShouldCarryTheWebhookCount(int webhooks) =>
        Evaluate(Observed(1, webhooks)).Usage.WebhooksCount.ShouldBe(webhooks);

    [Fact]
    public void Evaluate_ShouldLeaveAnUnreadableWebhookCountUnknown_NeverZero_AndKeepTheRest()
    {
        var usage = Evaluate(Observed(184, webhooks: null)).Usage;

        usage.Status.ShouldBe(EmailAllowanceStatus.Observed);
        usage.WebhooksCount.ShouldBeNull();
        usage.Sent.ShouldBe(184);
    }

    // ---- the plan guard ----

    [Fact]
    public void Evaluate_ShouldSayThePlanMatches_WhenTheProviderNamesTheConfiguredPlan()
    {
        var dto = Evaluate(Observed(184));

        dto.Plan.Status.ShouldBe(EmailAllowanceStatus.Observed);
        dto.Plan.Reason.ShouldBeNull();
        dto.Plan.Active.ShouldBe(EmailPlan.Essential);
        dto.Plan.Matches.ShouldBe(true);
    }

    [Theory]
    [InlineData(EmailPlan.Scale)]
    [InlineData(EmailPlan.Unknown)]
    public void Evaluate_ShouldWithholdTheCost_ButKeepTheCount_WhenThePlanInForceIsNotTheConfiguredOne(EmailPlan active)
    {
        var dto = Evaluate(Observed(1_301), plan: new EmailProviderPlanRead.Observed(active, MidMonth));

        dto.Plan.Active.ShouldBe(active);
        dto.Plan.Matches.ShouldBe(false);
        dto.Usage.Status.ShouldBe(EmailAllowanceStatus.Observed);
        dto.Usage.Sent.ShouldBe(1_301);
        dto.Usage.Billable.ShouldBeNull();
        dto.Usage.EstimatedExtraCostOre.ShouldBeNull();
        dto.Usage.AboveHalfOfSendLimit.ShouldBe(false);
    }

    [Fact]
    public void Evaluate_ShouldNeverLetAnUnknownPlanMatch_EvenIfTheOptionsSaidUnknown()
    {
        // Validation refuses Unknown as a configured plan; this pins that the evaluator does not lean on that alone.
        var options = new EmailAllowanceOptions { Plan = EmailPlan.Unknown };

        var dto = Evaluate(Observed(1), options, new EmailProviderPlanRead.Observed(EmailPlan.Unknown, MidMonth));

        dto.Plan.Matches.ShouldBe(false);
        dto.Usage.EstimatedExtraCostOre.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldWithholdTheCost_ButKeepTheCount_WhenThePlanReadFailed()
    {
        var dto = Evaluate(Observed(184), plan: new EmailProviderPlanRead.Failed(EmailAllowanceReason.Forbidden));

        dto.Plan.Status.ShouldBe(EmailAllowanceStatus.Failed);
        dto.Plan.Reason.ShouldBe(EmailAllowanceReason.Forbidden);
        dto.Plan.Active.ShouldBeNull();
        dto.Plan.Matches.ShouldBeNull();
        dto.Usage.Sent.ShouldBe(184);
        dto.Usage.EstimatedExtraCostOre.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldKeepThePlan_WhenOnlyTheCountFailed()
    {
        var dto = Evaluate(new EmailProviderUsageRead.Failed(EmailAllowanceReason.Timeout));

        dto.Usage.Status.ShouldBe(EmailAllowanceStatus.Failed);
        dto.Plan.Status.ShouldBe(EmailAllowanceStatus.Observed);
        dto.Plan.Matches.ShouldBe(true);
    }

    [Fact]
    public void Evaluate_ShouldReportBothReadsNotObserved_WhenNoProviderIsConfigured()
    {
        var dto = Evaluate(new EmailProviderUsageRead.NotConfigured(), plan: new EmailProviderPlanRead.NotConfigured());

        dto.Plan.Status.ShouldBe(EmailAllowanceStatus.NotObserved);
        dto.Plan.Reason.ShouldBe(EmailAllowanceReason.NotConfigured);
        dto.Plan.Active.ShouldBeNull();
        dto.Usage.Status.ShouldBe(EmailAllowanceStatus.NotObserved);
    }

    // ---- the month label ----

    [Fact]
    public void Evaluate_ShouldLabelTheMonth_WhenUtcAndStockholmAgree() =>
        Evaluate(Observed(1, at: MidMonth)).Usage.Month.ShouldBe("2026-10");

    [Fact]
    public void Evaluate_ShouldLeaveTheMonthUnlabelled_InTheHoursWhereStockholmIsAlreadyInTheNextMonth()
    {
        // 2026-10-31T23:30Z is 00:30 on 1 November in Stockholm (CET after the October change): UTC says October,
        // Stockholm says November, and the provider's own boundary is unmeasured.
        var straddle = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero);

        Evaluate(Observed(1, at: straddle)).Usage.Month.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldLeaveTheMonthUnlabelled_InTheSummerStraddle()
    {
        // 2026-06-30T22:30Z is 00:30 on 1 July in Stockholm (CEST, two hours ahead).
        var straddle = new DateTimeOffset(2026, 6, 30, 22, 30, 0, TimeSpan.Zero);

        Evaluate(Observed(1, at: straddle)).Usage.Month.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldLabelTheMonthAgain_OnceUtcHasCaughtUp()
    {
        var afterStraddle = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        Evaluate(Observed(1, at: afterStraddle)).Usage.Month.ShouldBe("2026-11");
    }

    [Fact]
    public void Evaluate_ShouldLabelTheMonthWithTheRightYear_AcrossNewYear()
    {
        var newYear = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Evaluate(Observed(1, at: newYear)).Usage.Month.ShouldBe("2027-01");
    }

    // ---- not observed and failed ----

    [Fact]
    public void Evaluate_ShouldReportNotObserved_WhenNoProviderIsConfigured_AndCarryNoFigures()
    {
        var usage = Evaluate(new EmailProviderUsageRead.NotConfigured()).Usage;

        usage.Status.ShouldBe(EmailAllowanceStatus.NotObserved);
        usage.Reason.ShouldBe(EmailAllowanceReason.NotConfigured);
        AssertNoFigures(usage);
    }

    [Theory]
    [MemberData(nameof(FailureReasons))]
    public void Evaluate_ShouldReportEachFailureWithItsOwnReason_AndCarryNoFigures(EmailAllowanceReason reason)
    {
        var usage = Evaluate(new EmailProviderUsageRead.Failed(reason)).Usage;

        usage.Status.ShouldBe(EmailAllowanceStatus.Failed);
        usage.Reason.ShouldBe(reason);
        AssertNoFigures(usage);
    }

    public static TheoryData<EmailAllowanceReason> FailureReasons()
    {
        var data = new TheoryData<EmailAllowanceReason>();
        foreach (var reason in Enum.GetValues<EmailAllowanceReason>().Where(r => r != EmailAllowanceReason.NotConfigured))
        {
            data.Add(reason);
        }

        return data;
    }

    private static void AssertNoFigures(EmailAllowanceUsageDto usage)
    {
        // An unknown count or cost is never a zero (ADR 0150 D2).
        usage.Sent.ShouldBeNull();
        usage.Billable.ShouldBeNull();
        usage.EstimatedExtraCostOre.ShouldBeNull();
        usage.AboveHalfOfSendLimit.ShouldBeNull();
        usage.WebhooksCount.ShouldBeNull();
        usage.ObservedAt.ShouldBeNull();
        usage.Month.ShouldBeNull();
    }
}
