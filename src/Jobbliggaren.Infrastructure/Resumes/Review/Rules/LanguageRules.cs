using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — Language-category (C) criterion rules. C1 (genuine
// spelling/grammar) is pinned NotAssessedV1 (ADR 0071 OQ3) — no rule here. The pronoun /
// passive / acronym signals are linguistic function-word patterns (not knowledge-bank
// data); the overselling-tone signal is purely structural (exclamation/shouting), so §5's
// "no hardcoded cliché/verb/rubric lists" holds.

/// <summary>C2 Ton (High) — neutral, non-overselling tone (no shouting/exclamation).</summary>
internal sealed partial class C2ToneRule : ICriterionRule
{
    public string CriterionId => "C2";

    // A "shouting" word: 5+ consecutive uppercase letters (åäö included) → not an acronym.
    [GeneratedRegex(@"\p{Lu}{5,}", RegexOptions.CultureInvariant)]
    private static partial Regex ShoutingRegex();

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var exclamations = prose.Count(c => c == '!');
        var shouting = ShoutingRegex().Match(prose);

        if (shouting.Success)
        {
            return CvCriterionVerdict.Assessed("C2", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, shouting.Value, "versalt 'skrik' — håll en saklig, neutral ton")));
        }

        if (exclamations >= 2)
        {
            return CvCriterionVerdict.Assessed("C2", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"{exclamations} utropstecken — håll en saklig, neutral ton.")));
        }

        return CvCriterionVerdict.Assessed("C2", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Saklig, neutral ton (inga 'skrik' eller överdrivna utropstecken).")));
    }
}

/// <summary>C3 Aktivt språk (High) — few passive constructions (language-aware).</summary>
internal sealed partial class C3ActiveVoiceRule : ICriterionRule
{
    public string CriterionId => "C3";

    // Swedish s-passive: a word ending in -ades / -des (e.g. "ansvarades", "utfördes").
    [GeneratedRegex(@"\b\w+(ades|des)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SwedishPassiveRegex();

    // English be-passive: was/were/been/being + past participle (-ed/-en).
    [GeneratedRegex(@"\b(was|were|been|being)\s+\w+(ed|en)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EnglishPassiveRegex();

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var regex = context.Language == TextLanguage.English ? EnglishPassiveRegex() : SwedishPassiveRegex();
        var matches = regex.Matches(prose);

        // A couple of incidental passives are fine; flag only a clear lean on passive voice.
        if (matches.Count >= 2)
        {
            return CvCriterionVerdict.Assessed("C3", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, matches[0].Value, "passiv konstruktion — föredra aktivt språk")));
        }

        return CvCriterionVerdict.Assessed("C3", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Övervägande aktivt språk (få eller inga passiveringar).")));
    }
}

/// <summary>C4 Konsekvent perspektiv (Medium) — no third-person narration.</summary>
internal sealed partial class C4PerspectiveRule : ICriterionRule
{
    public string CriterionId => "C4";

    // Third-person pronouns (sv + en) as standalone words.
    [GeneratedRegex(@"\b(han|hon|hen|denne|denna|he|she)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ThirdPersonRegex();

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var match = ThirdPersonRegex().Match(prose);
        if (match.Success)
        {
            return CvCriterionVerdict.Assessed("C4", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, match.Value, "tredje person — använd konsekvent perspektiv (svensk standard: utan pronomen)")));
        }

        return CvCriterionVerdict.Assessed("C4", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Konsekvent perspektiv (ingen tredje-persons-narration).")));
    }
}

/// <summary>C5 Språkkonsistens (High) — consistent with the detected document language.</summary>
internal sealed class C5LanguageConsistencyRule : ICriterionRule
{
    public string CriterionId => "C5";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;

        // v1: the F4-8 language detector classified the document as a single language; C5
        // affirms consistency at that document level. Deeper sentence-level sv/en mixing
        // detection (function-word data) is a measured future refinement — reported honestly,
        // never over-claimed.
        var language = context.Resume.DetectedLanguage.Name;
        return CvCriterionVerdict.Assessed("C5", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural(
                $"CV:t bedöms konsekvent på ett språk ({language}, F4-8-språkdetektering).")));
    }
}

/// <summary>C6 Förkortningar förklarade (Low) — few unexplained acronyms.</summary>
internal sealed partial class C6AbbreviationsRule : ICriterionRule
{
    public string CriterionId => "C6";

    // 2–5 consecutive uppercase letters = a candidate acronym (AB, KTH, SaaS-ish).
    [GeneratedRegex(@"\b\p{Lu}{2,5}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymRegex();

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var acronyms = AcronymRegex().Matches(prose)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var hasExpansion = prose.Contains('(');

        // A couple of acronyms are typically universal (AB, EU, IT); flag only a heavier,
        // unexplained load.
        if (acronyms.Count > 2 && !hasExpansion)
        {
            return CvCriterionVerdict.Assessed("C6", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, acronyms[0],
                    $"{acronyms.Count} förkortningar utan förklaring — skriv ut första gången")));
        }

        return CvCriterionVerdict.Assessed("C6", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Inga ovanliga oförklarade förkortningar.")));
    }
}
