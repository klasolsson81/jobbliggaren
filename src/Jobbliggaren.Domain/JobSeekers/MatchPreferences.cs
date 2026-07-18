using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// Value object — a job-seeker's STATED job-search preferences (Fas 4 STEG F4-12,
/// ADR 0076 + 2026-06-19 + 2026-06-21 amendments + ADR 0079 STEG 3). Holds the desired
/// occupation-groups (ssyk-level-4), regions, municipalities, employment-types, the
/// CONFIRMED skill set, and the stated years of experience the user is looking with.
/// The concept-id lists feed the deterministic match score (F4-13+): each maps straight
/// onto a <c>CandidateMatchProfile</c> dimension. <see cref="PreferredMunicipalities"/>
/// is the finer-grained location granularity that folds into the same location ("ort")
/// dimension as <see cref="PreferredRegions"/> (Spår 3, ADR 0076-amendment 2026-06-21).
/// <see cref="PreferredSkills"/> is the trusted capability source for the skill dimension
/// (ADR 0079 Beslut 1, Klas-override: CV-seeded but user-editable — the scorer re-sources
/// from here in STEG 3 PR-D). <see cref="ExperienceYears"/> is stored + surfaced only,
/// never scored in this STEG (ADR 0079 Beslut 1 / Beslut 7). NO AI/LLM (ADR 0071).
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

    // Mirrors the Resume Skill.YearsExperience domain bound (Resume.cs) — a stated
    // experience figure is believed within a sane human range, never negative. Public
    // so the command validator references the single authoritative bound (SPOT, like
    // SearchCriteria.MaxConceptIds) instead of restating the literal.
    public const int MaxExperienceYears = 70;

    public IReadOnlyList<string> PreferredOccupationGroups { get; private init; } = [];
    public IReadOnlyList<string> PreferredRegions { get; private init; } = [];
    public IReadOnlyList<string> PreferredEmploymentTypes { get; private init; } = [];

    // Finer-grained location granularity (kommun) that folds into the same "ort"
    // dimension as PreferredRegions (Spår 3, ADR 0076-amendment 2026-06-21). Appended
    // last so the jsonb-key contract stays a purely additive extension of the existing
    // write order. Empty = honest "no municipality stated" (the scorer reads it from PR-B).
    public IReadOnlyList<string> PreferredMunicipalities { get; private init; } = [];

    // STEG 3 (ADR 0079 Beslut 1, Klas-override) — the user's CONFIRMED skill set
    // (JobTech skill concept-ids, seeded from the CV via ISkillResolver and freely
    // edited: CV-proposals ∪ user-edits = the TRUSTED capability source for matching,
    // not the raw CV skill list). Appended last so the jsonb-key write order stays a
    // purely additive extension. PLAINTEXT concept-ids (non-PII taxonomy ids, symmetric
    // with the four dimensions above) → DEK-free, drives the fast sort==grade path
    // (ADR 0058/0059). Empty = honest "no skills confirmed" → the skill dimension
    // reports NotAssessed downstream (the scorer re-sources from here in STEG 3 PR-D).
    public IReadOnlyList<string> PreferredSkills { get; private init; } = [];

    // STEG 3 (ADR 0079 Beslut 1; Klas product decision 2026-06-22 = single profile-level
    // field) — the user's STATED total years of professional experience. Nullable:
    // null = "not stated" (honest); 0 = "stated as zero" (a new graduate) — distinct.
    // STORED + SURFACED only; it does NOT influence the match grade or sort in this STEG.
    // There is no ad-side seniority signal yet (ADR 0079 Beslut 7 / TD-B) and a hardcoded
    // "years ≥ required" threshold would break Goodhart / CLAUDE.md §5. Named
    // ExperienceYears (never *Score/*Value/*Rank) — a preference INPUT, never a
    // match-result magnitude (Goodhart guard).
    public int? ExperienceYears { get; private init; }

    // ADR 0079-amendment 2026-06-23 — per-occupation experience overlay (supersedes the
    // single profile-level ExperienceYears scalar above; the scalar is retained inert for
    // back-compat). A SPARSE overlay on PreferredOccupationGroups: an optional ~years
    // annotation per preferred occupation group, keyed by concept-id (subset invariant in
    // Create). Appended last so the jsonb-key write order stays a purely additive extension.
    // STORED + SURFACED only, never scored (Beslut 7 / TD-B). Empty = honest "no
    // per-occupation years stated". See OccupationExperience for the value-object contract.
    public IReadOnlyList<OccupationExperience> PreferredOccupationExperience { get; private init; } = [];

    // #551 PR-B F3 (ADR 0076 #551-amendment) — the user's remote/distans NOTIS-count
    // preference: when true, the notis/setup count treats distans as an accepted location
    // (JobAdFilterCriteria.Remote, unioned with the ort axes). A location-family bool that
    // folds into the same "ort" dimension as PreferredMunicipalities/PreferredRegions.
    // Appended last so the jsonb-key write order stays a purely additive extension.
    // STORED + read by the FACET-HARD notis count ONLY (GetMyMatchCount) — NEVER the scorer:
    // ADR 0079 never-grade-coupled, arch-pinned by MatchProfileRemoteIndependenceTests (F1),
    // so PreferredRemote deliberately does NOT reach CandidateMatchProfile. Default false =
    // honest "distans not requested".
    public bool PreferredRemote { get; private init; }

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
        IEnumerable<string>? preferredMunicipalities = null,
        IEnumerable<string>? preferredSkills = null,
        int? experienceYears = null,
        IEnumerable<OccupationExperience>? preferredOccupationExperience = null,
        bool preferredRemote = false)
    {
        var normOccupationGroups = NormalizeList(preferredOccupationGroups);
        var normRegions = NormalizeList(preferredRegions);
        var normEmploymentTypes = NormalizeList(preferredEmploymentTypes);
        var normMunicipalities = NormalizeList(preferredMunicipalities);
        var normSkills = NormalizeList(preferredSkills);
        var normOccupationExperience = NormalizeOccupationExperience(preferredOccupationExperience);

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
                "Kommun måste vara en giltig JobTech municipality-concept-id (1-32 tecken, alfanumeriskt + _-).")
            // STEG 3 (ADR 0079) — skills are a 5th concept-id list, same cap + regex
            // (the confirmed capability source). Appended last (jsonb-key additivity).
            ?? ValidateConceptList(
                normSkills,
                "MatchPreferences.TooManySkills",
                $"Max {SearchCriteria.MaxConceptIds} kompetenser.",
                "MatchPreferences.InvalidSkill",
                "Kompetens måste vara en giltig JobTech skill-concept-id (1-32 tecken, alfanumeriskt + _-).");

        if (error is not null)
            return Result.Failure<MatchPreferences>(error);

        // STEG 3 (ADR 0079) — experience is a single nullable scalar (not a list).
        // null = not stated; 0..MaxExperienceYears is the believed human range.
        if (experienceYears is { } years && (years < 0 || years > MaxExperienceYears))
            return Result.Failure<MatchPreferences>(DomainError.Validation(
                "MatchPreferences.ExperienceYearsOutOfRange",
                $"Antal års erfarenhet måste vara mellan 0 och {MaxExperienceYears}."));

        // ADR 0079-amendment — the per-occupation overlay protects its own coherence
        // (Evans 2003 kap. 5; DDD invariants live in the VO, not the handler): cap, valid
        // concept-id format, at-most-one-per-group, years in range, and the subset rule
        // (an entry only for an actually-preferred group). Runs after the group list is
        // normalized so the subset check reads the canonical set.
        var occupationExperienceError =
            ValidateOccupationExperience(normOccupationExperience, normOccupationGroups);
        if (occupationExperienceError is not null)
            return Result.Failure<MatchPreferences>(occupationExperienceError);

        return Result.Success(new MatchPreferences
        {
            PreferredOccupationGroups = normOccupationGroups,
            PreferredRegions = normRegions,
            PreferredEmploymentTypes = normEmploymentTypes,
            PreferredMunicipalities = normMunicipalities,
            PreferredSkills = normSkills,
            ExperienceYears = experienceYears,
            PreferredOccupationExperience = normOccupationExperience,
            PreferredRemote = preferredRemote,
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

    // Trim each entry's concept-id, drop blanks, sort by concept-id ordinal (deterministic
    // structural equality, parity with NormalizeList). Duplicates are NOT silently deduped
    // here — a duplicate concept-id is rejected as an invariant failure in
    // ValidateOccupationExperience (two entries for one group could carry conflicting years;
    // default-deny over silent data loss).
    private static OccupationExperience[] NormalizeOccupationExperience(
        IEnumerable<OccupationExperience>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(static e => e is not null && !string.IsNullOrWhiteSpace(e.ConceptId))
            .Select(static e => e with { ConceptId = e.ConceptId.Trim() })
            .OrderBy(static e => e.ConceptId, StringComparer.Ordinal)
            .ToArray();
    }

    // The per-occupation overlay invariants (ADR 0079-amendment): cap (reusing the same
    // IN(...)-DoS floor as the lists), concept-id format (default-deny), at-most-one entry
    // per group, years in the human range, and the SUBSET rule — an overlay entry may only
    // annotate a concept-id that is actually in PreferredOccupationGroups (the VO refuses an
    // orphan annotation). Empty is valid.
    private static DomainError? ValidateOccupationExperience(
        OccupationExperience[] entries, string[] preferredGroups)
    {
        if (entries.Length > SearchCriteria.MaxConceptIds)
            return DomainError.Validation(
                "MatchPreferences.TooManyOccupationExperience",
                $"Max {SearchCriteria.MaxConceptIds} erfarenhets-poster per yrke.");

        var groups = new HashSet<string>(preferredGroups, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!ConceptIdPattern.IsMatch(entry.ConceptId))
                return DomainError.Validation(
                    "MatchPreferences.InvalidOccupationExperience",
                    "Yrkes-erfarenhet måste referera en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

            if (!seen.Add(entry.ConceptId))
                return DomainError.Validation(
                    "MatchPreferences.DuplicateOccupationExperience",
                    "Endast en erfarenhets-post per yrkesgrupp.");

            if (entry.Years is { } y && (y < 0 || y > MaxExperienceYears))
                return DomainError.Validation(
                    "MatchPreferences.OccupationExperienceYearsOutOfRange",
                    $"Antal års erfarenhet måste vara mellan 0 och {MaxExperienceYears}.");

            if (!groups.Contains(entry.ConceptId))
                return DomainError.Validation(
                    "MatchPreferences.OrphanOccupationExperience",
                    "Erfarenhet kan bara anges för en vald yrkesgrupp.");
        }

        return null;
    }

    // Structural VO equality (Evans 2003 kap. 5). A record with IReadOnlyList gets
    // default REFERENCE equality → jsonb-dedupe/value-comparison would never match.
    // Lists are already normalized (sorted+distinct ordinal) in Create → sequence
    // comparison is deterministic. Canonical dimension order: OccupationGroups,
    // Regions, EmploymentTypes, Municipalities, Skills (each appended last), then the
    // ExperienceYears scalar. EVERY member MUST appear in both Equals and GetHashCode
    // or EF change-tracking / jsonb-equality silently breaks (the documented footgun).
    public bool Equals(MatchPreferences? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return PreferredOccupationGroups.SequenceEqual(other.PreferredOccupationGroups, StringComparer.Ordinal)
            && PreferredRegions.SequenceEqual(other.PreferredRegions, StringComparer.Ordinal)
            && PreferredEmploymentTypes.SequenceEqual(other.PreferredEmploymentTypes, StringComparer.Ordinal)
            && PreferredMunicipalities.SequenceEqual(other.PreferredMunicipalities, StringComparer.Ordinal)
            && PreferredSkills.SequenceEqual(other.PreferredSkills, StringComparer.Ordinal)
            && ExperienceYears == other.ExperienceYears
            // OccupationExperience is a record → SequenceEqual uses its structural equality
            // (both lists are normalized sorted-by-ConceptId in Create → deterministic).
            && PreferredOccupationExperience.SequenceEqual(other.PreferredOccupationExperience)
            // #551 PR-B F3 — the remote bool is a member too (jsonb-equality footgun: a member
            // omitted here silently breaks EF change-tracking / jsonb value comparison).
            && PreferredRemote == other.PreferredRemote;
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
        foreach (var s in PreferredSkills)
            hash.Add(s, StringComparer.Ordinal);
        hash.Add(ExperienceYears);
        foreach (var oe in PreferredOccupationExperience)
            hash.Add(oe);
        hash.Add(PreferredRemote);
        return hash.ToHashCode();
    }
}
