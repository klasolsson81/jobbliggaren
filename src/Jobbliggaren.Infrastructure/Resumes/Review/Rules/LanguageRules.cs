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

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var exclamations = prose.Count(c => c == '!');
        var shouting = ShoutingRegex().Match(prose);

        if (shouting.Success)
        {
            return CvCriterionVerdict.Assessed("C2", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, shouting.Value, "versalt 'skrik': håll en saklig, neutral ton")));
        }

        if (exclamations >= 2)
        {
            return CvCriterionVerdict.Assessed("C2", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"{exclamations} utropstecken: håll en saklig, neutral ton.")));
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

    // Swedish DEPONENS preterites: s-form but ACTIVE in meaning (#492). Their shape ends in
    // -ades/-des so the s-passive regex catches them, yet "lyckades öka försäljningen" / "trivdes i
    // en ledande roll" is exactly the achievement language A1 rewards — flagging it as passive makes
    // the engine contradict itself. A small CLOSED linguistic FUNCTION-WORD set (not knowledge-bank
    // data — parity the pronoun/acronym function-word patterns in this file; §5 holds). Only forms
    // that actually end in -ades/-des are listed (others never reach the filter).
    private static readonly HashSet<string> SwedishDeponentPreterites = new(StringComparer.Ordinal)
    {
        "lyckades", "trivdes", "hoppades", "andades", "låtsades", "vistades",
        "skämdes", "mindes", "nöjdes", "envisades", "samsades", "avundades",
    };

    // The C3 Fail ratio the rubric prose fixes (atsFailSignal ">30 % passiv form"). A code
    // operationalisation of versioned rubric PROSE (documented v1 posture), pinned by the C3 golden
    // drift-guard so code that drifts from the rubric fails CI (parity A7 #489).
    private const double FailRatio = 0.30;

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        // #489 ratio reconcile: score passives-per-sentence against the rubric's ">30 %", not an
        // absolute count (pre-fix "count >= 2" could never reach the rubric's ratio-based Fail, and
        // deponens/proper-noun false positives inflated that count). Swedish bli-passive detection
        // ("blev utsedd") is a deferred forward-note (needs a rubric-minor bump; out of scope here).
        var passives = RealPassives(context.Language, prose);
        var sentenceCount = Math.Max(1, ReviewText.Sentences(prose).Count);
        var ratio = (double)passives.Count / sentenceCount;

        if (ratio > FailRatio)
        {
            return CvCriterionVerdict.Assessed("C3", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(prose, passives[0], "hög andel passiv form, föredra aktivt språk")));
        }

        if (passives.Count > 0)
        {
            return CvCriterionVerdict.Assessed("C3", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, passives[0], "passiv konstruktion, föredra aktivt språk")));
        }

        return CvCriterionVerdict.Assessed("C3", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Övervägande aktivt språk (få eller inga passiveringar).")));
    }

    // The GENUINE passive constructions in the prose, in order. Swedish s-passives exclude the
    // deponens list (s-form but active — #492) AND capital-initial tokens (proper nouns like
    // "Mercedes"/"Archimedes", never a verb mid-sentence; a sentence-initial real passive is the
    // rare, documented false-negative of that exclusion). English be-passives need neither filter.
    private static List<string> RealPassives(TextLanguage language, string prose)
    {
        if (language == TextLanguage.English)
        {
            return EnglishPassiveRegex().Matches(prose).Select(m => m.Value).ToList();
        }

        return SwedishPassiveRegex().Matches(prose)
            .Select(m => m.Value)
            .Where(w => w.Length > 0 && !char.IsUpper(w[0]))
            .Where(w => !SwedishDeponentPreterites.Contains(w.ToLowerInvariant()))
            .ToList();
    }
}

/// <summary>C4 Konsekvent perspektiv (Medium) — no third-person narration.</summary>
internal sealed partial class C4PerspectiveRule : ICriterionRule
{
    public string CriterionId => "C4";

    // Third-person PERSONAL pronouns (sv + en) as standalone words. The Swedish demonstratives
    // "denna/denne" are deliberately EXCLUDED (#491): they are not third-person narration —
    // "i denna roll ansvarade jag …", "under denna period" is ordinary first-person CV prose, so
    // flagging them raised a false "tredje person" Warn on the common case. The real fail case
    // (name-as-subject, "Anna är en driven …") carries no pronoun and is out of scope for v1.
    [GeneratedRegex(@"\b(han|hon|hen|he|she)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ThirdPersonRegex();

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        var match = ThirdPersonRegex().Match(prose);
        if (match.Success)
        {
            return CvCriterionVerdict.Assessed("C4", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(prose, match.Value, "tredje person: använd konsekvent perspektiv (svensk standard: utan pronomen)")));
        }

        // Pass is scoped to what C4 actually checks — third-person PRONOUNS. Name-as-subject
        // narration ("Anna är en driven …") carries no pronoun and is out of scope for v1, so the
        // Pass claim is worded "inga tredje-persons-pronomen", never over-claiming that all
        // third-person narration was ruled out (§5 honesty, parity with the #488 C5 ruling).
        return CvCriterionVerdict.Assessed("C4", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Konsekvent perspektiv (inga tredje-persons-pronomen).")));
    }
}

// C5 Språkkonsistens (sv/en) has NO rule (#488): it is NotAssessedV1 in the rubric. The
// engine cannot honestly assess sentence-level sv/en mixing — the F4-8 detector only picks a
// DOMINANT document language, so a 50/50 CV still resolves to one language. The old rule
// returned an UNCONDITIONAL Pass with a fabricated citation, mis-reporting a property never
// checked (CLAUDE.md §5/§12 honesty contract). Removed; CvReviewEngine.Evaluate now reports
// NotAssessed with the asset-authored civic reason (parity A5/C1). Restoring a genuine
// assessment needs function-word sentence-level detection + a rubric minor bump (forward-note).

/// <summary>C6 Förkortningar förklarade (Low) — few unexplained acronyms.</summary>
internal sealed partial class C6AbbreviationsRule : ICriterionRule
{
    public string CriterionId => "C6";

    // 2–5 consecutive uppercase letters = a candidate acronym (AB, KTH, SaaS-ish).
    [GeneratedRegex(@"\b\p{Lu}{2,5}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymRegex();

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
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
                    $"{acronyms.Count} förkortningar utan förklaring: skriv ut första gången")));
        }

        return CvCriterionVerdict.Assessed("C6", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Inga ovanliga oförklarade förkortningar.")));
    }
}
