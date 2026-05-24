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
    /// Strömmar fullständig snapshot av aktiva annonser. Använt av nattlig
    /// backfill (P8c) och admin-trigger. Returnerar redan sanitized RawPayload
    /// per item. <see cref="IAsyncEnumerable{T}"/> — snapshot är ~300 MB
    /// (JobTech /v2/snapshot, web-verifierat 2026-05-16); materialisering till
    /// lista OOM:ar Fas 2 single-task Fargate (root-cause-fix 2026-05-16).
    /// <para>
    /// <b>ADR 0032-amendment 2026-05-23 (retention):</b> implementationen sätter
    /// <paramref name="outcome"/> via <see cref="SnapshotOutcomeRecorder.Record"/>
    /// exakt en gång precis innan <c>yield break</c> — caller använder utfallet
    /// för att avgöra om snapshot-miss-tracking ska köra (skippas vid trunkering).
    /// </para>
    /// </summary>
    IAsyncEnumerable<JobAdImportItem> FetchSnapshotAsync(
        SnapshotOutcomeRecorder outcome,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hämtar inkrementella ändringar sedan given timestamp. Använt av Hangfire-
    /// jobb (P8c). Inkluderar både upserts och removals. RawPayload är sanitized.
    /// </summary>
    IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);

    /// <summary>
    /// Per-ID-refetch för enskilda annonser. Använt av <c>BackfillJobAdSsykJob</c>
    /// för att re-hämta rader vars raw_payload saknar fält (t.ex. pre-2026-05-20-
    /// fix-rader som saknar <c>occupation.concept_id</c> — snapshot-trunkering
    /// kommer aldrig fram till just dessa IDs eftersom JobTech <c>/v2/snapshot</c>
    /// trunkerar icke-deterministiskt vid ~10k rader). Returnerar redan sanitized
    /// <see cref="JobAdImportItem"/>; <c>null</c> betyder att annonsen är borta
    /// från källan (404).
    /// <para>
    /// <b>Semantik vid <c>null</c>:</b> callern hanterar som "skip + log + count"
    /// — INTE arkivering. Retention-disciplinen (miss-tracking) ägs av
    /// <see cref="FetchSnapshotAsync"/>-flödet (ADR 0032-amendment 2026-05-23);
    /// per-ID-fetch får inte påverka den.
    /// </para>
    /// </summary>
    Task<JobAdImportItem?> RefetchByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken);
}

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
