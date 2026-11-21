using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Junior;

/// <summary>
/// #2278 — the shipped Data/IT rules, not the loader test's synthetic file: <c>text-seek-junior</c> shows an ad
/// automatically when its text seeks a junior, and never when the text says the post is not a junior one. The title
/// carries no junior, senior or student word, the ad has no reading id and no experience mark, so only the text
/// decides. Every text is synthetic.
/// </summary>
public class JuniorDataItRulesTests
{
    private static readonly JuniorRuleSet Rules = JuniorRuleSetLoader.LoadDataIt();

    private static JuniorClassification Classify(string text) =>
        JuniorClassifier.Classify(Rules, new JuniorClassificationInput("80000000", "IT-tekniker", text, ExperienceRequired: null));

    [Theory]
    [InlineData("Vi söker en junior utvecklare till vårt team.")]
    [InlineData("We are looking for a junior developer.")]
    [InlineData("Du som är junior får en bred introduktion.")]
    [InlineData("Vi söker en junior utvecklare. Detta är inte en seniortjänst.")]
    [InlineData("Du behöver inte ha erfarenhet av vår bransch, vi söker en junior.")]
    public void Classify_ATextThatSeeksAJunior_IsShownByTextSeekJunior(string text)
    {
        Classify(text).ShouldBe(new JuniorClassification(JuniorVerdict.Junior, "text-seek-junior", Rules.Version));
    }

    [Theory]
    [InlineData("Detta är inte en junior tjänst.")]
    [InlineData("Detta är ingen juniortjänst.")]
    [InlineData("Vi söker inte en junior.")]
    [InlineData("Rollen passar inte någon junior.")]
    [InlineData("We are not looking for a junior.")]
    [InlineData("We aren't looking for a junior.")]
    [InlineData("We aren’t hiring a junior this time.")]
    public void Classify_ATextThatSaysThePostIsNotJunior_IsQueuedAndNeverShownByTextSeekJunior(string text)
    {
        // The word "junior" still makes the ad a candidate, so it reaches the queue instead of the list.
        Classify(text).ShouldBe(new JuniorClassification(JuniorVerdict.Unclear, "candidate-only", Rules.Version));
    }
}
