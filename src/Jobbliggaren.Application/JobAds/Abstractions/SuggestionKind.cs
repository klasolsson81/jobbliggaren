namespace Jobbliggaren.Application.JobAds.Abstractions;

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

    /// <summary>
    /// #1546 — a distinct legal entity in the ad corpus, matched on <c>company_name</c> and carried
    /// with its org.nr so selecting it filters on <c>?employer=</c> rather than on a fuzzy name.
    /// <para>
    /// ⚠ <b>APPENDED LAST, and every future member must be too.</b> This enum has no explicit
    /// ordinals and reaches the wire as a bare integer; the frontend decodes it POSITIONALLY out of
    /// <c>SUGGESTION_KIND_ORDER</c>. Inserting anywhere else silently remaps every kind after the
    /// insertion point. <c>SuggestionKindWireContractTests</c> is what fails if you do — including a
    /// cross-language fact that reads the frontend array as source text, so appending here without
    /// appending there breaks the build rather than the client.
    /// </para>
    /// </summary>
    Employer,
}
