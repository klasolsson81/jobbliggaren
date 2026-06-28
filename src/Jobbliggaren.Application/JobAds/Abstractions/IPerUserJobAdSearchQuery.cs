using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// ADR 0076 Decision 4/5 + ADR 0079 STEG 5 — den PER-ANVÄNDAR-sökvägen för
/// <c>/jobb</c>. En SEPARAT port från <see cref="IJobAdSearchQuery"/>: den senare
/// delas med <c>RunSavedSearch</c> och MÅSTE förbli match-ren och anonymt cachebar
/// (Decision 5) — den här porten bär per-användar-profilen och är därmed inneboende
/// icke-cachebar (korrekt: en personlig vy).
/// <para>
/// <b>Två ordnings-lägen (ADR 0079 STEG 5 re-bind, 2026-06-23):</b> grad-filter och
/// sort-ordning är FRIKOPPLADE. Porten kan
/// <list type="bullet">
/// <item>rangordna på MATCH-RANK (grad fallande, Stark→Bra→Grund→otaggade sist +
/// gyllene skill-rung) när användaren valt "bästa matchning"
/// (<c>orderByMatchRank</c>), ELLER</item>
/// <item>rangordna på en REN axel (senast inlagda / kortast ansökningstid / relevans)
/// via den delade <c>JobAdSearchComposition.ApplySort</c> — medan grad-filtret
/// fortfarande gallrar mängden.</item>
/// </list>
/// I båda fallen appliceras grad-WHERE (om grader valda) ovanpå EXAKT samma
/// filter-SPOT som <see cref="IJobAdSearchQuery.SearchAsync"/>
/// (<c>JobAdSearchComposition.ApplyFilter</c>) — match-sorten/grad-filtret kan aldrig
/// träffa en annan annons-mängd än default-sorten.
/// </para>
/// <para>
/// <b>Grad-filtret (Fast-bandet) — Goodhart bevarad (Decision 4):</b> grad-ranken
/// (1=Grund/2=Bra/3=Stark, 0=otaggad) lever ENBART i <c>WHERE</c>/<c>ORDER BY</c> —
/// den returneras aldrig i <see cref="JobAdDto"/>, persisteras aldrig, renderas aldrig.
/// EN delad rank-Expression (<c>GradeRankExpression</c>) konsumeras av grad-WHERE,
/// count och match-rank-ORDER BY (DRY). Bandet är Fast (toppar på Stark) — Topp kan
/// inte beräknas i SQL (ingen kind-separerad must-have-lexem-kolumn; G3-OPT-A) och
/// avvisas wire-side av validatorn. Ett Testcontainers-orakel pinnar att SQL-ranken ≡
/// <c>MatchGradeCalculator.Grade(MatchScore)</c> (Fast) över hela verdict-tuple-rymden.
/// </para>
/// </summary>
public interface IPerUserJobAdSearchQuery
{
    /// <summary>
    /// Filtrerar (samma SPOT som <see cref="IJobAdSearchQuery.SearchAsync"/>),
    /// applicerar grad-WHERE (om <paramref name="grades"/> icke-tom), rangordnar
    /// (match-rank om <paramref name="orderByMatchRank"/>, annars
    /// <paramref name="sort"/>) och paginerar. Returnerar samma
    /// <see cref="JobAdDto"/>-sida — ingen match-data i DTO:n. Anropas endast med en
    /// profil vars <see cref="FullCandidateMatchProfile.Fast"/> har minst en angiven
    /// yrkesgrupp (grindas av handlern, Decision 7).
    /// <para>
    /// <b>Count-korrekthet:</b> när <paramref name="grades"/> är icke-tom räknas
    /// total-antalet om över den GRAD-filtrerade mängden (annars överräknar den delade
    /// port-counten → spök-paginering). Tom grad-mängd ⇒ den delade port-counten är
    /// exakt rätt (och bär TD-94-bitmap-plan-hygienen).
    /// </para>
    /// <para>
    /// <b>F4-15 (ADR 0076 Decision 6) → ADR 0079 STEG 3 PR-D:</b> profilen är
    /// <see cref="FullCandidateMatchProfile"/> så match-rank-ordningen kan lägga en
    /// GYLLENE topp-rung i <c>ORDER BY</c>: en Stark match som OCKSÅ delar minst en
    /// bekräftad skill (<c>extracted_lexemes ?| CvSkillConceptIds</c>) sorteras ÖVER en
    /// ren Stark. <c>CvSkillConceptIds</c> är den BEKRÄFTADE skill-mängden
    /// (<c>MatchPreferences.PreferredSkills</c>, plaintext, ingen DEK) — SAMMA källa som
    /// verdikt-scorern, så sort och grad kan aldrig divergera på en skill.
    /// </para>
    /// </summary>
    /// <param name="grades">Det valda Fast-bandet (Grund/Bra/Stark). Tom = inget
    /// grad-filter (men porten kan ändå anropas för ren match-rank-ordning, case 3).
    /// Top förekommer aldrig — validatorn avvisar det wire-side.</param>
    /// <param name="sort">Den rena sort-axeln som används när
    /// <paramref name="orderByMatchRank"/> är <c>false</c>.</param>
    /// <param name="orderByMatchRank"><c>true</c> = ordna på match-rank (grad + gyllene
    /// rung); <c>false</c> = ordna på <paramref name="sort"/> över den grad-filtrerade
    /// mängden.</param>
    ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        JobAdSortBy sort,
        bool orderByMatchRank,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// ADR 0079 STEG 6 — räknar (utan items/sort/paginering) hur många annonser som
    /// matchar profilen i det valda Fast-bandet (<paramref name="grades"/>). Återanvänder
    /// EXAKT samma filter-SPOT (<c>JobAdSearchComposition.ApplyFilter</c>) + den delade
    /// <c>GradeRankExpression</c> som <see cref="SearchPerUserAsync"/>:s grad-WHERE — så
    /// counten är PER KONSTRUKTION lika med den länkade /jobb-sidans <c>TotalCount</c> för
    /// samma profil + grad-set (ingen siffra↔landning-divergens; ett Testcontainers-test
    /// pinnar det). DEK-fri, ingen Worker, per-användare. Tom <paramref name="grades"/> →
    /// counten över hela den filtrerade mängden (ingen grad-gallring).
    /// <para>
    /// Driver Översikts live-notis ("Det finns X jobb som matchar din profil"). Topp ingår
    /// aldrig (Fast-bandet, G3-OPT-A) — rubriken är grad-neutral, aldrig "Toppmatchningar".
    /// </para>
    /// </summary>
    ValueTask<int> CountPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        CancellationToken cancellationToken);
}
