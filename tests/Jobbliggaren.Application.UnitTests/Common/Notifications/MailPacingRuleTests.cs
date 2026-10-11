using Jobbliggaren.Application.Common.Notifications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Notifications;

public class MailPacingRuleTests
{
    [Fact]
    public void TopMatch_HasNoGapPerRecipient()
    {
        MailPacingRule.TopMatch.MinGapPerRecipient.ShouldBe(TimeSpan.Zero,
            "A gap above zero holds a Toppmatch mail back. The signed consent text (ADR 0144 row 7) says "
            + "top matches are mailed directly, so raising the gap falsifies it: security-auditor V4 "
            + "(2026-10-11) sends row 7 back to her before the gap may change.");
    }

    [Fact]
    public void TopMatch_SendsAtMostTwentyMailsPerRun()
    {
        MailPacingRule.TopMatch.MaxMailsPerRun.ShouldBe(20);
    }

    [Fact]
    public void Constructor_NegativeGap_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MailPacingRule(TimeSpan.FromTicks(-1), 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_FewerThanOneMailPerRun_Throws(int maxMailsPerRun)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MailPacingRule(TimeSpan.Zero, maxMailsPerRun));
    }

    [Fact]
    public void Constructor_ZeroGapAndOneMailPerRun_IsTheSmallestRuleAccepted()
    {
        var rule = new MailPacingRule(TimeSpan.Zero, 1);

        rule.MinGapPerRecipient.ShouldBe(TimeSpan.Zero);
        rule.MaxMailsPerRun.ShouldBe(1);
    }
}
