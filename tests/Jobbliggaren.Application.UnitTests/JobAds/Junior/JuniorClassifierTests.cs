using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Junior;

/// <summary>
/// #2264 — the order of decision: the reading, then the rules in file order (first match decides; a rule that is not
/// automatic queues under its own id), then Unclear for an undecided candidate, then NotJunior for no signal. The rule
/// set is the loader test's synthetic file, read through the real loader; every text is synthetic.
/// </summary>
public class JuniorClassifierTests
{
    private static readonly JuniorRuleSet Rules = JuniorRuleSetLoader.LoadFrom(JuniorRuleSetLoaderTests.Json(JuniorRuleSetLoaderTests.MinimalValid));

    private static JuniorClassificationInput Ad(string title, string text = "", bool? experienceRequired = null, string? id = "80000000") =>
        new(id, title, text, experienceRequired);

    [Fact]
    public void Classify_TheReadingDecidesBothWays_AheadOfEveryRule()
    {
        // The rules alone would say not junior (three years) and junior (a junior title) respectively.
        JuniorClassifier.Classify(Rules, Ad("Utvecklare", "Minst tre års erfarenhet.", id: "90000001"))
            .ShouldBe(new JuniorClassification(JuniorVerdict.Junior, "reading-test", Rules.Version));
        JuniorClassifier.Classify(Rules, Ad("Junior utvecklare", id: "90000002"))
            .ShouldBe(new JuniorClassification(JuniorVerdict.NotJunior, "reading-test", Rules.Version));
    }

    [Fact]
    public void Evaluate_WithoutTheReading_JudgesARereadAdByTheRulesAlone()
    {
        var evaluation = JuniorClassifier.Evaluate(Rules, Ad("Junior utvecklare", id: "90000002"), useReading: false);

        evaluation.Classification.ShouldBe(new JuniorClassification(JuniorVerdict.Junior, "title-junior", Rules.Version));
    }

    [Fact]
    public void Classify_TheFirstMatchingRuleDecides_InFileOrder()
    {
        // Both years-3-plus and title-junior hold; years-3-plus comes first.
        JuniorClassifier.Classify(Rules, Ad("Junior utvecklare", "Du har 3 års erfarenhet."))
            .ShouldBe(new JuniorClassification(JuniorVerdict.NotJunior, "years-3-plus", Rules.Version));
    }

    [Fact]
    public void Classify_ARuleThatIsNotAutomatic_QueuesUnderItsOwnId()
    {
        JuniorClassifier.Classify(Rules, Ad("Junior utvecklare", "Du ska handleda junior kollegor."))
            .ShouldBe(new JuniorClassification(JuniorVerdict.Unclear, "text-mentor", Rules.Version));
    }

    [Fact]
    public void Classify_TheRulesReadCandidatesOnly_SoAnAdWithNoJuniorSignalIsNeverQueued()
    {
        // The text meets years-3-plus and text-mentor's patterns, but carries no candidate term and no experience mark.
        var evaluation = JuniorClassifier.Evaluate(
            Rules, Ad("Utvecklare", "Tre års erfarenhet. Du ska handleda junior kollegor.".Replace("junior", "nya", StringComparison.Ordinal)),
            useReading: false);

        evaluation.Classification.ShouldBe(new JuniorClassification(JuniorVerdict.NotJunior, "no-signal", Rules.Version));
        evaluation.MatchingRuleIds.ShouldBeEmpty();
        evaluation.YearsRequired.ShouldBe(3);
    }

    [Fact]
    public void Classify_ACandidateNoRuleDecides_IsUnclear()
    {
        JuniorClassifier.Classify(Rules, Ad("Utvecklare", "Vi har ett traineeprogram."))
            .ShouldBe(new JuniorClassification(JuniorVerdict.Unclear, "candidate-only", Rules.Version));
    }

    [Fact]
    public void Classify_AnAdWithNoJuniorSignal_IsNotJunior()
    {
        JuniorClassifier.Classify(Rules, Ad("Utvecklare", "Vi bygger tjänster."))
            .ShouldBe(new JuniorClassification(JuniorVerdict.NotJunior, "no-signal", Rules.Version));
    }

    [Fact]
    public void Classify_TheNoExperienceMark_MakesACandidate_AndItsRuleQueuesItWhenNoYearsAreStated()
    {
        JuniorClassifier.Classify(Rules, Ad("Utvecklare", "Vi bygger tjänster.", experienceRequired: false))
            .ShouldBe(new JuniorClassification(JuniorVerdict.Unclear, "flag-no-experience", Rules.Version));

        // Years stated: the negated condition fails, so the mark alone is only a candidate signal.
        JuniorClassifier.Classify(Rules, Ad("Utvecklare", "Två års erfarenhet.", experienceRequired: false))
            .ShouldBe(new JuniorClassification(JuniorVerdict.Unclear, "candidate-only", Rules.Version));
    }

    [Fact]
    public void Evaluate_ListsEveryRuleThatHolds_InFileOrder()
    {
        var evaluation = JuniorClassifier.Evaluate(
            Rules, Ad("Junior utvecklare", "Tre års erfarenhet. Du ska handleda junior kollegor."), useReading: false);

        evaluation.MatchingRuleIds.ShouldBe(["years-3-plus", "text-mentor"]);
        evaluation.IsCandidate.ShouldBeTrue();
        evaluation.YearsRequired.ShouldBe(3);
    }

    [Theory]
    [InlineData("Tre års erfarenhet.", 3)]
    [InlineData("Du har 2 års erfarenhet.", 2)]
    [InlineData("2-4 års erfarenhet.", 2)]
    [InlineData("Två års erfarenhet och 5 års erfarenhet av ledning.", 5)]
    [InlineData("40 års erfarenhet i branschen.", null)]
    [InlineData("4-2 års erfarenhet.", null)]
    [InlineData("Ingen erfarenhet krävs.", null)]
    public void Evaluate_ReadsTheHighestStatedLowerBound(string text, int? expected)
    {
        JuniorClassifier.Evaluate(Rules, Ad("Utvecklare", text), useReading: false).YearsRequired.ShouldBe(expected);
    }
}
