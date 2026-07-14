using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

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
/// <see cref="Requirements"/> (F4-4b) är de strukturerade arbetsgivar-kraven
/// (must_have/nice_to_have-skills) som ACL:n parsat ur JobTech-payloaden — tom
/// lista när annonsen saknar krav eller källan inte bär dem.
///
/// <para>
/// #841 — <see cref="Facets"/> carries the seven taxonomy/employer concept ids the ACL parsed out of the
/// same payload. It travels WITH the payload because the aggregate writes the two atomically: the JSON
/// paths (and the <c>working_hours_type</c> → <c>worktime_extent</c> rename) are ACL knowledge and stay in
/// the ACL, but the VALUES are the domain's, and they must outlive the payload's 30-day TTL. Before #841
/// these seven were Postgres STORED generated columns and self-destructed with the purge.
/// </para>
///
/// <para>
/// <b>PII:</b> <c>Facets.OrganizationNumber</c> can be a sole proprietor's personnummer (ADR 0087 D8).
/// This record must never be structured-logged. <c>JobAdPublicSurfaceGuardTests</c> bans <c>{@…}</c>
/// destructuring anywhere in <c>src/</c>, and <see cref="ToString"/> below is redacted so that even a
/// plain <c>{Item}</c> placeholder cannot print the org.nr. (An earlier draft cited
/// <c>OrganizationNumberSurfacingGuardTests</c> here. False: that class token-scans an allowlist of
/// paths, and a destructured record carries none of its tokens.)
/// </para>
/// </summary>
public sealed record JobAdImportItem(
    string ExternalId,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string SanitizedRawPayload,
    JobAdFacets Facets,
    IReadOnlyList<JobAdRequirement> Requirements)
{
    /// <summary>
    /// REDACTED on purpose — see <see cref="JobAdFacets.ToString"/> for the full reasoning. A record's
    /// compiler-generated <c>ToString()</c> prints every public member, so
    /// <c>LogWarning("skipping {Item}", item)</c> — with NO <c>@</c> anywhere — would dump
    /// <c>Facets.OrganizationNumber</c> (a sole proprietor's personnummer), the whole
    /// <c>SanitizedRawPayload</c>, and the recruiter free-text in <c>Description</c> (#842) straight into
    /// the log through MEL's default formatting. That form slips past both the destructuring guard (no
    /// <c>{@</c>) and the org.nr token scan (no org.nr token in the template).
    ///
    /// <para>
    /// Only the fields that identify the item for debugging survive. Found by <c>security-auditor</c>.
    /// </para>
    /// </summary>
    public override string ToString() =>
        $"JobAdImportItem(ExternalId={ExternalId}, Title={Title}, redacted)";
}
