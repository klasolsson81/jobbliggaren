using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;

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
    /// <param name="status">#383 — den per-användar-status-predikaten (sparade/ansökta/
    /// dölj ansökta) som <c>EXISTS</c>-stackas ovanpå filter-SPOT:en FÖRE grad-WHERE/count.
    /// <see cref="JobAdStatusFilter.None"/> för match-only-vägen (no-op, byte-for-byte som
    /// förr). När den är aktiv MÅSTE counten räknas om över den status-filtrerade mängden.</param>
    /// <param name="seekerId">Den inloggade jobbsökarens id (handlern resolverar den ur
    /// <c>ICurrentUser</c>). Driver status-EXISTS:en; oanvänd när
    /// <paramref name="status"/> är inaktiv.</param>
    ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        JobAdSortBy sort,
        bool orderByMatchRank,
        JobAdStatusFilter status,
        JobSeekerId seekerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// #383 (CTO-bind <c>cto-7f3a9c2e1b4d8a6f</c>, Approach B) — den STATUS-ONLY-vägen:
    /// filtrerar listan på den per-användar-status-predikaten
    /// (<paramref name="status"/>) UTAN profil, grad-rank eller match-ordning. Frikopplad
    /// från SSYK-match-gaten (SRP): "visa mina sparade/ansökta" måste fungera även för en
    /// användare som inte angett någon yrkesgrupp. Återanvänder EXAKT samma filter-SPOT
    /// (<c>JobAdSearchComposition.ApplyFilter</c>) + rena sort
    /// (<c>JobAdSearchComposition.ApplySort</c>) som default-vägen — bara
    /// status-<c>EXISTS</c>:en läggs på. <b>Count-korrekthet:</b> total-antalet räknas
    /// över den status-filtrerade mängden (aldrig den anonyma port-counten → annars
    /// spök-paginering).
    /// <para>
    /// Anropas endast med en aktiv <paramref name="status"/> och en resolverad seeker
    /// (handlern returnerar en tom sida för anon/seeker-lös begäran innan porten nås;
    /// FE döljer kontrollen då).
    /// </para>
    /// </summary>
    ValueTask<PagedResult<JobAdDto>> SearchByStatusAsync(
        JobAdFilterCriteria filter,
        JobSeekerId seekerId,
        JobAdStatusFilter status,
        JobAdSortBy sort,
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

    /// <summary>
    /// #452 (ADR 0087 D5-tillägg) — per WATCHED EMPLOYER, count the currently-Active public ads
    /// whose grade for this user's Fast <paramref name="profile"/> falls in
    /// <paramref name="grades"/> (the hub "matchande annonser"-count). One bounded
    /// <c>GROUP BY organization_number</c> over the <paramref name="organizationNumbers"/> set,
    /// reusing the EXACT same shared <c>GradeRankExpression</c> as
    /// <see cref="SearchPerUserAsync"/> / <see cref="CountPerUserAsync"/> — so the hub count can
    /// never diverge from what /jobb shows for the same profile + grade band (sort==grade
    /// coherence, ADR 0079).
    /// <para>
    /// <b>Fast band (≥Good) is EXACT here, not an approximation:</b> CV-skills only elevate a match
    /// WITHIN the notifiable band (Good→Strong→Top) and never lift a Basic across the Good
    /// threshold, so the Fast-band ≥Good set is identical to the Full-band ≥Good set — Top's
    /// SQL-incomputability (G3-OPT-A) is irrelevant to a ≥Good COUNT. A Testcontainers oracle pins
    /// Fast ≡ Full over the verdict-tuple space.
    /// </para>
    /// <para>
    /// <b>GDPR (ADR 0087 D8):</b> org.nr is read SERVER-SIDE only as the GROUP key — never
    /// surfaced (the value returned is a plain <c>int</c> count over PUBLIC ads) nor logged. No
    /// cross-user data: the query reads only public <c>job_ads</c> and the caller's own profile.
    /// Employers with zero matches are ABSENT from the result (the caller defaults them to 0).
    /// Empty <paramref name="organizationNumbers"/> or <paramref name="grades"/> → empty dict.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, int>> CountPerUserByEmployerAsync(
        IReadOnlyList<string> organizationNumbers,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bevaknings-reconcile PR-F1 (RF-3=3D / RF-5=5A, 2026-07-12) — of
    /// <paramref name="jobAdIds"/>, returns those whose Fast grade for this user's
    /// <paramref name="profile"/> is ≥Good (the FIXED "matchande"-floor — no grade parameter,
    /// no numeric threshold). Reuses the SAME shared <c>GradeRankExpression</c> SSOT as
    /// <see cref="SearchPerUserAsync"/>/<see cref="CountPerUserAsync"/>/
    /// <see cref="CountPerUserByEmployerAsync"/> (ADR 0079) — the follow-rail's in-app surface
    /// (Api, PR-F2) and the follow digest (Worker, PR-F3) read ONE grade authority, evaluated
    /// read-time (the grade is never persisted — Goodhart). ≥Good is EXACT in Fast (Fast ≡ Full
    /// at ≥Good; Top's SQL-incomputability is irrelevant to a ≥Good membership). Soft-deleted
    /// and non-Active ads are excluded (an expired ad is not "matchande").
    /// <para>
    /// <b>Precondition (fail-fast):</b> called ONLY with an ASSESSABLE profile
    /// (<c>Fast.SsykGroupConceptIds</c> non-empty). An empty-SSYK profile grades every ad 0, so
    /// the result would be an empty set that MEANS "not assessable", NOT "zero matches" — the
    /// dishonest-0 trap. The caller MUST branch on assessability first: a profile-less user's
    /// "endast matchade" filter is INERT (deliver unfiltered + nudge, RF-5 under-fork (i)), and
    /// this method is never called for one. The method throws on an empty-SSYK profile so the
    /// distinction is forced to the call boundary.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlySet<JobAdId>> FilterToMatchingAsync(
        FullCandidateMatchProfile profile,
        IReadOnlyCollection<JobAdId> jobAdIds,
        CancellationToken cancellationToken);
}
