using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class JuniorClassificationTests
{
    [Fact]
    public void Ctor_KeepsTheVerdictRuleAndVersion()
    {
        var classification = new JuniorClassification(JuniorVerdict.Unclear, "candidate-only", "1.0.0");

        classification.Verdict.ShouldBe(JuniorVerdict.Unclear);
        classification.RuleId.ShouldBe("candidate-only");
        classification.RuleSetVersion.ShouldBe("1.0.0");
    }

    [Fact]
    public void Ctor_Throws_OnAnUnknownVerdict() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new JuniorClassification((JuniorVerdict)0, "rule", "1"));

    [Theory]
    [InlineData("", "1")]
    [InlineData(" ", "1")]
    [InlineData("rule", "")]
    public void Ctor_Throws_OnABlankRuleOrVersion(string ruleId, string version) =>
        Should.Throw<ArgumentException>(() => new JuniorClassification(JuniorVerdict.Junior, ruleId, version));

    [Theory]
    [InlineData("Title-Junior", "1")]
    [InlineData("title junior", "1")]
    [InlineData("-title", "1")]
    [InlineData("title--junior", "1")]
    [InlineData("title-junior", "1.0 beta")]
    [InlineData("title-junior", "1.0.0+ABCD")]
    public void Ctor_Throws_WhenARuleIdOrVersionLeavesItsClosedShape(string ruleId, string version) =>
        Should.Throw<ArgumentException>(() => new JuniorClassification(JuniorVerdict.Junior, ruleId, version));

    [Fact]
    public void Ctor_Throws_OnARuleIdOrVersionLongerThanItsColumn()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new JuniorClassification(JuniorVerdict.Junior, new string('a', JuniorClassification.MaxRuleIdLength + 1), "1"));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new JuniorClassification(JuniorVerdict.Junior, "rule", new string('1', JuniorClassification.MaxRuleSetVersionLength + 1)));
    }
}
