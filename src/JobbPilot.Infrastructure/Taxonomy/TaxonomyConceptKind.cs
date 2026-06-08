namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 Variant A + ADR 0043-amendment 2026-06-08 (kommun + yrkesgrupp,
/// ADR 0067 Platsbanken sök-paritet). Persisteras som <c>string</c> (läsbart i
/// DB, stabilt mot enum-omordning). Kind ÄR nivå-diskriminatorn — ingen redundant
/// Level-int (DRY). Kommun→län och yrkesgrupp→yrkesområde är båda 1:1.
/// </summary>
internal enum TaxonomyConceptKind
{
    /// <summary>JobTech <c>region</c> — län (~21). Rot för kommun.</summary>
    Region,

    /// <summary>JobTech <c>occupation-field</c> — yrkesområde (~21). Rot.</summary>
    OccupationField,

    /// <summary>JobTech <c>occupation-name</c> — yrke (~2179). Barn till
    /// OccupationField; concept-id matchar <c>job_ads.ssyk_concept_id</c>.
    /// Bevaras som synonym-/recall-substrat (ADR 0067 Beslut 1) — degraderad
    /// från primärt yrke-filter till förmån för OccupationGroup.</summary>
    Occupation,

    /// <summary>JobTech <c>municipality</c> — kommun (~290). Barn till Region
    /// (1:1); concept-id matchar <c>job_ads.municipality_concept_id</c>
    /// (ADR 0043-amendment 2026-06-08).</summary>
    Municipality,

    /// <summary>JobTech <c>ssyk-level-4</c> — yrkesgrupp (~400). Barn till
    /// OccupationField (1:1); concept-id matchar
    /// <c>job_ads.occupation_group_concept_id</c>. Primärt yrke-filter för
    /// Platsbanken-paritet (ADR 0067 Beslut 1 — namnglapp: annons-fältet heter
    /// occupation_group men pekar på ssyk-level-4).</summary>
    OccupationGroup,
}
