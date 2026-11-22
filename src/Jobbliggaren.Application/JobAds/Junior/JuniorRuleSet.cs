using System.Text.RegularExpressions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>
/// #2264 — the junior rules for one occupation field, as loaded from its versioned data file. Every word list,
/// pattern and threshold lives in the file (AGENTS.md §5); this type only carries them, compiled.
/// </summary>
/// <param name="Version">The rule-set version stored beside every verdict it produces.</param>
/// <param name="OccupationFieldConceptId">The JobTech occupation field the rules are written for.</param>
/// <param name="Enabled">Whether ads in the field are classified at all.</param>
/// <param name="Reading">A hand reading whose junior ids are shown ahead of every rule.</param>
/// <param name="CandidatePatterns">An ad is a junior candidate when one of these matches its title or text.</param>
/// <param name="CandidateWhenNoExperienceRequired">An ad the advertiser marks "no experience required" is a candidate too.</param>
/// <param name="Years">How a stated years-of-experience requirement is read.</param>
/// <param name="Rules">The ordered rules; the first whose conditions all hold decides.</param>
/// <param name="CandidateFallbackRuleId">The rule id an undecided candidate is recorded with (verdict Unclear).</param>
/// <param name="NoSignalRuleId">The rule id an ad with no junior signal is recorded with (verdict NotJunior).</param>
/// <param name="TimeoutRuleId">The rule id an ad is recorded with when a pattern runs out of match time (verdict Unclear).</param>
public sealed record JuniorRuleSet(
    string Version,
    string OccupationFieldConceptId,
    bool Enabled,
    JuniorReading Reading,
    IReadOnlyList<Regex> CandidatePatterns,
    bool CandidateWhenNoExperienceRequired,
    JuniorYearsReader Years,
    IReadOnlyList<JuniorRule> Rules,
    string CandidateFallbackRuleId,
    string NoSignalRuleId,
    string TimeoutRuleId);

/// <summary>
/// A hand reading: the Platsbanken ids Klas judged to be junior jobs. Junior ids only (Klas, 2026-11-20, "Bara
/// junior-id:na"): an ad he judged otherwise goes through the rules like any other.
/// </summary>
public sealed record JuniorReading(string RuleId, IReadOnlySet<string> JuniorExternalIds);

/// <summary>
/// One rule: when every condition holds, the ad gets <see cref="Verdict"/> if the rule is <see cref="Auto"/>, and
/// <see cref="JuniorVerdict.Unclear"/> otherwise. Either way the rule's id is recorded, so the queue says why.
/// </summary>
public sealed record JuniorRule(string Id, JuniorVerdict Verdict, bool Auto, IReadOnlyList<JuniorCondition> All);

/// <summary>What a condition reads.</summary>
public enum JuniorConditionTarget
{
    /// <summary>The (post-scrub) title matches <see cref="JuniorCondition.Pattern"/>.</summary>
    Title,

    /// <summary>The (post-scrub) description matches <see cref="JuniorCondition.Pattern"/>.</summary>
    Text,

    /// <summary>The title or the description matches <see cref="JuniorCondition.Pattern"/>.</summary>
    TitleOrText,

    /// <summary>The advertiser's experience mark equals <see cref="JuniorCondition.Flag"/>.</summary>
    ExperienceRequired,

    /// <summary>The stated years requirement is at least <see cref="JuniorCondition.Years"/>.</summary>
    YearsAtLeast,

    /// <summary>The text states any years-of-experience requirement.</summary>
    YearsStated,
}

/// <summary>One condition of a rule; <see cref="Negated"/> turns "holds" into "does not hold".</summary>
public sealed record JuniorCondition(
    JuniorConditionTarget Target,
    Regex? Pattern,
    bool? Flag,
    int? Years,
    bool Negated);
