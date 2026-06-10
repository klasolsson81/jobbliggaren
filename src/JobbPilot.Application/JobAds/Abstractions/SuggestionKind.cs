namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Typ av typeahead-förslag (ADR 0067 Beslut 5a — utökad suggest-union).
/// Ren Application-presentations-enum, frikopplad från Infrastructures interna
/// <c>TaxonomyConceptKind</c> (som är <c>internal</c> och aldrig får korsa
/// Application-gränsen — CLAUDE.md §2.1, ADR 0043 ACL). Anti-magic-string per
/// CLAUDE.md §5.1 (senior-cto-advisor 2026-06-10, VAL 3 = Variant B).
/// <para>
/// Medlemsmängden = exakt de källor som faktiskt emitteras i unionen.
/// occupation-name (<c>Occupation</c>) ingår INTE — det saknar filter-dimension
/// i <see cref="JobAdFilterCriteria"/> (chip utan mål = återvändsgränd; VAL 4 =
/// Variant A). occupation-name nås ändå som recall via q-FTS-synonym-grenen.
/// </para>
/// </summary>
public enum SuggestionKind
{
    /// <summary>Fri titel-prefix-träff ur <c>job_ads.Title</c> (ADR 0042 Beslut C).
    /// Saknar concept-id.</summary>
    Title,

    /// <summary>Län (taxonomi-snapshot).</summary>
    Region,

    /// <summary>Kommun (taxonomi-snapshot).</summary>
    Municipality,

    /// <summary>Yrkesområde (taxonomi-snapshot).</summary>
    OccupationField,

    /// <summary>Yrkesgrupp / ssyk-level-4 (taxonomi-snapshot).</summary>
    OccupationGroup,
}
