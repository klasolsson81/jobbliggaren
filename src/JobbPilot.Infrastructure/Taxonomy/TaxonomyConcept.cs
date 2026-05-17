namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 — snapshot-rad av JobTech-taxonomin (Anticorruption Layer).
/// Infrastructure-INTERN replika av extern referensdata; medvetet INGEN
/// Domain-typ (taxonomi är inte JobbPilots ubiquitous language, Evans
/// kap. 14) och INTE på <c>IAppDbContext</c> (ADR 0043 Beslut C / MAP-2 —
/// read-model utan aggregate/invariant; ADR 0009 aggregate-per-DbSet).
/// Concept-id är PK; ingen FK till job_ads/saved_searches (lös referens —
/// ADR 0043, replika av extern taxonomi). Variant A-scope: Region (län),
/// OccupationField (yrkesområde), Occupation (yrke). Ingen kommun.
/// </summary>
internal sealed class TaxonomyConcept
{
    /// <summary>JobTech concept-id (PK). Matchar shadow-props
    /// <c>region_concept_id</c> / <c>ssyk_concept_id</c> vid filtrering —
    /// men filtreringen sker UTANFÖR denna tabell (ADR 0043 Beslut E).</summary>
    public required string ConceptId { get; init; }

    public required TaxonomyConceptKind Kind { get; init; }

    /// <summary>Svenskt visningsnamn (JobTech preferred-label).</summary>
    public required string Label { get; init; }

    /// <summary>För Occupation: yrkesområdets concept-id. Null för
    /// Region och OccupationField (enkelnivå respektive rot).</summary>
    public string? ParentConceptId { get; init; }
}
