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
/// <b><see cref="Municipalities"/> are JobTech municipality concept-ids (RF-4=4A)</b> — the
/// namespace job ads carry (<c>municipality_concept_id</c> STORED column) and the match-setup
/// picker already uses. Deliberately NOT the SCB 4-digit seat-kommun codes of the criteria
/// rail ("annonsens ort" vs "säteskommun" — two different concepts, kept apart in copy).
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
    public bool OnlyMatched { get; private init; }

    // EF + record copy-semantics
    private WatchFilterSpec() { }

    public static Result<WatchFilterSpec> Create(
        IEnumerable<string>? municipalities,
        bool onlyMatched)
    {
        var normMunicipalities = NormalizeList(municipalities);

        if (normMunicipalities.Length == 0 && !onlyMatched)
        {
            return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                "WatchFilterSpec.Empty",
                "Minst ett filter (ort eller endast matchade annonser) krävs."));
        }

        // Cap reuses SearchCriteria's SSOT constant (400 > ~290 kommuner — the cap
        // never bites a legitimate selection; "all municipalities" = no ort filter).
        if (normMunicipalities.Length > SearchCriteria.MaxConceptIds)
        {
            return Result.Failure<WatchFilterSpec>(DomainError.Validation(
                "WatchFilterSpec.TooManyMunicipalities",
                $"Max {SearchCriteria.MaxConceptIds} kommuner per bevakningsfilter."));
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

        return Result.Success(new WatchFilterSpec
        {
            Municipalities = normMunicipalities,
            OnlyMatched = onlyMatched,
        });
    }

    /// <summary>
    /// True when <paramref name="municipalityConceptId"/> passes this spec's ort dimension:
    /// no ort filter → everything passes; an active ort filter admits ONLY ads whose
    /// municipality is in the list — an ad WITHOUT a municipality (län-only) never matches
    /// an active ort filter (8A data-minimizing stance, architect design 2026-07-12).
    /// </summary>
    public bool AdmitsMunicipality(string? municipalityConceptId)
    {
        if (Municipalities.Count == 0)
            return true;

        return municipalityConceptId is not null
            && Municipalities.Contains(municipalityConceptId, StringComparer.Ordinal);
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
            && Municipalities.SequenceEqual(other.Municipalities, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OnlyMatched);
        foreach (var m in Municipalities)
            hash.Add(m, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
