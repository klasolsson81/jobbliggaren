namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// STEG 6 Approach B (2026-05-24) — översätter fritext-sökning till
/// JobTech occupation-concept-ids (SSYK-expansion). När en användare söker
/// "systemutvecklare" expanderas det till de 9 utvecklar-concept_ids som
/// JobTech taxonomy definierar (Systemutvecklare/Programmerare,
/// Mjukvaru-/Backend-/Frontend-/Fullstack-/Devops-/Mobil-/GIS-/Administrativ
/// utvecklare). Resultatet OR-:as in i Q-grenen i
/// <c>JobAdSearchQuery.ApplyCriteria</c> ovanpå FTS + title-LIKE-fallback.
///
/// <para>
/// <b>Anti-corruption layer (Evans 2003 kap. 14):</b> översätter mellan
/// användarens fritext-domän och JobTech taxonomy-bounded-context. Implementation
/// är konfiguration (<c>appsettings.json</c>) — inte domänregel — så mappningen
/// kan utvidgas utan kod-deploy (CTO-rond `a3b55188be4e119ca` Plan C-design,
/// architect-rond 2026-05-24).
/// </para>
///
/// <para>
/// <b>Konsumeras av Infrastructure (JobAdSearchQuery)</b> via DI. Returnerar
/// tom array om söktermen inte har en synonym-mapping → query-grenen får
/// ingen SSYK-expansion (befintlig FTS + title-LIKE-väg oförändrad).
/// </para>
/// </summary>
public interface IOccupationSynonymExpander
{
    /// <summary>
    /// Expanderar en fritext-sökterm till JobTech occupation-concept_ids.
    /// Returnerar tom collection om termen inte har en mapping.
    /// </summary>
    /// <param name="q">Söktermen från användaren (case-insensitive matchning internt).</param>
    IReadOnlyCollection<string> Expand(string? q);
}
