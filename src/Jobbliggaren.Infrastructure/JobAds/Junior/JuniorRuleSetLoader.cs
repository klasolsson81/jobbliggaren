using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Infrastructure.JobAds.Junior;

/// <summary>
/// #2264 — loads one occupation field's junior rules (<c>junior-rules.*.json</c>) from the assembly. Every shape
/// check fails loud at host build, because a malformed rule set is a list that silently shows the wrong ads: a pattern
/// that does not compile under the linear-time engine, a rule id named twice, an unknown verdict or condition, a
/// pattern name nothing defines, or a reading id that is not a Platsbanken id or is named twice.
/// <para>
/// The version stored beside every verdict is the file's declared version plus a short hash of the file's bytes, so
/// any edit at all, a changed pattern or a flipped <c>auto</c> included, re-classifies every open ad in the field even
/// when nobody remembered to raise the declared version.
/// </para>
/// </summary>
internal static class JuniorRuleSetLoader
{
    internal const string DataItResourceName = "Jobbliggaren.Infrastructure.JobAds.Junior.junior-rules.data-it.v1.json";

    // Linear-time matching over third-party text (no backtracking), and a bound on any single match besides.
    internal const RegexOptions PatternOptions =
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex ExternalIdShape = new(@"^[0-9]{1,20}\z", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex RuleIdShape = new(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex DeclaredVersionShape = new(@"^[0-9]+(\.[0-9]+){0,2}\z", RegexOptions.CultureInvariant, MatchTimeout);

    // The hex digits of the content hash appended to the declared version.
    private const int ContentHashLength = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    internal static JuniorRuleSet LoadDataIt()
    {
        using var stream = typeof(JuniorRuleSetLoader).Assembly.GetManifestResourceStream(DataItResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded junior rules missing: {DataItResourceName}. Check <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");
        return LoadFrom(stream);
    }

    /// <summary>Test seam: drives a synthetic file through the real checks.</summary>
    internal static JuniorRuleSet LoadFrom(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var file = JsonSerializer.Deserialize<JuniorRuleSetFile>(bytes, JsonOptions)
            ?? throw Fail("deserialised to null");

        Require(!string.IsNullOrWhiteSpace(file.RuleSetVersion) && DeclaredVersionShape.IsMatch(file.RuleSetVersion),
            "has no ruleSetVersion of the form major[.minor[.patch]]");
        var version = $"{file.RuleSetVersion}+{Convert.ToHexStringLower(SHA256.HashData(bytes))[..ContentHashLength]}";
        Require(version.Length <= JuniorClassification.MaxRuleSetVersionLength, "has a ruleSetVersion that is too long");
        Require(!string.IsNullOrWhiteSpace(file.OccupationFieldConceptId), "has no occupationFieldConceptId");

        var patterns = CompilePatterns(file.Patterns);

        var candidates = file.CandidatePatterns.Select(name => Resolve(patterns, name)).ToList();
        Require(candidates.Count > 0 || file.CandidateWhenNoExperienceRequired, "has no candidate signal at all");

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        RequireRuleId(file.CandidateFallbackRuleId, ruleIds);
        RequireRuleId(file.NoSignalRuleId, ruleIds);
        RequireRuleId(file.TimeoutRuleId, ruleIds);
        RequireRuleId(file.Reading.RuleId, ruleIds);

        var rules = new List<JuniorRule>(file.Rules.Count);
        foreach (var rule in file.Rules)
        {
            RequireRuleId(rule.Id, ruleIds);
            Require(rule.All.Count > 0, $"rule {rule.Id} has no condition");
            var verdict = ParseVerdict(rule.Verdict, rule.Id);
            Require(verdict != JuniorVerdict.Unclear, $"rule {rule.Id} decides Unclear; a rule that only queues sets auto to false");
            rules.Add(new JuniorRule(rule.Id, verdict, rule.Auto, rule.All.Select(c => ToCondition(c, patterns, rule.Id)).ToList()));
        }

        return new JuniorRuleSet(
            version,
            file.OccupationFieldConceptId,
            file.Enabled,
            ToReading(file.Reading),
            candidates,
            file.CandidateWhenNoExperienceRequired,
            ToYearsReader(file.Years, patterns),
            rules,
            file.CandidateFallbackRuleId,
            file.NoSignalRuleId,
            file.TimeoutRuleId);
    }

    private static Dictionary<string, Regex> CompilePatterns(IReadOnlyDictionary<string, string> source)
    {
        var compiled = new Dictionary<string, Regex>(StringComparer.Ordinal);
        foreach (var (name, pattern) in source)
        {
            Require(!string.IsNullOrWhiteSpace(pattern), $"pattern {name} is empty");
            try
            {
                compiled[name] = new Regex(pattern, PatternOptions, MatchTimeout);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException)
            {
                throw Fail($"pattern {name} does not compile under the linear-time engine", e);
            }
        }

        return compiled;
    }

    private static JuniorCondition ToCondition(JuniorConditionFile c, Dictionary<string, Regex> patterns, string ruleId)
    {
        var target = c.On switch
        {
            "title" => JuniorConditionTarget.Title,
            "text" => JuniorConditionTarget.Text,
            "titleOrText" => JuniorConditionTarget.TitleOrText,
            "experienceRequired" => JuniorConditionTarget.ExperienceRequired,
            "yearsAtLeast" => JuniorConditionTarget.YearsAtLeast,
            "yearsStated" => JuniorConditionTarget.YearsStated,
            _ => throw Fail($"rule {ruleId} has an unknown condition target"),
        };

        return target switch
        {
            JuniorConditionTarget.Title or JuniorConditionTarget.Text or JuniorConditionTarget.TitleOrText =>
                new JuniorCondition(target, Resolve(patterns, c.Pattern), null, null, c.Not),
            JuniorConditionTarget.ExperienceRequired =>
                new JuniorCondition(target, null, c.Flag ?? throw Fail($"rule {ruleId} reads the experience mark without a flag"), null, c.Not),
            JuniorConditionTarget.YearsAtLeast =>
                new JuniorCondition(target, null, null, c.Years is > 0 ? c.Years : throw Fail($"rule {ruleId} reads years without a positive bound"), c.Not),
            _ => new JuniorCondition(target, null, null, null, c.Not),
        };
    }

    private static JuniorReading ToReading(JuniorReadingFile reading) =>
        new(reading.RuleId, ToIdSet(reading.Junior));

    private static HashSet<string> ToIdSet(IReadOnlyList<string> ids)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Require(id is not null && ExternalIdShape.IsMatch(id), "has a reading id that is not a Platsbanken id");
            Require(set.Add(id!), "has a reading that names one id twice");
        }

        return set;
    }

    private static JuniorYearsReader ToYearsReader(JuniorYearsFile years, Dictionary<string, Regex> patterns)
    {
        var readers = years.Patterns.Select(name => Resolve(patterns, name)).ToList();
        foreach (var reader in readers)
        {
            Require(
                reader.GetGroupNames().Contains(JuniorYearsReader.FromGroup),
                $"a years pattern has no '{JuniorYearsReader.FromGroup}' group");
        }

        return new JuniorYearsReader(readers, years.NumberWords, years.PlausibleMin, years.PlausibleMax);
    }

    private static JuniorVerdict ParseVerdict(string verdict, string ruleId) =>
        Enum.TryParse<JuniorVerdict>(verdict, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw Fail($"rule {ruleId} has an unknown verdict");

    private static Regex Resolve(Dictionary<string, Regex> patterns, string? name) =>
        name is not null && patterns.TryGetValue(name, out var regex)
            ? regex
            : throw Fail("refers to a pattern nothing defines");

    private static void RequireRuleId(string id, HashSet<string> seen)
    {
        Require(!string.IsNullOrWhiteSpace(id) && RuleIdShape.IsMatch(id), "has a rule id that is not lower-case kebab");
        Require(id.Length <= JuniorClassification.MaxRuleIdLength, "has a rule id that is too long");
        Require(seen.Add(id), "names one rule id twice");
    }

    private static void Require(bool condition, string what)
    {
        if (!condition)
            throw Fail(what);
    }

    private static InvalidOperationException Fail(string what, Exception? inner = null) =>
        new($"The junior rule set {what}.", inner);
}

/// <summary>Deserialisation form; the JSON member names stay in Infrastructure. The <c>"//"</c> key is maintainer
/// documentation and is deliberately not mapped.</summary>
internal sealed record JuniorRuleSetFile
{
    [JsonPropertyName("ruleSetVersion")]
    public string RuleSetVersion { get; init; } = "";

    [JsonPropertyName("occupationFieldConceptId")]
    public string OccupationFieldConceptId { get; init; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("reading")]
    public JuniorReadingFile Reading { get; init; } = new();

    [JsonPropertyName("patterns")]
    public IReadOnlyDictionary<string, string> Patterns { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("candidatePatterns")]
    public IReadOnlyList<string> CandidatePatterns { get; init; } = [];

    [JsonPropertyName("candidateWhenNoExperienceRequired")]
    public bool CandidateWhenNoExperienceRequired { get; init; }

    [JsonPropertyName("years")]
    public JuniorYearsFile Years { get; init; } = new();

    [JsonPropertyName("rules")]
    public IReadOnlyList<JuniorRuleFile> Rules { get; init; } = [];

    [JsonPropertyName("candidateFallbackRuleId")]
    public string CandidateFallbackRuleId { get; init; } = "";

    [JsonPropertyName("noSignalRuleId")]
    public string NoSignalRuleId { get; init; } = "";

    [JsonPropertyName("timeoutRuleId")]
    public string TimeoutRuleId { get; init; } = "";
}

internal sealed record JuniorReadingFile
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; init; } = "";

    [JsonPropertyName("junior")]
    public IReadOnlyList<string> Junior { get; init; } = [];
}

internal sealed record JuniorYearsFile
{
    [JsonPropertyName("patterns")]
    public IReadOnlyList<string> Patterns { get; init; } = [];

    [JsonPropertyName("numberWords")]
    public IReadOnlyDictionary<string, int> NumberWords { get; init; } = new Dictionary<string, int>();

    [JsonPropertyName("plausibleMin")]
    public int PlausibleMin { get; init; }

    [JsonPropertyName("plausibleMax")]
    public int PlausibleMax { get; init; }
}

internal sealed record JuniorRuleFile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("verdict")]
    public string Verdict { get; init; } = "";

    [JsonPropertyName("auto")]
    public bool Auto { get; init; }

    [JsonPropertyName("all")]
    public IReadOnlyList<JuniorConditionFile> All { get; init; } = [];
}

internal sealed record JuniorConditionFile
{
    [JsonPropertyName("on")]
    public string On { get; init; } = "";

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("flag")]
    public bool? Flag { get; init; }

    [JsonPropertyName("years")]
    public int? Years { get; init; }

    [JsonPropertyName("not")]
    public bool Not { get; init; }
}
