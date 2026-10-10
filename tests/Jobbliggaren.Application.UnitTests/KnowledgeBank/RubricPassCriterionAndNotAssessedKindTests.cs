using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// #2083 PR 2 — the rubric's two user-facing review fields, pinned against the SHIPPED asset
/// through the real loader: <c>notAssessedKind</c> (the class of a <c>notAssessedReason</c>,
/// authored as one pair with it) and <c>passCriterion</c> (the "Godkänns när" clause). The
/// clauses are claims about what the engine checks, so their set, their numbers and their
/// civic form are guarded here; the loader's tolerance and fail-loud paths are driven through
/// <see cref="RubricLoader.LoadFrom"/> with the shipped JSON mutated in memory.
/// </summary>
public class RubricPassCriterionAndNotAssessedKindTests
{
    private static Rubric LoadRubric() => new RubricProvider().GetRubric();

    private static RubricCriterion Criterion(Rubric rubric, string id) =>
        rubric.Criteria.Single(c => c.Id == id);

    // ── notAssessedKind: one pair with its reason ──

    [Fact]
    public void Asset_ShouldAuthorAKindBesideEveryReason_AndNoKindWithoutOne()
    {
        var rubric = LoadRubric();

        foreach (var criterion in rubric.Criteria)
        {
            (criterion.NotAssessedKind is null).ShouldBe(criterion.NotAssessedReason is null,
                $"Kriterium {criterion.Id}: skäl och skälgrupp skrivs som ett par i assetet.");
        }
    }

    [Fact]
    public void Asset_ShouldClassifyEveryReasonExactly()
    {
        // The kind follows where the deciding property lives (CTO F4): a job ad, no measurement
        // yet, the text not settling it, or a property outside the text.
        var expected = new Dictionary<string, NotAssessedKind>(StringComparer.Ordinal)
        {
            ["A3"] = NotAssessedKind.RequiresJobAd,
            ["D8"] = NotAssessedKind.RequiresJobAd,
            ["A5"] = NotAssessedKind.NotInVersion,
            ["C1"] = NotAssessedKind.NotInVersion,
            ["C5"] = NotAssessedKind.NotInVersion,
            ["A8"] = NotAssessedKind.InsufficientData,
        };
        string[] textOnly =
            ["B2", "B5", "B8", "D2", "D3", "D4", "D5", "D7", "D9", "D10", "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8"];
        foreach (var id in textOnly)
        {
            expected[id] = NotAssessedKind.TextOnly;
        }

        var actual = LoadRubric().Criteria
            .Where(c => c.NotAssessedKind is not null)
            .ToDictionary(c => c.Id, c => c.NotAssessedKind!.Value, StringComparer.Ordinal);

        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Asset_ShouldClassifyEveryNotAssessedV1CriterionAsNotInVersion()
    {
        var pinned = LoadRubric().Criteria
            .Where(c => c.Assessability == CriterionAssessability.NotAssessedV1)
            .ToList();

        pinned.ShouldNotBeEmpty();
        pinned.ShouldAllBe(c => c.NotAssessedKind == NotAssessedKind.NotInVersion);
    }

    [Theory]
    [InlineData(NotAssessedKind.TextOnly)]
    [InlineData(NotAssessedKind.RequiresJobAd)]
    public void Asset_ShouldGiveEveryReasonOfATagOnlyKindOneSentence(NotAssessedKind kind)
    {
        // The review shows only the tag for these two kinds (CTO F5), which loses nothing only
        // while every reason of the kind is the one sentence the tag already says. A reason of
        // its own wording fails here first, so the row form is revisited before the UI could
        // silently hide information.
        var reasons = LoadRubric().Criteria
            .Where(c => c.NotAssessedKind == kind)
            .Select(c => c.NotAssessedReason)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        reasons.Count.ShouldBe(1, $"{kind}: varje skäl ska vara samma mening, annars döljer taggen något.");
    }

    // ── passCriterion: the "Godkänns när" clause ──

    // The two ruled criteria whose rule returns Warn or NotAssessed but never Pass: a pass
    // condition for them would promise an outcome the engine cannot produce.
    private static readonly string[] NeverPass = ["B5", "E2"];

    private static readonly string[] ExpectedPassCriterionIds =
    [
        "A1", "A2", "A4", "A6", "A7", "A8", "A9", "A10",
        "B1", "B2", "B3", "B4", "B6", "B7", "B8",
        "C2", "C3", "C4", "C6", "C7",
        "D1", "D3", "D6", "D9",
    ];

    [Fact]
    public void Asset_ShouldAuthorAPassCriterionOnExactlyThePassCapableCriteria()
    {
        var authored = LoadRubric().Criteria
            .Where(c => c.PassCriterion is not null)
            .Select(c => c.Id)
            .ToList();

        authored.ShouldBe(ExpectedPassCriterionIds, ignoreOrder: true);
    }

    [Fact]
    public void PassCapableSet_ShouldBeTheEnginesRuledCriteriaLessTheNeverPassOnes()
    {
        // Ties the pinned set to the engine itself: registering or removing a rule without
        // authoring (or retiring) its clause fails here.
        var ruled = CvReviewEngine.BuildRules().Select(r => r.CriterionId).ToList();

        ruled.Except(NeverPass, StringComparer.Ordinal)
            .ShouldBe(ExpectedPassCriterionIds, ignoreOrder: true);
    }

    [Fact]
    public void PassCriterion_ShouldBeOneCivicSentenceWithoutJargonOrEmDash()
    {
        // Acronym codenames are matched as whole upper-case words: a case-insensitive substring
        // would trip on ordinary Swedish ("E-post", "nämner").
        string[] acronyms = ["ADR", "POS", "NER", "OQ"];
        string[] fragments = ["F4-", " v1", "v1 ", "matchnings-motor", "parse", "—", "!"];

        foreach (var criterion in LoadRubric().Criteria.Where(c => c.PassCriterion is not null))
        {
            var text = criterion.PassCriterion!;

            text.ShouldEndWith(".", Case.Sensitive, $"{criterion.Id}: \"{text}\"");
            RubricCopyGuard.VersionToken.IsMatch(text).ShouldBeFalse($"{criterion.Id} nämner en motorversion: \"{text}\"");
            Regex.IsMatch(text, @"[.!?]\s").ShouldBeFalse($"{criterion.Id} är en mening: \"{text}\"");
            foreach (var token in acronyms)
            {
                Regex.IsMatch(text, $@"\b{token}\b").ShouldBeFalse($"{criterion.Id} läcker '{token}': \"{text}\"");
            }

            foreach (var token in fragments)
            {
                text.Contains(token, StringComparison.OrdinalIgnoreCase)
                    .ShouldBeFalse($"{criterion.Id} innehåller '{token}': \"{text}\"");
            }
        }
    }

    [Fact]
    public void PassCriterion_ShouldStateOnlyNumbersItsOwnThresholdsCarry()
    {
        // Golden drift guard (parity the *Signal guards): a number in a clause is the rule's
        // threshold, ratios as percentages and "below N" as N − 1. A clause with no entry here
        // states no number at all, so a threshold edit cannot leave a stale one behind.
        var expected = new Dictionary<string, Func<RubricCriterion, int[]>>(StringComparer.Ordinal)
        {
            ["A2"] = c => [Percent(c, RubricThresholdKeys.PassRatio)],
            ["A4"] = c => [Whole(c, RubricThresholdKeys.MaxGapMonths)],
            ["A6"] = c => [Percent(c, RubricThresholdKeys.PassRatio)],
            ["A7"] = c => [Whole(c, RubricThresholdKeys.PassBelowCount) - 1],
            ["A8"] = c => [Whole(c, RubricThresholdKeys.MaxWords)],
            ["B2"] = c => [Whole(c, RubricThresholdKeys.MaxPages)],
            ["C2"] = c => [Whole(c, RubricThresholdKeys.WarnFromExclamationCount) - 1],
            ["C6"] = c => [Whole(c, RubricThresholdKeys.MaxUnexplainedAcronyms)],
            ["C7"] = c => [Whole(c, RubricThresholdKeys.WarnFromMisspellingCount) - 1],
            ["D3"] = c => [Whole(c, RubricThresholdKeys.FontBodyPtWarnBelow)],
            // The row's evidence prints the size in MiB, so the clause states the threshold in MiB.
            ["D9"] = c => [(int)(c.RequiredThreshold(RubricThresholdKeys.FileSizeWarnBytes) / (1024 * 1024))],
        };

        foreach (var criterion in LoadRubric().Criteria.Where(c => c.PassCriterion is not null))
        {
            var stated = Regex.Matches(criterion.PassCriterion!, @"\d+")
                .Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            var want = expected.TryGetValue(criterion.Id, out var derive) ? derive(criterion) : [];

            string.Join(", ", stated).ShouldBe(
                string.Join(", ", want), $"{criterion.Id}: \"{criterion.PassCriterion}\"");
        }
    }

    [Fact]
    public void D9PassCriterion_ShouldStateAStrictBound()
    {
        Criterion(LoadRubric(), "D9").PassCriterion!.ShouldContain("mindre än");
    }

    private static int Whole(RubricCriterion criterion, string key) =>
        (int)criterion.RequiredThreshold(key);

    private static int Percent(RubricCriterion criterion, string key) =>
        (int)Math.Round(criterion.RequiredThreshold(key) * 100);

    // ── The loader: tolerance and fail-loud ──

    [Fact]
    public void Load_ShouldMapMissingKindAndPassCriterionToNull_OnAnOlderAsset()
    {
        // The v1.0.0 synthetic fixture predates both fields: they load as null, never a guess.
        var asm = typeof(RubricPassCriterionAndNotAssessedKindTests).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v1.0.0-no-reason-synthetic.json")!;

        var rubric = RubricLoader.LoadFrom(stream);

        rubric.Criteria.ShouldAllBe(c => c.NotAssessedKind == null && c.PassCriterion == null);
    }

    [Fact]
    public void Load_ShouldFailLoud_WhenAKindTokenIsUnknown()
    {
        var json = ShippedRubricJson().Replace(
            "\"requires_job_ad\"", "\"job_ad\"", StringComparison.Ordinal);

        var act = () => LoadFrom(json);

        act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("notAssessedKind");
    }

    [Theory]
    [InlineData("\"notAssessedKind\": \"text_only\",")]
    [InlineData("\"notAssessedKind\": \"text_only\", \"notAssessedReason\": \"  \",")]
    public void Load_ShouldFailLoud_WhenAKindHasNoReason(string fields)
    {
        // A1 carries no notAssessedReason; a kind on it, or on a blank reason, classifies nothing.
        var json = ShippedRubricJson().Replace(
            "\"id\": \"A1\",", $"\"id\": \"A1\", {fields}", StringComparison.Ordinal);

        var act = () => LoadFrom(json);

        act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("A1");
    }

    [Fact]
    public void Load_ShouldMapEveryKindToken_OnTheShippedAsset()
    {
        // Each of the four tokens occurs in the shipped asset, so a renamed or dropped table
        // entry fails the load of the real file, not only a synthetic one.
        var rubric = LoadRubric();

        Criterion(rubric, "B2").NotAssessedKind.ShouldBe(NotAssessedKind.TextOnly);
        Criterion(rubric, "A8").NotAssessedKind.ShouldBe(NotAssessedKind.InsufficientData);
        Criterion(rubric, "A5").NotAssessedKind.ShouldBe(NotAssessedKind.NotInVersion);
        Criterion(rubric, "D8").NotAssessedKind.ShouldBe(NotAssessedKind.RequiresJobAd);
    }

    private static Rubric LoadFrom(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return RubricLoader.LoadFrom(stream);
    }

    private static string ShippedRubricJson()
    {
        var assembly = typeof(RubricProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Jobbliggaren.Infrastructure.KnowledgeBank.rubric.v2.3.0.json")
            ?? throw new InvalidOperationException("Det inbäddade rubrik-assetet saknas.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
