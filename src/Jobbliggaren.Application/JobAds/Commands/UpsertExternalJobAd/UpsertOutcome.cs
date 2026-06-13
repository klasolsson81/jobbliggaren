namespace Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;

/// <summary>
/// Resultat-disposition för <see cref="UpsertExternalJobAdCommand"/>. Räknas
/// upp av sync-orchestrators (Stream + Snapshot) till aggregerad sync-statistik.
/// </summary>
public enum UpsertOutcome
{
    /// <summary>Ny JobAd skapad via <see cref="Domain.JobAds.JobAd.Import"/>.</summary>
    Added,

    /// <summary>Existerande JobAd uppdaterad via <c>UpdateFromSource</c> (race-säker reload efter UNIQUE-collision eller direkt-träff).</summary>
    Updated,

    /// <summary>Domain-validering misslyckades (korrupt wire-data). Hela batchen får inte fallera p.g.a. en bad item — registreras i sync-statistik.</summary>
    Skipped,
}
