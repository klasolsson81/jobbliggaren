using System.Text.RegularExpressions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>The inputs the junior rules read: the ad's post-scrub title and text, its experience mark and its id.</summary>
/// <param name="ExternalId">The Platsbanken id, read only by the hand reading. The sweep reads listed ads only, and
/// <c>JobAdPublication.IsListed</c> admits Platsbanken ads only, so the id is never another source's.</param>
public sealed record JuniorClassificationInput(
    string? ExternalId,
    string Title,
    string Description,
    bool? ExperienceRequired);

/// <summary>
/// The full evaluation behind a verdict: the verdict itself, every rule whose conditions hold (in file order, before
/// first-match; empty for an ad that is not a candidate), whether the ad is a candidate at all, and the stated years
/// requirement. The measurement reads all of it; production keeps <see cref="Classification"/>.
/// </summary>
public sealed record JuniorEvaluation(
    JuniorClassification Classification,
    IReadOnlyList<string> MatchingRuleIds,
    bool IsCandidate,
    int? YearsRequired);

/// <summary>
/// #2264 — the deterministic junior classification (no model, ADR 0071). Order of decision: the hand reading's junior
/// ids; then an
/// ad with no junior signal is NotJunior; then the rules in file order (the first whose conditions all hold decides; a
/// rule that is not automatic yields Unclear under its own id); then Unclear for a candidate no rule decides.
/// The classification is total: a pattern that runs out of its match time yields Unclear under the file's timeout rule
/// id, so the ad reaches the queue instead of being re-read on every run.
/// </summary>
public static class JuniorClassifier
{
    public static JuniorClassification Classify(JuniorRuleSet ruleSet, JuniorClassificationInput input) =>
        Evaluate(ruleSet, input, useReading: true).Classification;

    /// <param name="useReading">False measures the rules alone, as they would judge an ad nobody has read.</param>
    public static JuniorEvaluation Evaluate(JuniorRuleSet ruleSet, JuniorClassificationInput input, bool useReading)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(input);

        if (useReading && Read(ruleSet, input) is { } read)
            return new JuniorEvaluation(read, [], IsCandidate: true, YearsRequired: null);

        try
        {
            return EvaluateRules(ruleSet, input);
        }
        catch (RegexMatchTimeoutException)
        {
            return new JuniorEvaluation(
                new JuniorClassification(JuniorVerdict.Unclear, ruleSet.TimeoutRuleId, ruleSet.Version),
                [], IsCandidate: true, YearsRequired: null);
        }
    }

    private static JuniorClassification? Read(JuniorRuleSet ruleSet, JuniorClassificationInput input)
    {
        if (input.ExternalId is not { } externalId)
            return null;
        return ruleSet.Reading.JuniorExternalIds.Contains(externalId)
            ? new JuniorClassification(JuniorVerdict.Junior, ruleSet.Reading.RuleId, ruleSet.Version)
            : null;
    }

    private static JuniorEvaluation EvaluateRules(JuniorRuleSet ruleSet, JuniorClassificationInput input)
    {
        var years = ruleSet.Years.ReadRequirement(input.Description);
        if (!IsCandidate(ruleSet, input))
        {
            return new JuniorEvaluation(
                new JuniorClassification(JuniorVerdict.NotJunior, ruleSet.NoSignalRuleId, ruleSet.Version),
                [], IsCandidate: false, years);
        }

        var context = new Context(input, years);
        var matching = new List<string>();
        JuniorRule? first = null;
        foreach (var rule in ruleSet.Rules)
        {
            if (!rule.All.All(c => Holds(c, context)))
                continue;

            matching.Add(rule.Id);
            first ??= rule;
        }

        var classification = first is null
            ? new JuniorClassification(JuniorVerdict.Unclear, ruleSet.CandidateFallbackRuleId, ruleSet.Version)
            : new JuniorClassification(first.Auto ? first.Verdict : JuniorVerdict.Unclear, first.Id, ruleSet.Version);
        return new JuniorEvaluation(classification, matching, IsCandidate: true, years);
    }

    private static bool IsCandidate(JuniorRuleSet ruleSet, JuniorClassificationInput input)
    {
        if (ruleSet.CandidateWhenNoExperienceRequired && input.ExperienceRequired == false)
            return true;

        return ruleSet.CandidatePatterns.Any(p => p.IsMatch(input.Title) || p.IsMatch(input.Description));
    }

    private static bool Holds(JuniorCondition condition, Context context)
    {
        var holds = condition.Target switch
        {
            JuniorConditionTarget.Title => condition.Pattern!.IsMatch(context.Input.Title),
            JuniorConditionTarget.Text => condition.Pattern!.IsMatch(context.Input.Description),
            JuniorConditionTarget.TitleOrText =>
                condition.Pattern!.IsMatch(context.Input.Title) || condition.Pattern.IsMatch(context.Input.Description),
            JuniorConditionTarget.ExperienceRequired => context.Input.ExperienceRequired == condition.Flag,
            JuniorConditionTarget.YearsAtLeast => context.Years is { } y && y >= condition.Years,
            JuniorConditionTarget.YearsStated => context.Years is not null,
            _ => throw new InvalidOperationException($"Unknown junior condition target {condition.Target}."),
        };

        return condition.Negated ? !holds : holds;
    }

    private sealed record Context(JuniorClassificationInput Input, int? Years);
}
