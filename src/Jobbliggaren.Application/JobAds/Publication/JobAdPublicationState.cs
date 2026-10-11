namespace Jobbliggaren.Application.JobAds.Publication;

/// <summary>
/// Where one job ad stands on the public surface (ADR 0160). <see cref="JobAdPublication.Classify"/>
/// is the only producer.
/// </summary>
public enum JobAdPublicationState
{
    /// <summary>Shown and listed publicly.</summary>
    Listed,

    /// <summary>Was public and is not any more: archived, or its deadline has passed.</summary>
    Ended,

    /// <summary>Erased under GDPR Art. 17 (ADR 0106 Tier B).</summary>
    Gone,

    /// <summary>Never public: not imported from an allowlisted source.</summary>
    NotPublic,
}
