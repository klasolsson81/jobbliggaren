using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Application-port för taxonomi-ACL (ADR 0043 — Anticorruption Layer,
/// Evans 2003 kap. 14). JobTech-taxonomins concept-id är ett externt
/// systems ubiquitous language; den läcker aldrig till slutanvändaren.
/// Denna port översätter namn↔concept-id i presentations-/inmatnings-
/// skiktet (picker-träd + reverse-lookup) men ligger UTANFÖR sök-/filter-
/// vägen — <c>JobAdSearch.ApplyCriteria</c> filtrerar fortsatt namn-omedvetet
/// på shadow-props (ADR 0043 Beslut E — shadow-prop-filtrering ORÖRD).
/// Implementationen ligger i Infrastructure (snapshot-tabell + cache);
/// Application ser bara denna port (CLAUDE.md §2.1, speglar
/// <see cref="IJobSource"/>). Scope (ADR 0043 Variant A): Län (region) +
/// Yrkesområde→Yrke (occupation-field→occupation-name). Ingen kommun.
/// </summary>
public interface ITaxonomyReadModel
{
    /// <summary>
    /// Hela picker-trädet: län (platt) + yrkesområden med underordnade yrken.
    /// Statiskt och bounded (~21 län, ~21 yrkesområden, ~2 700 yrken) →
    /// ingen paginering/användarstyrd Take. Ren Application-DTO; inga
    /// EF-entities över Application-gränsen (CLAUDE.md §5.1).
    /// </summary>
    ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reverse-lookup för redan-sparade sökningar/valda chips: concept-id →
    /// namn. Okänt id (taxonomi-drift, borttagen kod) ger fallback-label
    /// <c>"Okänd kod (&lt;id&gt;)"</c> — aldrig null/throw. Sökningen fungerar
    /// ändå (filtrering sker på rå concept-id mot shadow-props; namnet är
    /// ren presentation). Graceful degradation, ingen data-migration.
    /// </summary>
    ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
        IReadOnlyList<string> conceptIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Prefix-sök mot taxonomi-snapshotens labels (ADR 0067 Beslut 5a — utökad
    /// typeahead-suggest). Matchar Län/Kommun/Yrkesområde/Yrkesgrupp vars namn
    /// börjar med <paramref name="prefix"/> (case-insensitivt). occupation-name
    /// ingår INTE (saknar filter-dimension; senior-cto-advisor 2026-06-10 VAL 4).
    /// <para>
    /// Ren in-memory-scan av den redan cachade snapshoten — bryter EJ ADR 0043:s
    /// extern-hop-förbud på sök-vägen (ingen DB-/API-träff per tangenttryck).
    /// Porten översätter Infrastructures interna <c>TaxonomyConceptKind</c> till
    /// publika <see cref="SuggestionKind"/> (ACL — <c>TaxonomyConceptKind</c> är
    /// <c>internal</c> och får aldrig korsa Application-gränsen). Cappas till
    /// <paramref name="limit"/> (delas med titel-grenen i union-handlern).
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
        string prefix, int limit, CancellationToken cancellationToken);
}
