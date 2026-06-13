namespace Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;

/// <summary>
/// Resultat-disposition för <see cref="ArchiveExternalJobAdCommand"/>.
/// </summary>
public enum ArchiveOutcome
{
    /// <summary>JobAd hittad och arkiverad. <c>JobAdArchivedDomainEvent</c> raisad.</summary>
    Archived,

    /// <summary>JobAd redan i <c>Archived</c>-status (idempotent no-op).</summary>
    AlreadyArchived,

    /// <summary>Ingen JobAd matchade (Source, ExternalId) — accepteras tyst (möjlig stream-event-tapp eller manual radering).</summary>
    NotFound,
}
