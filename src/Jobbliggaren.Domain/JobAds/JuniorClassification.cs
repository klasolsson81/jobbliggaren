namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #2264 — one verdict of the junior rules, with the rule that decided it and the version of the rule set it was
/// read from, so a verdict can be explained and re-run. Both strings are a closed domain by construction: a rule id is
/// lower-case kebab (<c>title-junior</c>), a version is digits, dots, a plus and lower-case hex
/// (<c>1.0.0+3f9a1c2b</c>), so neither can carry a span of ad text.
/// </summary>
public sealed record JuniorClassification
{
    public const int MaxRuleIdLength = 64;
    public const int MaxRuleSetVersionLength = 32;

    public JuniorClassification(JuniorVerdict verdict, string ruleId, string ruleSetVersion)
    {
        if (!Enum.IsDefined(verdict))
            throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown junior verdict.");
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ruleId.Length, MaxRuleIdLength, nameof(ruleId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            ruleSetVersion.Length, MaxRuleSetVersionLength, nameof(ruleSetVersion));
        if (!IsKebab(ruleId))
            throw new ArgumentException("A junior rule id is lower-case kebab.", nameof(ruleId));
        if (!ruleSetVersion.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c) || c is '.' or '+'))
            throw new ArgumentException("A junior rule-set version is digits, dots, a plus and lower-case hex.", nameof(ruleSetVersion));

        Verdict = verdict;
        RuleId = ruleId;
        RuleSetVersion = ruleSetVersion;
    }

    public JuniorVerdict Verdict { get; }

    public string RuleId { get; }

    public string RuleSetVersion { get; }

    private static bool IsKebab(string value) =>
        value.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c) || c == '-')
        && value[0] != '-'
        && value[^1] != '-'
        && !value.Contains("--", StringComparison.Ordinal);
}
