using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;

namespace Jobbliggaren.Application.JobAds.Abstractions;

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

    /// <summary>
    /// ADR 0084 — yrkesmatchnings-breddning. Givet en användares EXAKT angivna
    /// ssyk-4-yrkesgrupper, returnera de RELATERADE (utbytbara) ssyk-4-grupperna
    /// per JobTechs <c>substitutability</c> (occupation-name <c>substitutes</c>
    /// rollat upp till ssyk-4 off-repo i generatorn — F1 premiss-korrigering
    /// 2026-06-28). Resultatet UTESLUTER de exakt angivna grupperna själva, så
    /// exakt-vs-relaterad förblir disjunkt (scorern/SQL splittar dem i PR-2+).
    /// <para>
    /// Ren in-memory-uppslagning mot den redan cachade snapshoten (ingen ny
    /// per-request-DB-träff — ADR 0043 §1.4). Okänd käll-grupp (taxonomi-drift,
    /// inga relationer) bidrar med tomt — aldrig null/throw (graceful
    /// degradation, paritet <see cref="ResolveLabelsAsync"/>). Ligger UTANFÖR
    /// <c>JobAdSearch.ApplyCriteria</c>-shadow-prop-vägen (ADR 0043 Beslut E).
    /// Deterministisk ordning (Ordinal) → stabila tester. v1: endast
    /// <c>substitutes</c>-riktningen, any-member-rollup (ADR 0084 svar B).
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
        IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken);

    /// <summary>
    /// #477 Low 1 — kommun→län-containment för ort-matchningen. Givet en användares
    /// EXAKT angivna kommun-concept-ids, returnera de LÄN (region-concept-ids) som
    /// INNEHÅLLER dem, via <c>TaxonomyConcept.ParentConceptId</c> (kommun är barn under
    /// län, 1:1 — samma <c>municipalitiesByRegion</c>-relation som redan cachas, läst
    /// baklänges). Låter <c>MatchProfileBuilder</c> härleda den härledda mängden
    /// <c>CandidateMatchProfile.ContainmentRegionConceptIds</c> så en kommun-only-
    /// preferens inte längre RB1-golvar en län-only-annons i användarens EGEN kommuns
    /// län till Basic som en plats-"motsägelse" (scorern läser då den annonsen som
    /// <c>NotAssessed</c>, aldrig <c>NoMatch</c>).
    /// <para>
    /// Ren in-memory-uppslagning mot den redan cachade snapshoten (ingen ny
    /// per-request-DB-träff — ADR 0043 §1.4; datan seedas redan av
    /// <c>TaxonomySnapshotSeeder</c>, ingen migration). Resultatet är dedupliserat
    /// (flera kommuner i samma län → ETT län) och EXKLUDERAR inget — en kommun vars län
    /// användaren redan valt som region-preferens dyker upp i båda mängderna, vilket är
    /// harmlöst (scorern unionsar dem). Okänd kommun (taxonomi-drift, saknad förälder)
    /// bidrar inget — aldrig null/throw (graceful degradation, paritet
    /// <see cref="ResolveLabelsAsync"/>/<see cref="GetRelatedOccupationGroupsAsync"/>).
    /// Deterministisk ordning (Ordinal) → stabila tester. Ligger UTANFÖR
    /// <c>JobAdSearch.ApplyCriteria</c>-shadow-prop-vägen (ADR 0043 Beslut E).
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
        IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken);

    /// <summary>
    /// Fas 4b 8b.4a — yrkesgrupp→yrkesområde-containment för CV:ts sektionsförslag. Givet en
    /// användares BEKRÄFTADE ssyk-4-yrkesgrupper (<c>MatchPreferences.PreferredOccupationGroups</c>),
    /// returnera de YRKESOMRÅDEN (occupation-field-concept-ids) som INNEHÅLLER dem, via
    /// <c>TaxonomyConcept.ParentConceptId</c> (yrkesgrupp är barn under yrkesområde, 1:1 — samma
    /// <c>groupsByField</c>-relation som redan cachas, läst BAKLÄNGES). Exakt spegling av
    /// <see cref="GetContainingRegionsAsync"/>; yrkesområdet är nyckeln branschgrupp-assetet
    /// slår upp på (ADR 0107).
    /// <para>
    /// Ren in-memory-uppslagning mot den redan cachade snapshoten (ingen ny per-request-DB-träff —
    /// ADR 0043 §1.4; datan seedas redan av <c>TaxonomySnapshotSeeder</c>, ingen migration).
    /// Resultatet är dedupliserat (flera yrkesgrupper i samma område → ETT område) — och att det
    /// KAN returnera flera områden är hela poängen: den anropande läs-slicen vägrar gissa när en
    /// användares yrkesval spänner två branschgrupper. Okänd yrkesgrupp (taxonomi-drift, saknad
    /// förälder) bidrar inget — aldrig null/throw (graceful degradation, paritet
    /// <see cref="ResolveLabelsAsync"/>/<see cref="GetContainingRegionsAsync"/>). Deterministisk
    /// Ordinal-ordning → stabila tester. Ligger UTANFÖR <c>JobAdSearch.ApplyCriteria</c>-
    /// shadow-prop-vägen (ADR 0043 Beslut E).
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
        IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken);
}
