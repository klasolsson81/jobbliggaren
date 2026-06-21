using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// Value object — a job-seeker's STATED job-search preferences (Fas 4 STEG F4-12,
/// ADR 0076 + 2026-06-19 + 2026-06-21 amendments). Holds the desired occupation-groups
/// (ssyk-level-4), regions, municipalities, and employment-types the user is looking for.
/// These feed the deterministic match score (F4-13+): each list maps straight onto a
/// <c>CandidateMatchProfile</c> dimension. <see cref="PreferredMunicipalities"/> is the
/// finer-grained location granularity that folds into the same location ("ort") dimension
/// as <see cref="PreferredRegions"/> (Spår 3, ADR 0076-amendment 2026-06-21) — added here
/// additively; the scorer consumes it from PR-B onward. NO AI/LLM (ADR 0071).
///
/// <para>
/// <b>Mirrors <see cref="SearchCriteria"/> (ADR 0042 Beslut B) for the multi-value
/// invariants:</b> (1) normalization sorted+distinct ordinal (otherwise jsonb-equality
/// is reference-based — a record with <c>IReadOnlyList</c> gets default reference equality);
/// (2) per-list cap <see cref="SearchCriteria.MaxConceptIds"/> (IN(...)-DoS floor, reuses
/// the one authoritative constant — DRY/SPOT); (3) per-element concept-id regex
/// (default-deny, Saltzer/Schroeder 1975). <c>Equals</c>/<c>GetHashCode</c> are overridden
/// with ordinal sequence comparison so two logically-equal preference sets compare equal
/// (Evans 2003 kap. 5).
/// </para>
///
/// <para>
/// <b>Deliberate divergence from <see cref="SearchCriteria"/>:</b> there is NO "at least
/// one criterion" invariant. All four lists empty is a VALID <see cref="MatchPreferences"/>
/// — a user who has not yet stated any preference (skipped onboarding). Empty → the
/// corresponding match dimension reports <c>NotAssessed</c> downstream, never faked
/// (ADR 0076 Decision 7). <see cref="Empty"/> is the honest "no preferences stated" default.
/// </para>
///
/// <para>
/// <b>Property names are the jsonb-key contract (PascalCase)</b> — a rename without a
/// converter+migration breaks persisted data (see <c>MatchPreferencesConverters</c>).
/// </para>
/// </summary>
public sealed record MatchPreferences
{
    // JobTech concept-id format — identical to SearchCriteria.ConceptIdPattern
    // (the desired ssyk-4 group / region / employment-type ids). Default-deny.
    private static readonly Regex ConceptIdPattern =
        new(@"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.Compiled);

    public IReadOnlyList<string> PreferredOccupationGroups { get; private init; } = [];
    public IReadOnlyList<string> PreferredRegions { get; private init; } = [];
    public IReadOnlyList<string> PreferredEmploymentTypes { get; private init; } = [];

    // Finer-grained location granularity (kommun) that folds into the same "ort"
    // dimension as PreferredRegions (Spår 3, ADR 0076-amendment 2026-06-21). Appended
    // last so the jsonb-key contract stays a purely additive extension of the existing
    // write order. Empty = honest "no municipality stated" (the scorer reads it from PR-B).
    public IReadOnlyList<string> PreferredMunicipalities { get; private init; } = [];

    // EF + record copy-semantik. Property-initializers (= []) ensure non-null even
    // before EF materializes via the value converter.
    private MatchPreferences() { }

    /// <summary>The honest "no preferences stated" value — all four lists empty. Valid.</summary>
    public static readonly MatchPreferences Empty = new();

    // preferredMunicipalities is the additive 4th dimension (Spår 3, ADR 0076-amendment
    // 2026-06-21). Optional so every existing caller keeps compiling — absence is the
    // honest empty/"not stated" default, identical to passing an empty list (mirrors the
    // VO's all-empty-is-valid philosophy; the other three are independently
    // optional-by-emptiness too).
    public static Result<MatchPreferences> Create(
        IEnumerable<string>? preferredOccupationGroups,
        IEnumerable<string>? preferredRegions,
        IEnumerable<string>? preferredEmploymentTypes,
        IEnumerable<string>? preferredMunicipalities = null)
    {
        var normOccupationGroups = NormalizeList(preferredOccupationGroups);
        var normRegions = NormalizeList(preferredRegions);
        var normEmploymentTypes = NormalizeList(preferredEmploymentTypes);
        var normMunicipalities = NormalizeList(preferredMunicipalities);

        // Cap + per-element regex per dimension (default-deny). Empty is allowed —
        // no "at least one" invariant (deliberate divergence from SearchCriteria).
        var error =
            ValidateConceptList(
                normOccupationGroups,
                "MatchPreferences.TooManyOccupationGroups",
                $"Max {SearchCriteria.MaxConceptIds} yrkesgrupper.",
                "MatchPreferences.InvalidOccupationGroup",
                "Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normRegions,
                "MatchPreferences.TooManyRegions",
                $"Max {SearchCriteria.MaxConceptIds} regioner.",
                "MatchPreferences.InvalidRegion",
                "Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normEmploymentTypes,
                "MatchPreferences.TooManyEmploymentTypes",
                $"Max {SearchCriteria.MaxConceptIds} anställningsformer.",
                "MatchPreferences.InvalidEmploymentType",
                "Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normMunicipalities,
                "MatchPreferences.TooManyMunicipalities",
                $"Max {SearchCriteria.MaxConceptIds} kommuner.",
                "MatchPreferences.InvalidMunicipality",
                "Kommun måste vara en giltig JobTech municipality-concept-id (1-32 tecken, alfanumeriskt + _-).");

        if (error is not null)
            return Result.Failure<MatchPreferences>(error);

        return Result.Success(new MatchPreferences
        {
            PreferredOccupationGroups = normOccupationGroups,
            PreferredRegions = normRegions,
            PreferredEmploymentTypes = normEmploymentTypes,
            PreferredMunicipalities = normMunicipalities,
        });
    }

    private static DomainError? ValidateConceptList(
        string[] values,
        string tooManyCode, string tooManyMessage,
        string invalidCode, string invalidMessage)
    {
        if (values.Length > SearchCriteria.MaxConceptIds)
            return DomainError.Validation(tooManyCode, tooManyMessage);

        foreach (var v in values)
        {
            if (!ConceptIdPattern.IsMatch(v))
                return DomainError.Validation(invalidCode, invalidMessage);
        }

        return null;
    }

    // Trim per element, drop empty/whitespace, distinct ordinal, sort ordinal —
    // identical to SearchCriteria.NormalizeList (deterministic structural equality).
    private static string[] NormalizeList(IEnumerable<string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .Select(static v => v.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal)
            .ToArray();
    }

    // Structural VO equality (Evans 2003 kap. 5). A record with IReadOnlyList gets
    // default REFERENCE equality → jsonb-dedupe/value-comparison would never match.
    // Lists are already normalized (sorted+distinct ordinal) in Create → sequence
    // comparison is deterministic. Canonical dimension order: OccupationGroups,
    // Regions, EmploymentTypes, Municipalities (municipalities appended last).
    public bool Equals(MatchPreferences? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return PreferredOccupationGroups.SequenceEqual(other.PreferredOccupationGroups, StringComparer.Ordinal)
            && PreferredRegions.SequenceEqual(other.PreferredRegions, StringComparer.Ordinal)
            && PreferredEmploymentTypes.SequenceEqual(other.PreferredEmploymentTypes, StringComparer.Ordinal)
            && PreferredMunicipalities.SequenceEqual(other.PreferredMunicipalities, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var g in PreferredOccupationGroups)
            hash.Add(g, StringComparer.Ordinal);
        foreach (var r in PreferredRegions)
            hash.Add(r, StringComparer.Ordinal);
        foreach (var e in PreferredEmploymentTypes)
            hash.Add(e, StringComparer.Ordinal);
        foreach (var m in PreferredMunicipalities)
            hash.Add(m, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
