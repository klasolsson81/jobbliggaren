using JobbPilot.Domain.JobAds;

namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Application-port för externa JobAd-källor (Platsbanken, framtida LinkedIn etc.).
/// Implementationer ligger i Infrastructure och översätter wire-format till
/// <see cref="JobAdImportItem"/>-DTOs. Sanitering av PII (ADR 0032 §8-amendment)
/// sker INNE i implementationen — Application-lagret ser aldrig osanerad payload.
/// </summary>
/// <remarks>
/// ADR 0032 §2 (LSP via gemensam IJobSource) + §4 (DTO över aggregate-gränsen).
/// Aggregate-konstruktion (<see cref="JobAd.Import"/>) sker i Application-handlers,
/// inte i Infrastructure — JobAdImportItem är ett rent transport-värde.
/// </remarks>
public interface IJobSource
{
    /// <summary>Källan denna implementation hanterar (Platsbanken, etc.).</summary>
    JobSource Source { get; }

    /// <summary>
    /// Hämtar fullständig snapshot av aktiva annonser. Använt av admin-trigger
    /// (P8b) och nattlig backfill (P8c). Returnerar redan sanitized RawPayload
    /// per item.
    /// </summary>
    Task<JobAdSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Hämtar inkrementella ändringar sedan given timestamp. Använt av Hangfire-
    /// jobb (P8c). Inkluderar både upserts och removals. RawPayload är sanitized.
    /// </summary>
    IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultat av <see cref="IJobSource.FetchSnapshotAsync"/>.
/// </summary>
public sealed record JobAdSnapshot(
    IReadOnlyList<JobAdImportItem> Items,
    DateTimeOffset FetchedAt);

/// <summary>
/// Polymorft change-event från <see cref="IJobSource.StreamChangesAsync"/>.
/// Diskriminerad union via sealed records (LSP, Martin 2017 kap 9).
/// </summary>
public abstract record JobAdChange(string ExternalId, DateTimeOffset OccurredAt);

/// <summary>Annons skapad eller uppdaterad i extern källa.</summary>
public sealed record JobAdUpsert(
    string ExternalId,
    JobAdImportItem Item,
    DateTimeOffset OccurredAt)
    : JobAdChange(ExternalId, OccurredAt);

/// <summary>
/// Annons borttagen i extern källa. Hanteras via <see cref="JobAd.Archive"/>
/// (ADR 0032 §6 — soft-archive bevarar arbetsmarknad-historik).
/// </summary>
public sealed record JobAdRemoval(
    string ExternalId,
    DateTimeOffset OccurredAt)
    : JobAdChange(ExternalId, OccurredAt);

/// <summary>
/// Transport-DTO för en JobAd som ska importeras. <see cref="SanitizedRawPayload"/>
/// är redan sanerad enligt ADR 0032 §8-amendment (PII-stripping via allowlist).
/// </summary>
public sealed record JobAdImportItem(
    string ExternalId,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string SanitizedRawPayload);
