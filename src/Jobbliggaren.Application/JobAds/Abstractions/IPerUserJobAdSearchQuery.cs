using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5) — den per-användar-match-sorten för <c>/jobb</c>
/// ("Sortera efter matchning"). En SEPARAT port från <see cref="IJobAdSearchQuery"/>:
/// den senare delas med <c>RunSavedSearch</c> och MÅSTE förbli match-ren och
/// anonymt cachebar (Decision 5) — den här porten bär per-användar-profilen och
/// är därmed inneboende icke-cachebar (korrekt: en personlig ordning).
/// <para>
/// <b>Global ordning (Klas-bind 2026-06-19):</b> grad fallande
/// (Stark→Bra→Grund→otaggade sist), tie-break <c>publishedAt</c> fallande inom
/// samma grad. Ordningen produceras över HELA den filtrerade mängden (ej bara
/// hämtad sida) — den kan därför bara beräknas i sök-queryns <c>ORDER BY</c>
/// server-side, ovanpå EXAKT samma filter-SPOT som
/// <see cref="IJobAdSearchQuery.SearchAsync"/> (<c>JobAdSearchComposition</c>).
/// </para>
/// <para>
/// <b>Goodhart (Decision 4):</b> sort-nyckeln (grad-ranken) lever ENBART i
/// <c>ORDER BY</c> — den returneras aldrig i <see cref="JobAdDto"/>, persisteras
/// aldrig, renderas aldrig. Ordningen speglar
/// <c>MatchGradeCalculator</c> (ordnings-SSOT); ett Testcontainers-orakel pinnar
/// att SQL-ranken ≡ kalkylatorn över hela verdict-tuple-rymden.
/// </para>
/// </summary>
public interface IPerUserJobAdSearchQuery
{
    /// <summary>
    /// Filtrerar (samma SPOT som <see cref="IJobAdSearchQuery.SearchAsync"/>),
    /// rangordnar efter match-grad fallande + <c>publishedAt</c> fallande, och
    /// paginerar. Returnerar samma <see cref="JobAdDto"/>-sida — ingen match-data
    /// i DTO:n. Anropas endast med en profil vars <see cref="FullCandidateMatchProfile.Fast"/>
    /// har minst en angiven yrkesgrupp (grindas av handlern, Decision 7).
    /// <para>
    /// <b>F4-15 (ADR 0076 Decision 6) → ADR 0079 STEG 3 PR-D:</b> profilen är
    /// <see cref="FullCandidateMatchProfile"/> så att en GYLLENE topp-rung kan läggas
    /// i <c>ORDER BY</c>: en Stark match (yrke+region+anställning bekräftade) som OCKSÅ
    /// delar minst en skill (<c>extracted_lexemes ?| CvSkillConceptIds</c>) sorteras
    /// ÖVER en ren Stark match. Tom <see cref="FullCandidateMatchProfile.CvSkillConceptIds"/>
    /// → ingen gyllene lyft (ordning ≡ F4-14). Sort-nyckeln (inkl. den gyllene rungen)
    /// lever ENBART i <c>ORDER BY</c> — aldrig i <see cref="JobAdDto"/> (Goodhart,
    /// Decision 4). <c>CvSkillConceptIds</c> är nu den BEKRÄFTADE skill-mängden
    /// (<c>MatchPreferences.PreferredSkills</c>, plaintext, ingen DEK; ADR 0079 Beslut 1) —
    /// SAMMA källa som verdikt-scorern läser, så sort och grad kan aldrig divergera på en
    /// borttagen/tillagd skill (ingen fel-lyft; sort==grad-koherens). Den gamla top-5-
    /// plaintext-vägen (R5-REBIND Option H) är ersatt: den bekräftade mängden är komplett
    /// per definition, så ingen under-/fel-lyft-asymmetri kvarstår.
    /// </para>
    /// </summary>
    ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        int page,
        int pageSize,
        DateTimeOffset? since,
        CancellationToken cancellationToken);
}
