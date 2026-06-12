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
/// <b>ADR 0067 Beslut 1+6 (Fas C2, 2026-06-09) — dimensionsbyte:</b>
/// <see cref="OccupationGroup"/> (ssyk-level-4/yrkesgrupp, primärt yrke-filter)
/// + <see cref="Municipality"/> (kommun) ersätter occupation-name-dimensionen
/// (Ssyk UTGICK — sparade rader reverse-lookup-migrerades occupation-name →
/// parent ssyk-level-4; senior-cto-advisor-dom (a)/(f) 2026-06-09).
/// occupation-name-substratet bevaras i job_ads (synonym-/recall-väg via Q) —
/// inte i VO:t.
/// </para>
///
/// <para>
/// <b>ADR 0067 Beslut 6 (Fas B2, 2026-06-12) — Klass 2 wirad:</b>
/// <see cref="EmploymentType"/> (anställningsform) + <see cref="WorktimeExtent"/>
/// (omfattning) är nu fullvärdiga VO-dimensioner (query-wiring landad mot
/// re-ingestad data — ~79% av Active populerad). Ortogonala axlar (oberoende
/// av varandra och övriga dims), enkel IN-equality i ApplyCriteria.
/// </para>
///
/// <para>
/// <b>ADR 0042 Beslut B (Accepted 2026-05-16) — multi-värde-invarianter:</b>
/// listorna är <c>IReadOnlyList&lt;string&gt;</c> (aldrig null; tom lista =
/// inget filter). Fyra invarianter upprätthålls i <see cref="Create"/>:
/// (1) normalisering sorterad+distinct ordinal (annars bryts SavedSearch
/// jsonb-dedupe — record-collection-equality är referensbaserad),
/// (2) maxantal-cap <see cref="MaxConceptIds"/> (query-blowup/IN(...)-DoS-
/// skydd, speglas i ListJobAdsQueryValidator), (3) generaliserad tom-invariant
/// (minst en icke-tom lista ELLER Q), (4) jsonb-bakåtkompat hanteras i
/// Infrastructure-converter (CTO Yta A3). <c>Equals</c>/<c>GetHashCode</c>
/// överrids explicit med ordinal sekvensjämförelse — SavedSearch jsonb-dedupe
/// vilar på strukturell VO-likhet (Evans 2003 kap. 5).
/// </para>
///
/// <para>
/// <b>Property-namnen är jsonb-nyckel-kontraktet (PascalCase)</b> — rename
/// utan converter+migration bryter persisterad data (SearchCriteriaJsonConverter).
/// </para>
/// </summary>
public sealed record SearchCriteria
{
    // JobTech v2 concept-id-format — identiskt med ListJobAdsQueryValidator
    // (CTO-rond 2026-05-13 Q7a/Q7b). Defense-in-depth default-deny.
    private static readonly Regex ConceptIdPattern =
        new(@"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.Compiled);

    /// <summary>Min/max-längd för fritext-<see cref="Q"/> (CTO-rond 2026-05-13
    /// Q7c). <c>public const</c> så att ListJobAdsQueryValidator (pre-handler
    /// defense-in-depth) OCH <c>ISearchQueryParser</c> (ADR 0067 Fas D2 residual-
    /// normalisering) refererar EN sanningskälla i stället för att duplicera
    /// literalerna 2/100 (DRY/SPOT, Hunt/Thomas 1999). Speglar
    /// <see cref="MaxConceptIds"/>-exponeringen.</summary>
    public const int QMinLength = 2;
    public const int QMaxLength = 100;

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

    public IReadOnlyList<string> OccupationGroup { get; private init; } = [];
    public IReadOnlyList<string> Municipality { get; private init; } = [];
    public IReadOnlyList<string> Region { get; private init; } = [];

    // ADR 0067 Beslut 6 (Fas B2, 2026-06-12) — Klass 2 anställningsform +
    // omfattning är fullvärdiga VO-dimensioner (del av sökningens identitet, ej
    // runtime-kontext). De är ORTOGONALA mot varandra och mot övriga dims —
    // till skillnad från Municipality/Region (län ⊃ kommun, geo-union) är
    // employment-type och worktime-extent oberoende axlar (enkel IN-equality,
    // AND mot allt annat; se ApplyCriteria).
    public IReadOnlyList<string> EmploymentType { get; private init; } = [];
    public IReadOnlyList<string> WorktimeExtent { get; private init; } = [];

    public string? Q { get; private init; }
    public JobAdSortBy SortBy { get; private init; }

    // EF + record copy-semantik
    private SearchCriteria() { }

    public static Result<SearchCriteria> Create(
        IEnumerable<string>? occupationGroup,
        IEnumerable<string>? municipality,
        IEnumerable<string>? region,
        IEnumerable<string>? employmentType,
        IEnumerable<string>? worktimeExtent,
        string? q,
        JobAdSortBy sortBy)
    {
        if (!Enum.IsDefined(sortBy))
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.InvalidSortBy", "Ogiltig sortering."));

        var normOccupationGroup = NormalizeList(occupationGroup);
        var normMunicipality = NormalizeList(municipality);
        var normRegion = NormalizeList(region);
        var normEmploymentType = NormalizeList(employmentType);
        var normWorktimeExtent = NormalizeList(worktimeExtent);
        var normQ = NormalizeString(q);

        if (normOccupationGroup.Length == 0 && normMunicipality.Length == 0
            && normRegion.Length == 0 && normEmploymentType.Length == 0
            && normWorktimeExtent.Length == 0 && normQ is null)
        {
            return Result.Failure<SearchCriteria>(DomainError.Validation(
                "SearchCriteria.Empty",
                "Minst ett sökkriterium (yrkesgrupp, kommun, region, anställningsform, omfattning eller fritext) krävs."));
        }

        // Cap + per-element-regex per dimension (invariant 2 + default-deny).
        // Helper bevarar per-dimension-felkoder utan duplicerade block (DRY).
        var error =
            ValidateConceptList(
                normOccupationGroup,
                "SearchCriteria.TooManyOccupationGroup",
                $"Max {MaxConceptIds} yrkesgrupper per sökning.",
                "SearchCriteria.InvalidOccupationGroup",
                "Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normMunicipality,
                "SearchCriteria.TooManyMunicipality",
                $"Max {MaxConceptIds} kommuner per sökning.",
                "SearchCriteria.InvalidMunicipality",
                "Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normRegion,
                "SearchCriteria.TooManyRegion",
                $"Max {MaxConceptIds} regioner per sökning.",
                "SearchCriteria.InvalidRegion",
                "Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normEmploymentType,
                "SearchCriteria.TooManyEmploymentType",
                $"Max {MaxConceptIds} anställningsformer per sökning.",
                "SearchCriteria.InvalidEmploymentType",
                "Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).")
            ?? ValidateConceptList(
                normWorktimeExtent,
                "SearchCriteria.TooManyWorktimeExtent",
                $"Max {MaxConceptIds} omfattningar per sökning.",
                "SearchCriteria.InvalidWorktimeExtent",
                "Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        if (error is not null)
            return Result.Failure<SearchCriteria>(error);

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
            OccupationGroup = normOccupationGroup,
            Municipality = normMunicipality,
            Region = normRegion,
            EmploymentType = normEmploymentType,
            WorktimeExtent = normWorktimeExtent,
            Q = normQ,
            SortBy = sortBy,
        });
    }

    private static DomainError? ValidateConceptList(
        string[] values,
        string tooManyCode, string tooManyMessage,
        string invalidCode, string invalidMessage)
    {
        if (values.Length > MaxConceptIds)
            return DomainError.Validation(tooManyCode, tooManyMessage);

        foreach (var v in values)
        {
            if (!ConceptIdPattern.IsMatch(v))
                return DomainError.Validation(invalidCode, invalidMessage);
        }

        return null;
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
    // i Create → sekvensjämförelse är deterministisk. Kanonisk dimensions-
    // ordning: OccupationGroup, Municipality, Region, EmploymentType,
    // WorktimeExtent (architect F1; Klass 2 tillagt Fas B2, ADR 0067 Beslut 6).
    public bool Equals(SearchCriteria? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return SortBy == other.SortBy
            && string.Equals(Q, other.Q, StringComparison.Ordinal)
            && OccupationGroup.SequenceEqual(other.OccupationGroup, StringComparer.Ordinal)
            && Municipality.SequenceEqual(other.Municipality, StringComparer.Ordinal)
            && Region.SequenceEqual(other.Region, StringComparer.Ordinal)
            && EmploymentType.SequenceEqual(other.EmploymentType, StringComparer.Ordinal)
            && WorktimeExtent.SequenceEqual(other.WorktimeExtent, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SortBy);
        hash.Add(Q, StringComparer.Ordinal);
        foreach (var g in OccupationGroup)
            hash.Add(g, StringComparer.Ordinal);
        foreach (var m in Municipality)
            hash.Add(m, StringComparer.Ordinal);
        foreach (var r in Region)
            hash.Add(r, StringComparer.Ordinal);
        foreach (var e in EmploymentType)
            hash.Add(e, StringComparer.Ordinal);
        foreach (var w in WorktimeExtent)
            hash.Add(w, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
