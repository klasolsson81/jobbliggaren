using Jobbliggaren.Application.Common.Notifications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Notifications;

/// <summary>
/// The run's own contract. The gapped rule here is the rule type's input domain; the only rule a sender
/// constructs today is <see cref="MailPacingRule.TopMatch"/>, pinned in <see cref="MailPacingRuleTests"/>.
/// </summary>
public class MailPacingRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_UntilTheRunCap_AdmitsAndCountsEachMail()
    {
        var run = new MailPacingRun(new MailPacingRule(TimeSpan.Zero, 3));

        run.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted);
        run.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted);
        run.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted);

        run.Admitted.ShouldBe(3);
    }

    [Fact]
    public void Decide_AfterTheRunCap_ReturnsRunFullAndCountsNothing()
    {
        var run = new MailPacingRun(new MailPacingRule(TimeSpan.Zero, 2));
        run.Decide(null, Now);
        run.Decide(null, Now);

        run.Decide(null, Now).ShouldBe(MailPacingDecision.RunFull);
        run.Decide(Now.AddDays(-1), Now).ShouldBe(MailPacingDecision.RunFull);

        run.Admitted.ShouldBe(2);
    }

    [Fact]
    public void Decide_InsideTheGap_ReturnsRecipientTooSoonAndCountsNothing()
    {
        var run = new MailPacingRun(new MailPacingRule(TimeSpan.FromHours(1), 5));

        run.Decide(Now.AddMinutes(-59), Now).ShouldBe(MailPacingDecision.RecipientTooSoon);

        run.Admitted.ShouldBe(0);
    }

    [Fact]
    public void Decide_ExactlyTheGapAfterTheLastMail_Admits()
    {
        var run = new MailPacingRun(new MailPacingRule(TimeSpan.FromHours(1), 5));

        run.Decide(Now.AddHours(-1), Now).ShouldBe(MailPacingDecision.Admitted);
    }

    [Fact]
    public void Decide_RecipientNeverMailed_Admits()
    {
        var run = new MailPacingRun(new MailPacingRule(TimeSpan.FromHours(1), 5));

        run.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted);
    }

    [Fact]
    public void Decide_WithTheTopMatchRule_AdmitsARecipientMailedAMomentAgo()
    {
        var run = new MailPacingRun(MailPacingRule.TopMatch);

        run.Decide(Now, Now).ShouldBe(MailPacingDecision.Admitted);
    }

    [Fact]
    public void Decide_TwoRunsOfOneRule_CountSeparately()
    {
        var rule = new MailPacingRule(TimeSpan.Zero, 1);
        var first = new MailPacingRun(rule);
        var second = new MailPacingRun(rule);

        first.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted);

        second.Decide(null, Now).ShouldBe(MailPacingDecision.Admitted,
            "the rule holds no state; each run counts its own mails");
        first.Decide(null, Now).ShouldBe(MailPacingDecision.RunFull);
    }
}
