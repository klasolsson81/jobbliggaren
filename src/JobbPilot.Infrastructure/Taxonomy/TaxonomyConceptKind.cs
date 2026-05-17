namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 Variant A-scope. Persisteras som <c>string</c> (läsbart i DB,
/// stabilt mot enum-omordning). Ingen <c>Municipality</c> — Län→Kommun är
/// payload-verifierings-trigger (ADR 0043, ej levererad i Variant A).
/// </summary>
internal enum TaxonomyConceptKind
{
    /// <summary>JobTech <c>region</c> — län (~21). Enkelnivå.</summary>
    Region,

    /// <summary>JobTech <c>occupation-field</c> — yrkesområde (~21). Rot.</summary>
    OccupationField,

    /// <summary>JobTech <c>occupation-name</c> — yrke. Barn till
    /// OccupationField; concept-id matchar <c>job_ads.ssyk_concept_id</c>.</summary>
    Occupation,
}
