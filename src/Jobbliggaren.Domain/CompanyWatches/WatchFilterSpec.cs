using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// Per-watch notification filter (bevaknings-reconcile RF-2, senior-cto-advisor 2026-07-12) —
/// the user's narrowing of ONE followed company's ad notifications: only ads in these
/// municipalities and/or only ads the user matches.
///
/// <para>
/// <b>House pattern-sibling of the upcoming <c>CompanyWatchCriteriaSpec</c></b> (criteria wave)
/// and a structural sibling of <see cref="SavedSearches.SearchCriteria"/>: normalized
/// (trim → drop blank → distinct ordinal → sort ordinal), per-element concept-id format
/// validation, explicit structural equality (a record with an <see cref="IReadOnlyList{T}"/>
/// member gets reference equality by default — jsonb value comparison in EF relies on
/// structural equality).
/// </para>
///
/// <para>
/// <b><see cref="Municipalities"/> and <see cref="Regions"/> are JobTech concept-ids in two
/// DISJOINT namespaces (RF-4=4A)</b> — the ones job ads carry (<c>municipality_concept_id</c>
/// and <c>region_concept_id</c> STORED columns) and the match-setup picker already emits.
/// Deliberately NOT the SCB 4-digit seat-kommun codes of the criteria rail ("annonsens ort"
/// vs "säteskommun" — two different concepts, kept apart in copy).
/// </para>
///
/// <para>
/// <b>The two geo axes are a UNION, not a hierarchy (F4a, CTO 2026-07-12 Q3=B).</b> A whole-län
/// selection is stored as the LÄN concept-id, never expanded into its municipalities — because
/// an ad may be tagged at län granularity with NO municipality at all. Materialising "Hela Skåne"
/// into 33 kommun-ids would silently drop every län-only Skåne ad from the user's notifications:
/// a silent miss in a never-miss product. <see cref="AdmitsLocation"/> therefore mirrors the
/// house-canonical predicate (<c>JobAdSearchComposition</c>): municipality-hit OR region-hit.
/// One geo semantics across /jobb, match-setup and the watch filter.
/// </para>
///
/// <para>
/// <b><see cref="OnlyMatched"/> carries no grade value (RF-5=5A)</b> — the floor is the FIXED
/// system-wide "matchande" definition (≥Good, one grade authority via the shared
/// GradeRankExpression SSOT, evaluated read-time). No named-or-numeric threshold is stored
/// (Goodhart; a configurable floor is a deferred, additive extension).
/// </para>
///
/// <para>
/// <b>An empty spec is invalid</b> — a present spec always narrows something. The NULL jsonb
/// column is the single canonical representation of "no filter" (architect design 2026-07-12).
/// Property names are the jsonb-key contract (PascalCase) — renaming breaks persisted data.
/// </para>
/// </summary>
public sealed record WatchFilterSpec
{
    // Identical to SearchCriteria.ConceptIdPattern (JobTech v2 concept-id format;
    // defense-in-depth default-deny). Private there, so mirrored here.
    private static readonly Regex ConceptIdPattern =
        new(@"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.Compiled);

    public IReadOnlyList<string> Municipalities { get; private init; } = [];
    public IReadOnlyList<string> Regions { get; private init; } = [];
    public bool OnlyMatched { get; private init; }

    // #551 PR-B D6 (ADR 0076 #551-amendment) — the remote/distans axis. A UNION disjunct of
    // the ort dimension (not "only": named Remote, not OnlyRemote), mirroring the house geo
    // predicate — an ad passes iff muni-hit OR region-hit OR (Remote && adRemote). A spec whose
    // ONLY narrowing is Remote=true IS valid (it narrows to remote ads). Appended last so the
    // jsonb-key write order stays a purely additive extension; the property name is the
    // jsonb-key contract. This is the follow-company watch filter's ort dimension — NOT the
    // SCB säteskommun of CompanyWatchCriteriaSpec (disjoint namespaces).
    public bool Remote { get; private init; }

    // EF + record copy-semantics
    private WatchFilterSpec() { }

    /// <summary>
    /// True when a selection carries no filter at all — i.e. what "the user cleared the filter" means.
    ///
    /// <para>
    /// <b>This is the SSOT for emptiness, and callers MUST use it rather than counting the raw lists.</b>
    /// Emptiness is decided on the NORMALIZED lists, because <c>[""]</c> and <c>["  "]</c> are empty
    /// selections that a raw <c>Count &gt; 0</c> would call non-empty. A caller that counted raw items
    /// would send such a payload to <see cref="Create"/>, get the empty-spec failure back, and report
    /// "at least one filter is required" to a user who was trying to REMOVE the filter — leaving the
    /// old filter active and the user with no way to clear it. One authority, so the transport boundary
    /// and the invariant can never disagree.
    /// </para>
    /// </summary>
    public static bool IsEmptySelection(
        IEnumerable<string>? municipalities,
        IEnumerable<string>? regions,
        bool onlyMatched,
        bool remote = false)
        => NormalizeList(municipalities).Length == 0
            && NormalizeList(regions).Length == 0
            && !onlyMatched
            // #551 PR-B D6 — remote=true alone narrows (to remote ads) → non-empty/valid.
            && !remote;

    public static Result<WatchFilterSpec> Create(
        IEnumerable<string>? municipalities,
        IEnumerable<string>? regions,
        bool onlyMatched,
        bool remote = false)
    {
        var normMunicipalities = NormalizeList(municipalities);
        var normRegions = NormalizeList(regions);

        if (normMunicipalities.Length == 0 && normRegions.Length == 0 && !onlyMatched && !remote)
        {
            return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                "WatchFilterSpec.Empty",
                "Minst ett filter (ort eller endast matchade annonser) krävs."));
        }

        // Cap reuses SearchCriteria's SSOT constant, applied PER AXIS (400 > ~290 kommuner
        // and > 21 län — the cap never bites a legitimate selection; "all municipalities"
        // = no ort filter).
        if (normMunicipalities.Length > SearchCriteria.MaxConceptIds)
        {
            return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                "WatchFilterSpec.TooManyMunicipalities",
                $"Max {SearchCriteria.MaxConceptIds} kommuner per bevakningsfilter."));
        }

        if (normRegions.Length > SearchCriteria.MaxConceptIds)
        {
            return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                "WatchFilterSpec.TooManyRegions",
                $"Max {SearchCriteria.MaxConceptIds} län per bevakningsfilter."));
        }

        foreach (var m in normMunicipalities)
        {
            if (!ConceptIdPattern.IsMatch(m))
            {
                return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                    "WatchFilterSpec.InvalidMunicipality",
                    "Ort måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."));
            }
        }

        foreach (var r in normRegions)
        {
            if (!ConceptIdPattern.IsMatch(r))
            {
                return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                    "WatchFilterSpec.InvalidRegion",
                    "Län måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."));
            }
        }

        return Result.Success(new WatchFilterSpec
        {
            Municipalities = normMunicipalities,
            Regions = normRegions,
            OnlyMatched = onlyMatched,
            Remote = remote,
        });
    }

    /// <summary>
    /// True when an ad at (<paramref name="municipalityConceptId"/>,
    /// <paramref name="regionConceptId"/>) passes this spec's ort dimension.
    ///
    /// <para>
    /// <b>UNION semantics, mirroring the house-canonical geo predicate</b>
    /// (<c>JobAdSearchComposition</c>: <c>municipalities.Contains(m) || regions.Contains(r)</c>):
    /// no geo axis set → everything passes; otherwise the ad passes iff its municipality is in
    /// the municipality list OR its region is in the region list. A whole-län selection therefore
    /// admits BOTH the län-only ads (municipality NULL) and every ad in that län's kommuner —
    /// which is exactly what the user picking "Hela Skåne" means, and what the same picker means
    /// everywhere else in the app.
    /// </para>
    ///
    /// <para>
    /// The 8A data-minimizing stance is unchanged in kind: an ad tagged with NEITHER axis never
    /// matches an active geo filter (the hit is never created).
    /// </para>
    /// </summary>
    public bool AdmitsLocation(string? municipalityConceptId, string? regionConceptId, bool adRemote)
    {
        // #551 PR-B D6 — NO geo axis set AT ALL (incl. the remote axis) → everything passes.
        // The early-return MUST account for Remote, else a remote-only spec would admit EVERY ad.
        if (Municipalities.Count == 0 && Regions.Count == 0 && !Remote)
            return true;

        if (municipalityConceptId is not null
            && Municipalities.Contains(municipalityConceptId, StringComparer.Ordinal))
        {
            return true;
        }

        if (regionConceptId is not null
            && Regions.Contains(regionConceptId, StringComparer.Ordinal))
        {
            return true;
        }

        // #551 PR-B D6 — remote is a UNION disjunct: a remote spec admits a remote ad. Same
        // union shape as ApplyFilter's Distans-only case (D5) — one geo semantics across
        // /jobb, match-setup and the watch filter.
        return Remote && adRemote;
    }

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

    // Structural VO equality (Evans 2003 ch. 5) — lists are already normalized
    // (sorted+distinct ordinal) in Create, so sequence comparison is deterministic.
    public bool Equals(WatchFilterSpec? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return OnlyMatched == other.OnlyMatched
            // #551 PR-B D6 — Remote is a member too (jsonb-equality footgun: a member omitted
            // here silently breaks EF change-tracking / jsonb value comparison).
            && Remote == other.Remote
            && Municipalities.SequenceEqual(other.Municipalities, StringComparer.Ordinal)
            && Regions.SequenceEqual(other.Regions, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OnlyMatched);
        hash.Add(Remote);
        foreach (var m in Municipalities)
            hash.Add(m, StringComparer.Ordinal);
        foreach (var r in Regions)
            hash.Add(r, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
