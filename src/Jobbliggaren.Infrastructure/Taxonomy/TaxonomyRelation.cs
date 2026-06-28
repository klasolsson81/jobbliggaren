namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0084 — a directed taxonomy relation edge (Anticorruption Layer). A
/// relation is an EDGE in a graph (source ssyk-4 yrkesgrupp → related ssyk-4
/// yrkesgrupp, many-to-many), not a node — forcing it into
/// <see cref="TaxonomyConcept"/> would break that node-table's semantics
/// (Evans 2003 ch. 14 — ACL does not distort the source model). So it gets its
/// own slim table <c>taxonomy_relations</c>.
/// <para>
/// Infrastructure-INTERNAL reference data; deliberately NO Domain type (taxonomy
/// relations are JobTech's ubiquitous language, not Jobbliggarens) and NOT on
/// <c>IAppDbContext</c> — read-model behind <c>ITaxonomyReadModel</c>, parity
/// <see cref="TaxonomyConcept"/> (ADR 0043 Beslut C / ADR 0009
/// aggregate-per-DbSet; read via <c>db.Set&lt;TaxonomyRelation&gt;()</c>). No FK
/// (loose reference — replica of external taxonomy; concept-ids are natural
/// keys, drift handled gracefully). Off-search-path (ADR 0042/0043 Beslut E).
/// </para>
/// <para>
/// The substitutability edges are rolled up from occupation-name
/// <c>substitutes</c> to ssyk-4 OFF-REPO in the generator
/// (<c>tools/taxonomy-snapshot/generate-substitutability.mjs</c>), per the
/// ADR 0084 F1 premiss-korrigering 2026-06-28: <c>substitutability</c> is
/// modelled occupation-name ↔ occupation-name in JobTech, not ssyk-4 → ssyk-4.
/// </para>
/// </summary>
internal sealed class TaxonomyRelation
{
    /// <summary>Source ssyk-4 yrkesgrupp concept-id (the user's stated group).</summary>
    public required string SourceConceptId { get; init; }

    /// <summary>Related ssyk-4 yrkesgrupp concept-id (a substitutable group).</summary>
    public required string RelatedConceptId { get; init; }

    /// <summary>Relation kind — <see cref="TaxonomyRelationKind.Substitutability"/>
    /// in v1 (ADR 0084). <c>related</c> is a documented future additive wave.
    /// A named kind, never a magic string (CLAUDE.md §5).</summary>
    public required TaxonomyRelationKind Kind { get; init; }
}

/// <summary>
/// ADR 0084 — kind of a <see cref="TaxonomyRelation"/> edge. v1 carries only
/// <see cref="Substitutability"/>; <c>related</c> (broader, noisier) is a named
/// future additive recall-wave behind the measurement step (ADR 0084 answer B).
/// Stored as string (parity <see cref="TaxonomyConceptKind"/> — readable in DB,
/// stable against enum reordering).
/// </summary>
internal enum TaxonomyRelationKind
{
    /// <summary>JobTech <c>substitutability</c> — occupation-name <c>substitutes</c>
    /// rolled up to ssyk-4 (ADR 0084 F1, premiss-korrigering 2026-06-28).</summary>
    Substitutability = 1,
}
