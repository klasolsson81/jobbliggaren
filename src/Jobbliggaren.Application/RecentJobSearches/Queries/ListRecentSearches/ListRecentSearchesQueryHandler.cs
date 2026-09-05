using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;

/// <summary>
/// ADR 0060 — list-projektion för auto-fångade RecentJobSearches per JobSeeker.
/// Avsiktlig N+1 i CurrentCount-loopen (CTO 2026-05-20 Variant A): cap=20
/// (<c>RecentJobSearch.MaxPerSeeker</c>) håller fan-out hanterbart; varje
/// träffräkning går via <see cref="IJobAdSearchQuery.CountAsync"/> (ADR 0062 —
/// samma filter-SPOT som ListJobAds, q-FTS-accelererad). ADR 0060 Beslut 4 förutsåg en
/// ADR 0045 fitness function på endpointen; den är inte byggd — se kommentaren i loopen.
///
/// <para>Label server-härleds så FE inte behöver konstruera presentation
/// (ADR 0060; E2g 2026-06-11).</para>
///
/// <para><b>Fas C2 (ADR 0067):</b> entiteten bär OccupationGroup + Municipality
/// (occupation-name/Ssyk utgick) — mappas in i filter-SPOT:en (täpper C1:s
/// tomma listor). <b>Fas E2b:</b> C2-shimmets deprecated SsykList/SsykLabels
/// borttagna (FE-zod frikopplad sedan E2a — architect F5-planen utförd).</para>
/// </summary>
public sealed class ListRecentSearchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IJobAdSearchQuery search)
    : IQueryHandler<ListRecentSearchesQuery, IReadOnlyList<RecentJobSearchDto>>
{
    public async ValueTask<IReadOnlyList<RecentJobSearchDto>> Handle(
        ListRecentSearchesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        var items = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId)
            .OrderByDescending(r => r.LastViewedAt)
            .ToListAsync(cancellationToken);

        // E2g (Klas-direktiv 2026-06-11): hel-områdes-kollaps i labeln kräver
        // fält→grupp-trädet. In-memory-snapshot (ADR 0043 — ingen extern hop);
        // hämtas EN gång per Handle (CTO-krav), och bara när någon rad har >1
        // yrkesgrupp (enda fallet kollapsen kan behövas — q-rader når aldrig
        // grupp-grenen men extra-hämtningen är gratis mot in-memory-cachen).
        IReadOnlyList<TaxonomyOccupationFieldDto>? occupationFields = null;
        if (items.Any(r => r.OccupationGroup.Count > 1))
        {
            occupationFields =
                (await taxonomy.GetTreeAsync(cancellationToken))?.OccupationFields;
        }

        var dtos = new List<RecentJobSearchDto>(items.Count);
        foreach (var r in items)
        {
            // #1471 — ONE criteria object per row, and all three consumers read it: the count
            // (CountAsync), the projection the replay href is built from, and the label. The
            // entity's axes used to be copied into each by hand, and every divergence between
            // the copies shipped as a defect (#1407 dropped remote from the projection, #1471
            // dropped employer), so no consumer reads the entity's axes on its own any more.
            //
            // Employer passes the same gate the capture behavior persists through (ADR 0087
            // D8(c), masked arm): a row written before A2 (2026-08-19) can still carry a
            // personnummer-shaped value, and it reaches none of the three. The count is then
            // computed on the same masked criterion the click runs, so count == replay holds
            // for that row too.
            var replay = new JobAdFilterCriteria(
                OccupationGroup: r.OccupationGroup,
                Municipality: r.Municipality,
                Region: r.Region,
                EmploymentType: r.EmploymentType,
                WorktimeExtent: r.WorktimeExtent,
                Employer: EmployerAxisGate.Surfaceable(r.Employer),
                Remote: r.Remote,
                Q: r.Q);

            var occupationGroupLabels = await taxonomy.ResolveLabelsAsync(
                replay.OccupationGroup, cancellationToken);
            var municipalityLabels = await taxonomy.ResolveLabelsAsync(
                replay.Municipality, cancellationToken);
            var regionLabels = await taxonomy.ResolveLabelsAsync(
                replay.Region, cancellationToken);

            // F6 P5 P4 svans-PR4 (2026-05-24, Klas perf-feedback /oversikt 7-10s):
            // Per-row COUNT är sekventiell (CTO Variant A 2026-05-20 — cap=20
            // N+1). När `IncludeCount=false` skippar vi COUNT.
            //
            // 2026-06-13: SIDLADDNINGEN hämtar med IncludeCount=false (/oversikt,
            // /sokningar, /jobb hero-chip) — den slow N+1-COUNT:en återskapar annars
            // 8s-timeouten (Npgsql 57014) på kritisk väg. currentCount/newCount är
            // därför 0 i den listan, och en falsk "(0)" renderas aldrig (CTO-beslut
            // 2026-06-13: hellre ingen siffra).
            //
            // Talet visas ändå, och det kommer från GRENEN NEDAN: den lata
            // klient-hämtningen (B, useFacetCounts-mönstret) är levererad —
            // use-recent-search-counts.ts → /api/me/recent-searches/counts →
            // getRecentSearches(true) → hit med IncludeCount=true, off-critical-path.
            // Ta alltså inte bort grenen som död kod; den är den enda producenten av
            // siffran.
            //
            // Vad som kostar är FAN-OUT, inte per-count: TD-94:s per-count-rot är fixad
            // (ADR 0062 Amendment 2026-06-13). Fan-out:en cap=20 är accepterad i ADR 0060
            // Beslut 4 och OMÄTT — inget perf-scenario träffar den här endpointen. Det som
            // håller den borta från kritisk väg är IncludeCount=false ovan, inget annat.
            int currentCount = 0;
            if (query.IncludeCount)
                currentCount = await search.CountAsync(replay, cancellationToken);

            var newCount = Math.Max(0, currentCount - r.LastSeenCount);
            var label = DeriveLabel(
                replay, occupationGroupLabels, municipalityLabels, regionLabels, occupationFields);

            // Named throughout: eight positional lists in a row is the transposition trap
            // JobAdFilterCriteria's own docblock names, and the raw dimensions below are the
            // replay — a swapped pair would count one search and run another.
            dtos.Add(new RecentJobSearchDto(
                Id: r.Id.Value,
                Q: replay.Q,
                OccupationGroupList: replay.OccupationGroup,
                MunicipalityList: replay.Municipality,
                RegionList: replay.Region,
                EmploymentTypeList: replay.EmploymentType,
                WorktimeExtentList: replay.WorktimeExtent,
                EmployerList: replay.Employer,
                Remote: replay.Remote,
                OccupationGroupLabels: occupationGroupLabels,
                MunicipalityLabels: municipalityLabels,
                RegionLabels: regionLabels,
                SortBy: r.SortBy,
                Label: label,
                CurrentCount: currentCount,
                NewCount: newCount,
                LastViewedAt: r.LastViewedAt));
        }

        return dtos;
    }

    // E2g (Klas-direktiv 2026-06-11, CTO-bekräftad mekanik): "första labeln"
    // var missvisande vid multi-val ("Drifttekniker, IT" när hela Data/IT
    // valts). Ny regel per dimension: (i) selektion = EXAKT alla grupper i
    // ETT yrkesområde (mängd-likhet mot trädet) → områdets namn; (ii) ett
    // val → namnet; (iii) annars → "{första} +{N−1} till". Blandfall (helt
    // område + extra grupper) → (iii) räknat på grupper. Taxonomi-drift →
    // (i)-matchen faller gracefully till (iii). "{första}" är deterministisk
    // (resolvad label-ordning = persisterad sorterad id-ordning).
    //
    // #1430: reglerna är oförändrade, formen är det inte. Metoden härleder VILKEN
    // dimension som namnger raden och HUR delarna hänger ihop; orden som renderar
    // det är locale-copy och ligger i messages/{sv,en}/jobads.json. Grenen namnges
    // explicit i deskriptorn — även q-grenen — så FE aldrig härleder om den.
    //
    // #1471: the criteria handed in is the SAME object the count ran on and the projection is
    // built from, so the label cannot name an axis the click drops, nor drop one the count kept.
    private static RecentSearchLabelDto DeriveLabel(
        JobAdFilterCriteria criteria,
        IReadOnlyList<TaxonomyLabelDto> occupationGroupLabels,
        IReadOnlyList<TaxonomyLabelDto> municipalityLabels,
        IReadOnlyList<TaxonomyLabelDto> regionLabels,
        IReadOnlyList<TaxonomyOccupationFieldDto>? occupationFields)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Q))
            return Single(RecentSearchLabelKind.Query, Named(criteria.Q));
        if (occupationGroupLabels.Count > 0)
        {
            if (criteria.OccupationGroup.Count > 1 && occupationFields is not null)
            {
                var selected = criteria.OccupationGroup.ToHashSet(StringComparer.Ordinal);
                var wholeField = occupationFields.FirstOrDefault(f =>
                    f.OccupationGroups.Count == selected.Count
                    && f.OccupationGroups.All(g => selected.Contains(g.ConceptId)));
                if (wholeField is not null)
                    return Single(RecentSearchLabelKind.OccupationField, Named(wholeField.Label));
            }
            return Single(RecentSearchLabelKind.Dimensions, WithMoreCount(occupationGroupLabels));
        }
        if (municipalityLabels.Count > 0 || regionLabels.Count > 0 || criteria.Remote)
            return DeriveOrtLabel(municipalityLabels, regionLabels, criteria.Remote);
        if (criteria.EmploymentType.Count > 0 || criteria.WorktimeExtent.Count > 0
            || criteria.Employer.Count > 0)
            return DeriveRefinementLabel(criteria.EmploymentType, criteria.WorktimeExtent, criteria.Employer);
        return new RecentSearchLabelDto(RecentSearchLabelKind.All, RecentSearchLabelJoin.None, []);
    }

    private static RecentSearchLabelPartDto Named(string text) =>
        new(RecentSearchLabelPartKind.Named, text, ConceptId: null, MoreCount: 0);

    private static RecentSearchLabelDto Single(
        RecentSearchLabelKind kind,
        RecentSearchLabelPartDto part) =>
        new(kind, RecentSearchLabelJoin.None, [part]);

    // #1418 — förfiningsaxlarna namnger raden när ingen primär dimension gör det. Grenen nås
    // bara därifrån, så ordningen q → yrkesgrupp → ort är orörd för varje rad som HAR en
    // primär dimension.
    //
    // Ort är EN dimension i tre granulariteter och unioneras, därav Disjunction i
    // DeriveOrtLabel. De här är ortogonala AND-axlar (JobAdSearchComposition) — en disjunktion
    // vore semantiskt falsk, så fogningen är Conjunction (Klas-beslut 2026-08-23). Varje satt
    // axel räknas upp: att namnge bara en av dem beskriver en äkta ÖVERMÄNGD av vad klicket
    // kör, spegelbilden av det ort-fall DeriveOrtLabel finns för. Anropas bara när minst en
    // del är satt — samma call-site-invariant som WithMoreCount.
    private static RecentSearchLabelDto DeriveRefinementLabel(
        IReadOnlyList<string> employmentTypeIds,
        IReadOnlyList<string> worktimeExtentIds,
        IReadOnlyList<string> employers)
    {
        // Kanonisk filter-SPOT-ordning (JobAdFilterCriteria): anställningsform → omfattning →
        // arbetsgivare. Per axel före fogningen — en hopslagen lista bryter "+N":s enhet, samma
        // skäl som i DeriveOrtLabel.
        var parts = new List<RecentSearchLabelPartDto>(3);
        if (employmentTypeIds.Count > 0)
            parts.Add(CodedWithMoreCount(employmentTypeIds));
        if (worktimeExtentIds.Count > 0)
            parts.Add(CodedWithMoreCount(worktimeExtentIds));
        // #1471 — value-free, unlike its two siblings: the value is an org.nr, and for an enskild
        // firma that is the holder's personnummer (#841), so the part carries neither Text nor
        // ConceptId (Klas 2026-08-23). MoreCount counts the same unit as every other part.
        if (employers.Count > 0)
            parts.Add(new RecentSearchLabelPartDto(
                RecentSearchLabelPartKind.Employer, Text: null, ConceptId: null, employers.Count - 1));

        return new RecentSearchLabelDto(
            RecentSearchLabelKind.Dimensions,
            parts.Count == 1 ? RecentSearchLabelJoin.None : RecentSearchLabelJoin.Conjunction,
            parts);
    }

    // Ort är EN dimension: län ⊃ kommun, plus distans som boolesk sub-axel. Geo-
    // predikatet UNIONERAR dem (kommun ∨ län ∨ distans, JobAdSearchComposition
    // #551 PR-B D5), så labeln räknar upp varje satt del i stället för att namna
    // den första: en rad med kommun+distans som heter "Stockholm" namnger en
    // strikt delmängd av vad klicket kör. Fogningsformen är ett Klas-beslut
    // 2026-08-19. Anropas bara när minst en del är satt — samma call-site-
    // invariant som WithMoreCount.
    //
    // Distans-delen bär inget namn: den är ett ORD, inte taxonomidata, och vilket
    // ord beror på både locale och position (svenskan versaliserar det bara först).
    // Positionen är läsbar ur Parts-ordningen, så FE behöver ingen egen flagga.
    private static RecentSearchLabelDto DeriveOrtLabel(
        IReadOnlyList<TaxonomyLabelDto> municipalityLabels,
        IReadOnlyList<TaxonomyLabelDto> regionLabels,
        bool remote)
    {
        // Per del, före fogningen — en hopslagen lista bryter "+N":s enhet.
        var parts = new List<RecentSearchLabelPartDto>(3);
        if (municipalityLabels.Count > 0)
            parts.Add(WithMoreCount(municipalityLabels));
        if (regionLabels.Count > 0)
            parts.Add(WithMoreCount(regionLabels));
        if (remote)
            parts.Add(new RecentSearchLabelPartDto(
                RecentSearchLabelPartKind.Remote, Text: null, ConceptId: null, MoreCount: 0));

        return new RecentSearchLabelDto(
            RecentSearchLabelKind.Dimensions,
            parts.Count == 1 ? RecentSearchLabelJoin.None : RecentSearchLabelJoin.Disjunction,
            parts);
    }

    // "{första} +{N−1}" — +N räknar samma enhet som första namnet anger.
    //
    // Ett id snapshoten inte kände (taxonomi-drift) har inget namn att ange, så delen bär
    // koden i stället och klienten namnger den ur sin katalog — exakt CodedWithMoreCount
    // nedan, av samma skäl: det ord som saknas är locale-copy, inte registerdata (#1540).
    // Urvalsregeln är oförändrad: första elementet namnger delen, resten räknas.
    private static RecentSearchLabelPartDto WithMoreCount(IReadOnlyList<TaxonomyLabelDto> labels) =>
        labels[0].Label is { } text
            ? new(RecentSearchLabelPartKind.Named, text, ConceptId: null, labels.Count - 1)
            : new(RecentSearchLabelPartKind.Coded, Text: null, labels[0].ConceptId, labels.Count - 1);

    // Samma form, men koden i stället för namnet: klass 2-termerna är allmänsubstantiv och
    // deras ord ägs av katalogen (#1537). Tar id:na direkt ur kriteriet — de ÄR det som ska
    // på wire:n, så ingen etikett behöver hämtas och kastas.
    private static RecentSearchLabelPartDto CodedWithMoreCount(IReadOnlyList<string> conceptIds) =>
        new(RecentSearchLabelPartKind.Coded, Text: null, conceptIds[0], conceptIds.Count - 1);
}
