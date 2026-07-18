using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// Adapter (ADR 0062): mappar <see cref="ListJobAdsQuery"/> till
/// <see cref="JobAdSearchCriteria"/> och delegerar till <see cref="IJobAdSearchQuery"/>.
/// Hela sök-kompositionen (filter, FTS, sort, paginering) bor i Infrastructure-
/// impl:en bakom porten — ADR 0039 Beslut 1 SPOT delas med
/// <c>RunSavedSearchQueryHandler</c>.
/// <para>
/// <b>ADR 0067 Fas D2 (Beslut 5c):</b> live-fritexten (<c>query.Q</c>) är
/// residual-input — den körs genom <see cref="ISearchQueryParser"/> innan den
/// når filter-SPOT:en. <c>ResidualQ</c> matar <c>JobAdFilterCriteria.Q</c> →
/// FTS-hybridens OR-additiva gren (kraschsäker: residual blir aldrig hårt
/// AND-villkor; dimensionerna är separata AND-listor). RunSavedSearch parsar
/// INTE om sitt Q — det är ett persisterat, redan-normaliserat
/// <c>SearchCriteria</c>-värde (validerat vid spar-tid), inte rå residual.
/// </para>
/// <para>
/// <b>F4-14 (ADR 0076 Decision 4/5/7):</b> "Sortera efter matchning"
/// (<c>query.SortByMatch</c>) grenar till den per-användar-match-sort-porten
/// <see cref="IPerUserJobAdSearchQuery"/>. Profilen byggs ur lagrade
/// preferenser (ingen CV-läsning). Decision 7 honest fallback: ingen angiven
/// yrkesgrupp (tom SSYK-gate) → faller tillbaka till den rena default-sorten
/// (<c>SortBy</c> == PublishedAtDesc för en match-begäran), aldrig en fejkad
/// ordning. <see cref="IJobAdSearchQuery"/> förblir match-ren — match-datan
/// rör aldrig den delade SPOT-porten (Decision 5).
/// </para>
/// </summary>
public sealed class ListJobAdsQueryHandler(
    IJobAdSearchQuery search,
    IPerUserJobAdSearchQuery perUserSearch,
    IMatchProfileBuilder profileBuilder,
    ISearchQueryParser parser,
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<ListJobAdsQuery, PagedResult<JobAdDto>>
{
    // #419 punkt 1 (CTO Approach A) — det filtrerbara Fast-bandet (Grund/Relaterat/Bra/
    // Stark = rank {1,2,3,4}, ALLA positiva grader). "Visa bara matchade" injicerar denna
    // NÄR ingen specifik grad-delmängd är vald → grad-WHERE:t blir RankInSet(rank ∈
    // {1,2,3,4}) = exkludera otaggade (rank 0), dvs. exakt "bara matchade". MÅSTE spegla
    // ListJobAdsQueryValidator:s allow-list (Basic/Related/Good/Strong) + GradeToRank:s
    // positiva rankar; håll dem i lockstep om en grad någonsin läggs till/tas bort.
    private static readonly IReadOnlyList<MatchGrade> AllFilterableGrades =
        [MatchGrade.Basic, MatchGrade.Related, MatchGrade.Good, MatchGrade.Strong];

    public async ValueTask<PagedResult<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
    {
        // null → tom lista: "inget filter" (ADR 0042 Beslut B).
        // ADR 0067 Fas C2 (CTO-dom (e)): Ssyk-dimensionen borttagen ur SPOT:en —
        // q-vägens synonym-expansion mot SsykConceptId drivs separat av Q.
        // ADR 0067 Fas D2 — residual-normalisering före FTS-hybriden. Parsas EN
        // gång; samma filter används av båda grenarna (samma träff-mängd, SPOT).
        var filter = new JobAdFilterCriteria(
            OccupationGroup: query.OccupationGroup ?? [],
            Municipality: query.Municipality ?? [],
            Region: query.Region ?? [],
            EmploymentType: query.EmploymentType ?? [],
            WorktimeExtent: query.WorktimeExtent ?? [],
            // #311 D6 (ADR 0087) — arbetsgivar-facet (org.nr). Live-sök-dimension
            // (null → [], samma normalisering som dimensionerna ovan).
            Employer: query.Employer ?? [],
            // #551 PR-B D5 — den boolska distans-facetten (?remote=on) unionas med
            // kommun/län i ApplyFilter. Persistensen deferrad (se ListJobAdsQuery.Remote).
            Remote: query.Remote,
            Q: parser.Parse(query.Q).ResidualQ);

        // #383 — resolvera seekern EN gång, ENDAST när status-filtret är aktivt (status
        // är den enda per-användar-nycklade predikaten i list-vägen; match-profilen
        // resolverar användaren internt). Anon/seeker-lös begäran med aktivt status → tom
        // sida (FE döljer kontrollen; detta är defense-in-depth-backstoppen).
        var status = query.Status;
        JobSeekerId seekerId = default;
        if (status.IsActive)
        {
            seekerId = await ResolveSeekerIdAsync(cancellationToken);
            // default(JobSeekerId) = Guid.Empty → ingen seeker (paritet GetJobAdStatusBatch).
            if (seekerId == default)
                return new PagedResult<JobAdDto>([], 0, query.Page, query.PageSize);
        }

        // ADR 0079 STEG 5 — enter the per-user path when a grade filter is active
        // (MatchContextActive), the user asked for match-rank order (SortByMatch), OR
        // "Visa bara matchade" is on (#419 pt1). Decoupled: a grade filter composes with
        // ANY sort (senast inlagda / kortast ansökningstid / relevans / bästa matchning).
        if (query.MatchContextActive || query.SortByMatch || query.OnlyMatched)
        {
            // F4-15 (ADR 0076 Decision 6, R5-REBIND Option H): the global sort builds the
            // FULL profile from the confirmed plaintext skill set (ADR 0079 STEG 3 PR-D) —
            // NO DEK on the hottest path. The skill signal adds only the binary golden rung.
            // #300 PR-5a (ADR 0084 §A): includeRelated broadens the gate to exact ∪ related so
            // a Related grade filter / match-sort ranks related ads at the Related rung. Driven by
            // the live ?relaterade=on toggle (off by default → exact-only, byte-identical to pre-#300).
            var profile = await profileBuilder.BuildFullForSortAsync(
                cancellationToken, includeRelated: query.IncludeRelated);

            // SSYK-gate (parity F4-13 GetJobAdMatchBatch): utan angiven yrkesgrupp kan
            // ingen annons få en grad → grad-filter/match-ordning/"bara matchade" vore
            // meningslös. Honest fallback till default-sorten (Decision 7) med den valda
            // sorten, aldrig en tom grad-filtrerad sida (CTO-re-bind case 2). FE döljer
            // kontrollerna (Matchning-pillen + "Visa bara matchade") då.
            if (profile.Fast.SsykGroupConceptIds.Count > 0)
            {
                // #419 pt1 (CTO Approach A) — "Visa bara matchade": NÄR användaren bett om
                // bara-matchade och INTE smalnat till en specifik grad-delmängd, injicera hela
                // det filtrerbara Fast-bandet (= alla positiva rankar {1,2,3,4}). Detta
                // återbrukar det BEFINTLIGA positiv-only grad-WHERE:t (RankInSet över rank > 0)
                // verbatim — ingen ny port-param, ingen andra rank>0-Expression (DRY; CTO-bind).
                // En specifik grad-delmängd VINNER (only-matched = det tomma-delmängds-fallet);
                // Related (rank 2) matchar bara när ?relaterade=on breddat profilen, så att
                // injicera den är inert med toggle:n av.
                IReadOnlyList<MatchGrade> grades =
                    query.MatchGrades is { Count: > 0 } subset ? subset
                    : query.OnlyMatched ? AllFilterableGrades
                    : [];

                // #383 — status (om aktivt) komponeras IN i match-vägen: samma grad-WHERE/
                // rank, plus status-EXISTS:en ovanpå den delade filter-SPOT:en (counten räknas
                // om över båda). Inaktivt status ⇒ JobAdStatusFilter.None (no-op, byte-for-byte).
                return await perUserSearch.SearchPerUserAsync(
                    filter,
                    profile,
                    grades: grades,
                    sort: query.SortBy,
                    orderByMatchRank: query.SortByMatch,
                    status,
                    seekerId,
                    query.Page,
                    query.PageSize,
                    cancellationToken);
            }
        }

        // #383 — ingen match-gate (inget angivet yrke / matchning av): är status aktivt
        // körs den frikopplade status-only-vägen (ingen profil/grad-rank), annars den
        // delade anonyma sökningen. SRP: "visa mina sparade/ansökta" fungerar utan SSYK.
        if (status.IsActive)
        {
            return await perUserSearch.SearchByStatusAsync(
                filter, seekerId, status, query.SortBy, query.Page, query.PageSize,
                cancellationToken);
        }

        return await search.SearchAsync(
            new JobAdSearchCriteria(
                filter, query.SortBy, query.Page, query.PageSize),
            cancellationToken);
    }

    // #383 — seeker-resolution (paritet GetJobAdStatusBatchQueryHandler): UserId →
    // JobSeekerId. Anonym (ingen UserId) eller ingen JobSeeker-rad → Empty (anroparen
    // returnerar en tom sida). .AsNoTracking() (CLAUDE §3.6, ren läsning).
    private async ValueTask<JobSeekerId> ResolveSeekerIdAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return default;

        return await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
