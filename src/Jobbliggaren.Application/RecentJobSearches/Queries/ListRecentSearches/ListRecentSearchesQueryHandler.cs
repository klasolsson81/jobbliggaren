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
            var occupationGroupLabels = await taxonomy.ResolveLabelsAsync(
                r.OccupationGroup, cancellationToken);
            var municipalityLabels = await taxonomy.ResolveLabelsAsync(
                r.Municipality, cancellationToken);
            var regionLabels = await taxonomy.ResolveLabelsAsync(
                r.Region, cancellationToken);
            // #1418 — Klass 2-labels. Reverse-lookupen är kind-agnostisk, så de här resolvar mot
            // samma cachade snapshot som de tre ovan utan port-ändring. Ovillkorligt, inte bakom
            // en grind som upprepar DeriveLabels precedens: ett andra hem för samma predikat
            // driftar isär.
            var employmentTypeLabels = await taxonomy.ResolveLabelsAsync(
                r.EmploymentType, cancellationToken);
            var worktimeExtentLabels = await taxonomy.ResolveLabelsAsync(
                r.WorktimeExtent, cancellationToken);

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
            {
                // ADR 0067 Fas C2: radens egna dimensioner in i filter-SPOT:en
                // (C1:s tomma-listor-läge täppt).
                currentCount = await search.CountAsync(
                    new JobAdFilterCriteria(
                        OccupationGroup: r.OccupationGroup,
                        Municipality: r.Municipality,
                        Region: r.Region,
                        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 i count-filtret.
                        EmploymentType: r.EmploymentType,
                        WorktimeExtent: r.WorktimeExtent,
                        // #311 PR-2b C1 (ADR 0087 D6): PR-2:s CONTAINED-seam (Employer: []) ersatt —
                        // RecentJobSearch bär nu employer_list → en återbesökt sökning räknar
                        // arbetsgivar-filtrerat.
                        Employer: r.Employer,
                        // #551 PR-D (ADR 0087 D6-paritet): PR-B:s deferrade seam (Remote: false) ersatt —
                        // RecentJobSearch bär nu remote-kolumnen → en återbesökt distans-sökning räknar
                        // distans-filtrerat (samma filter som reproduceras vid klick).
                        Remote: r.Remote,
                        Q: r.Q),
                    cancellationToken);
            }

            var newCount = Math.Max(0, currentCount - r.LastSeenCount);
            var label = DeriveLabel(
                r.Q, r.OccupationGroup, occupationGroupLabels,
                municipalityLabels, regionLabels, r.Remote,
                employmentTypeLabels, worktimeExtentLabels,
                occupationFields);

            dtos.Add(new RecentJobSearchDto(
                r.Id.Value,
                r.Q,
                OccupationGroupList: r.OccupationGroup,
                MunicipalityList: r.Municipality,
                RegionList: r.Region,
                // ADR 0067 Beslut 6 (Fas B2) — råa Klass 2-listor (inga labels, Fas E).
                EmploymentTypeList: r.EmploymentType,
                WorktimeExtentList: r.WorktimeExtent,
                Remote: r.Remote,
                OccupationGroupLabels: occupationGroupLabels,
                MunicipalityLabels: municipalityLabels,
                RegionLabels: regionLabels,
                r.SortBy,
                label,
                currentCount,
                newCount,
                r.LastViewedAt));
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
    private static RecentSearchLabelDto DeriveLabel(
        string? q,
        IReadOnlyList<string> occupationGroupIds,
        IReadOnlyList<TaxonomyLabelDto> occupationGroupLabels,
        IReadOnlyList<TaxonomyLabelDto> municipalityLabels,
        IReadOnlyList<TaxonomyLabelDto> regionLabels,
        bool remote,
        IReadOnlyList<TaxonomyLabelDto> employmentTypeLabels,
        IReadOnlyList<TaxonomyLabelDto> worktimeExtentLabels,
        IReadOnlyList<TaxonomyOccupationFieldDto>? occupationFields)
    {
        if (!string.IsNullOrWhiteSpace(q))
            return Single(RecentSearchLabelKind.Query, Named(q));
        if (occupationGroupLabels.Count > 0)
        {
            if (occupationGroupIds.Count > 1 && occupationFields is not null)
            {
                var selected = occupationGroupIds.ToHashSet(StringComparer.Ordinal);
                var wholeField = occupationFields.FirstOrDefault(f =>
                    f.OccupationGroups.Count == selected.Count
                    && f.OccupationGroups.All(g => selected.Contains(g.ConceptId)));
                if (wholeField is not null)
                    return Single(RecentSearchLabelKind.OccupationField, Named(wholeField.Label));
            }
            return Single(RecentSearchLabelKind.Dimensions, WithMoreCount(occupationGroupLabels));
        }
        if (municipalityLabels.Count > 0 || regionLabels.Count > 0 || remote)
            return DeriveOrtLabel(municipalityLabels, regionLabels, remote);
        if (employmentTypeLabels.Count > 0 || worktimeExtentLabels.Count > 0)
            return DeriveRefinementLabel(employmentTypeLabels, worktimeExtentLabels);
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
        IReadOnlyList<TaxonomyLabelDto> employmentTypeLabels,
        IReadOnlyList<TaxonomyLabelDto> worktimeExtentLabels)
    {
        // Kanonisk filter-SPOT-ordning (JobAdFilterCriteria): anställningsform → omfattning.
        // Per axel före fogningen — en hopslagen lista bryter "+N":s enhet, samma skäl som i
        // DeriveOrtLabel.
        var parts = new List<RecentSearchLabelPartDto>(2);
        if (employmentTypeLabels.Count > 0)
            parts.Add(CodedWithMoreCount(employmentTypeLabels));
        if (worktimeExtentLabels.Count > 0)
            parts.Add(CodedWithMoreCount(worktimeExtentLabels));

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
    private static RecentSearchLabelPartDto WithMoreCount(IReadOnlyList<TaxonomyLabelDto> labels) =>
        new(RecentSearchLabelPartKind.Named, labels[0].Label, ConceptId: null, labels.Count - 1);

    // Samma form, men koden i stället för namnet: klass 2-termerna är allmänsubstantiv och
    // deras ord ägs av katalogen (#1537). Id:t läses ur SAMMA TaxonomyLabelDto som WithMoreCount
    // läser sitt namn ur, så ingen parning behöver härledas på klientsidan.
    private static RecentSearchLabelPartDto CodedWithMoreCount(IReadOnlyList<TaxonomyLabelDto> labels) =>
        new(RecentSearchLabelPartKind.Coded, Text: null, labels[0].ConceptId, labels.Count - 1);
}
