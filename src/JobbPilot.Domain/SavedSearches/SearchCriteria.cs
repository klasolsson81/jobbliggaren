using System.Text.RegularExpressions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;

namespace JobbPilot.Domain.SavedSearches;

/// <summary>
/// Value object — en reproducerbar jobbsökning. Speglar ListJobAds-sökytan
/// (ADR 0039 Beslut 3). Page/PageSize exkluderas medvetet: de är
/// runtime-pagination, inte del av sökningens identitet. SortBy ingår
/// (determinerar paginerat resultat → del av användarens avsikt).
///
/// <para>
/// <b>ADR 0042 Beslut B (Accepted 2026-05-16) — Ssyk/Region single→multi:</b>
/// <see cref="Ssyk"/>/<see cref="Region"/> är <c>IReadOnlyList&lt;string&gt;</c>
/// (aldrig null; tom lista = inget filter). Fyra invarianter upprätthålls i
/// <see cref="Create"/>: (1) normalisering sorterad+distinct ordinal (annars
/// bryts SavedSearch jsonb-dedupe — record-collection-equality är referens-
/// baserad), (2) maxantal-cap <see cref="MaxConceptIds"/> (query-blowup/
/// IN(...)-DoS-skydd, speglas i ListJobAdsQueryValidator), (3) generaliserad
/// tom-invariant (minst en icke-tom lista ELLER Q), (4) jsonb-bakåtkompat
/// hanteras i Infrastructure-converter (CTO Yta A3). <c>Equals</c>/
/// <c>GetHashCode</c> överrids explicit med ordinal sekvensjämförelse —
/// SavedSearch jsonb-dedupe vilar på strukturell VO-likhet (Evans 2003 kap. 5).
/// </para>
/// </summary>
public sealed record SearchCriteria
{
    // JobTech v2 concept-id-format — identiskt med ListJobAdsQueryValidator
    // (CTO-rond 2026-05-13 Q7a/Q7b). Defense-in-depth default-deny.
    private static readonly Regex ConceptIdPattern =
        new(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    private const int QMinLength = 2;
    private const int QMaxLength = 100;

    /// <summary>Maxantal concept-ids per lista (ADR 0042 Beslut B invariant 2 —
    /// query-blowup/IN(...)-DoS-tak). Speglas i ListJobAdsQueryValidator.
    /// <para>ADR 0042-amendment 2026-06-09 (ADR 0067 Platsbanken sök-paritet Fas C1):
    /// 10→400. Taket = ssyk-level-4-universumets storlek (~400 yrkesgrupper) så
    /// "Välj alla yrkesgrupper" aldrig träffar taket (Klas-GO 2026-06-09, CTO-dom).
    /// "Markera alla" uttrycks som tom lista (= inget filter = alla), inte ~400
    /// materialiserade ids (FE-kontrakt Fas E). 400 är säkert mot IN(...)-DoS
    /// (B-tree-indexerad STORED-kolumn) + jsonb-dedupe (≤~15KB/sparad sökning,
    /// TOAST normalt) + ADR 0045 read-query 300ms p95. Ändligt tak består —
    /// obegränsad lista vore den verkliga DoS-vektorn. Om ssyk-universum växer
    /// förbi 400 i framtida JobTech-snapshot bör taket följa med.</para></summary>
    public const int MaxConceptIds = 400;

    public IReadOnlyList<string> Ssyk { get; private init; } = [];
    public IReadOnlyList<string> Region { get; private init; } = [];
    public string? Q { get; private init; }
    public JobAdSortBy SortBy { get; private init; }

    // EF + record copy-semantik
    private SearchCriteria() { }

    public static Result<SearchCriteria> Create(
        IEnumerable<string>? ssyk,
        IEnumerable<string>? region,
        string? q,
        JobAdSortBy sortBy)
    {
        if (!Enum.IsDefined(sortBy))
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.InvalidSortBy", "Ogiltig sortering."));

        var normSsyk = NormalizeList(ssyk);
        var normRegion = NormalizeList(region);
        var normQ = NormalizeString(q);

        if (normSsyk.Length == 0 && normRegion.Length == 0 && normQ is null)
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.Empty",
                "Minst ett sökkriterium (yrkesområde, region eller fritext) krävs."));

        if (normSsyk.Length > MaxConceptIds)
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.TooManySsyk",
                $"Max {MaxConceptIds} yrkesområden per sökning."));

        if (normRegion.Length > MaxConceptIds)
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.TooManyRegion",
                $"Max {MaxConceptIds} regioner per sökning."));

        foreach (var s in normSsyk)
        {
            if (!ConceptIdPattern.IsMatch(s))
                return Result.Failure<SearchCriteria>(DomainError.Validation(
                    "SearchCriteria.InvalidSsyk",
                    "Yrkesområde måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."));
        }

        foreach (var r in normRegion)
        {
            if (!ConceptIdPattern.IsMatch(r))
                return Result.Failure<SearchCriteria>(DomainError.Validation(
                    "SearchCriteria.InvalidRegion",
                    "Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-)."));
        }

        if (normQ is not null && (normQ.Length < QMinLength || normQ.Length > QMaxLength))
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.InvalidQ", "Söktext måste vara 2-100 tecken."));

        // ADR 0042 Beslut D — relevans-ordning utan söktext är odefinierad
        // (fail-fast; speglas i ListJobAdsQueryValidator).
        if (sortBy == JobAdSortBy.Relevance && normQ is null)
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.RelevanceRequiresQ",
                "Relevans-sortering kräver en söktext."));

        return Result.Success(new SearchCriteria
        {
            Ssyk = normSsyk,
            Region = normRegion,
            Q = normQ,
            SortBy = sortBy,
        });
    }

    // Invariant 1 (ADR 0042 Beslut B): trim per element, droppa tom/whitespace,
    // distinct ordinal, sortera ordinal. Deterministisk normalisering gör två
    // logiskt lika kriterie-uppsättningar strukturellt lika → SavedSearch
    // jsonb-dedupe fungerar (record-collection-equality är annars referens-
    // baserad). Tom/null input → tom lista (= inget filter, analogt med
    // whitespace→null för Q).
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

    private static string? NormalizeString(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Strukturell VO-likhet (Evans 2003 kap. 5). record med IReadOnlyList får
    // default REFERENS-equality → SavedSearch jsonb-dedupe skulle aldrig hitta
    // dubbletter. Listorna är redan normaliserade (sorterad+distinct ordinal)
    // i Create → sekvensjämförelse är deterministisk.
    public bool Equals(SearchCriteria? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return SortBy == other.SortBy
            && string.Equals(Q, other.Q, StringComparison.Ordinal)
            && Ssyk.SequenceEqual(other.Ssyk, StringComparer.Ordinal)
            && Region.SequenceEqual(other.Region, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SortBy);
        hash.Add(Q, StringComparer.Ordinal);
        foreach (var s in Ssyk)
            hash.Add(s, StringComparer.Ordinal);
        foreach (var r in Region)
            hash.Add(r, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
