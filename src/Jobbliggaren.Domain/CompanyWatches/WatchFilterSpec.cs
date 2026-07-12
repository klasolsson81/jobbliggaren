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

    // EF + record copy-semantics
    private WatchFilterSpec() { }

    public static Result<WatchFilterSpec> Create(
        IEnumerable<string>? municipalities,
        IEnumerable<string>? regions,
        bool onlyMatched)
    {
        var normMunicipalities = NormalizeList(municipalities);
        var normRegions = NormalizeList(regions);

        if (normMunicipalities.Length == 0 && normRegions.Length == 0 && !onlyMatched)
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
    public bool AdmitsLocation(string? municipalityConceptId, string? regionConceptId)
    {
        if (Municipalities.Count == 0 && Regions.Count == 0)
            return true;

        if (municipalityConceptId is not null
            && Municipalities.Contains(municipalityConceptId, StringComparer.Ordinal))
        {
            return true;
        }

        return regionConceptId is not null
            && Regions.Contains(regionConceptId, StringComparer.Ordinal);
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
            && Municipalities.SequenceEqual(other.Municipalities, StringComparer.Ordinal)
            && Regions.SequenceEqual(other.Regions, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OnlyMatched);
        foreach (var m in Municipalities)
            hash.Add(m, StringComparer.Ordinal);
        foreach (var r in Regions)
            hash.Add(r, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
