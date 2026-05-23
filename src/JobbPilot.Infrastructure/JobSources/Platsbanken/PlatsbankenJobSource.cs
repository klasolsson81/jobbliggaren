using System.Runtime.CompilerServices;
using System.Text.Json;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Platsbanken-implementation av <see cref="IJobSource"/>. Konsumerar
/// <see cref="IJobTechStreamClient"/> + <see cref="JobTechPayloadSanitizer"/>
/// och översätter JobTech-shape till <see cref="JobAdImportItem"/>-DTOs som
/// Application-handlers kan konsumera utan att exponeras för wire-format
/// eller osanerad PII.
/// </summary>
internal sealed partial class PlatsbankenJobSource(
    IJobTechStreamClient streamClient,
    IDateTimeProvider clock,
    ILogger<PlatsbankenJobSource> logger) : IJobSource
{
    public JobSource Source => JobSource.Platsbanken;

    // Bounded retry av snapshot-fetch vid mid-stream-trunkering. JobTechs
    // /v2/snapshot (~300 MB+ singel-GET, parameterlös, ingen resume —
    // web-verifierat 2026-05-16) termineras icke-deterministiskt mitt i
    // strömmen (Batch 0 CloudWatch-evidens: trunkering vid 21–364 MB,
    // 87–442 s). Eftersom droppen är icke-deterministisk kan ett senare
    // försök leverera mer/hela; 3 försök kapar kostnaden (~3 min vid
    // JobTech 1 req/min) — resten fylls av hybrid stream-katch-up + nästa
    // cron (CTO A2→hybrid 2026-05-16, ADR 0032-amendment). Redan-yieldade
    // items är idempotenta dubbletter via UNIQUE-index (ADR 0032 §5).
    private const int MaxSnapshotAttempts = 3;

    public async IAsyncEnumerable<JobAdImportItem> FetchSnapshotAsync(
        SnapshotOutcomeRecorder outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        LogSnapshotStarted(logger);

        var converted = 0;
        var total = 0;

        for (var attempt = 1; attempt <= MaxSnapshotAttempts; attempt++)
        {
            // Färsk GET per försök — strömmar per hit, ~300 MB materialiseras
            // aldrig (streaming-fix 2026-05-16). Konsumenten
            // (SyncPlatsbankenSnapshotJob) kör child-scope per yieldat item.
            await using var enumerator = streamClient
                .FetchSnapshotAsync(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            var truncated = false;

            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw; // Cancellation propagerar alltid — aldrig svald.
                }
                catch (Exception ex)
                    when (ex is JsonException or IOException or HttpRequestException)
                {
                    // ROTORSAKS-FIX (Batch 0 2026-05-16): mid-stream-trunkering
                    // fångas vid ENUMERATION-boundary. Detta är skilt från
                    // per-item-upsert-catchen i SyncPlatsbankenSnapshotJob —
                    // slå ALDRIG ihop dem (ADR 0032 §5-clarification: ofångad
                    // enumeration var hela storm-mekanismen, 60 starts/0
                    // completes). Ofångad här = Hangfire.AutomaticRetry-loop.
                    truncated = true;
                    if (attempt < MaxSnapshotAttempts)
                        LogSnapshotTruncatedRetrying(logger, ex, attempt, total);
                    else
                        LogSnapshotTruncatedGivingUp(logger, ex, attempt, converted, total);
                    break;
                }

                if (!moved)
                {
                    // Strömmen slut utan trunkering = fullständig snapshot.
                    // ADR 0032-amendment 2026-05-23: registrera utfall för caller
                    // INNAN yield break så miss-tracking kan köra säkert.
                    LogSnapshotCompleted(logger, converted, total);
                    outcome.Record(new SnapshotOutcome(
                        ParsedTotal: total,
                        Attempts: attempt,
                        TruncatedAndExhausted: false));
                    yield break;
                }

                var hit = enumerator.Current;
                total++;

                if (hit.Removed == true)
                    continue; // Snapshot innehåller bara aktiva; defensive skip.

                var item = TryConvertToImportItem(hit);
                if (item is null)
                    continue;

                converted++;
                yield return item;
            }

            if (truncated && attempt >= MaxSnapshotAttempts)
            {
                // Bounded retry uttömd → avsluta GRACEFULLT (ingen ofångad
                // exception → ingen retry-storm). Parsad prefix är redan
                // idempotent persisterad; hybrid stream-katch-up + nästa
                // cron fyller resten (CTO 2026-05-16).
                // ADR 0032-amendment 2026-05-23: registrera trunkerings-utfall
                // → caller skippar miss-tracking (kan inte särskilja missing
                // från trunkering).
                LogSnapshotCompleted(logger, converted, total);
                outcome.Record(new SnapshotOutcome(
                    ParsedTotal: total,
                    Attempts: attempt,
                    TruncatedAndExhausted: true));
                yield break;
            }
            // truncated && attempt < Max → ny iteration = färsk GET.
        }
    }

    public async IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var hit in streamClient.StreamChangesAsync(since, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(hit.Id))
                continue;

            var occurredAt = hit.LastPublicationDate
                ?? hit.RemovedDate
                ?? hit.PublicationDate
                ?? clock.UtcNow;

            if (hit.Removed == true)
            {
                yield return new JobAdRemoval(hit.Id, occurredAt);
                continue;
            }

            var item = TryConvertToImportItem(hit);
            if (item is null)
                continue;

            yield return new JobAdUpsert(hit.Id, item, occurredAt);
        }
    }

    private JobAdImportItem? TryConvertToImportItem(JobTechHit hit)
    {
        if (string.IsNullOrWhiteSpace(hit.Id) || hit.PublicationDate is null)
            return null;

        // SECURITY-NOTE (security-auditor 2026-05-12 Maj-1): description.text + url
        // är fri-text-fält från JobTech som kan innehålla rekryterar-PII
        // ("Skicka CV till anna@acme.se"). Vi sparar dem klartext eftersom samma
        // text är publikt indexerad på `arbetsformedlingen.se/platsbanken/annonser/{id}`
        // (legitimt intresse per GDPR Art. 6(1)(f) — annonsen är redan publicerad).
        // Sanitizer-allowlist täcker bara raw_payload-jsonb. Regex-baserad
        // PII-redaction kan lyftas som Trigger-TD vid faktiskt klagomål.
        var headline = hit.Headline?.Trim();
        var description = hit.Description?.Text?.Trim();
        // sec-Min-1: filtrera bort mailto:-länkar (application_details.url-fallback
        // kan vara `mailto:rekryterare@acme.se?subject=Job` — det är PII vi inte vill
        // persistera i job_ads.url-kolumnen).
        //
        // v2-prioritering: webpage_url är top-level i v2 (web-verifierat 2026-05-13).
        // source_links är v1-fallback om legacy JobTech återaktiveras. application_details.url
        // är sista fallback (sällan satt — kan vara mailto).
        var url = FirstNonMailtoUrl(hit.WebpageUrl, hit.SourceLinks, hit.ApplicationDetails?.Url);
        var company = hit.Employer?.Name?.Trim();
        var publishedAt = hit.PublicationDate.Value;
        var expiresAt = hit.LastPublicationDate;

        // Lite tolerant filtrering vid wire-format-luckor — JobAd.Import-faktorn
        // validerar slutligt (titel/desc/url non-empty + URL absolute).
        if (string.IsNullOrWhiteSpace(headline)
            || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(company))
        {
            LogHitSkipped(logger, hit.Id);
            return null;
        }

        // Serialisera hit till JSON och kör sanitizer. Sanitizer-allowlist
        // garanterar att raw_payload inte innehåller rekryterar-PII (TD-73 +
        // ADR 0032 §8-amendment).
        var rawJson = JsonSerializer.Serialize(hit);
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        return new JobAdImportItem(
            ExternalId: hit.Id,
            Title: headline,
            CompanyName: company,
            Description: description,
            Url: url,
            PublishedAt: publishedAt,
            ExpiresAt: expiresAt,
            SanitizedRawPayload: sanitized);
    }

    private static string? FirstNonMailtoUrl(
        string? webpageUrl,
        IReadOnlyList<JobTechSourceLink>? sourceLinks,
        string? applicationDetailsUrl)
    {
        // v2-prioritet: webpage_url först.
        if (IsValidNonMailto(webpageUrl))
            return webpageUrl;

        // v1-fallback: source_links[0].url.
        if (sourceLinks is not null)
        {
            foreach (var link in sourceLinks)
            {
                if (IsValidNonMailto(link.Url))
                    return link.Url;
            }
        }

        // Sista fallback: application_details.url (kan vara mailto i prod-data).
        if (IsValidNonMailto(applicationDetailsUrl))
            return applicationDetailsUrl;

        return null;
    }

    private static bool IsValidNonMailto(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Platsbanken snapshot fetch startad.")]
    private static partial void LogSnapshotStarted(ILogger logger);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Platsbanken snapshot fetch klar — {ConvertedCount}/{TotalCount} items konverterade.")]
    private static partial void LogSnapshotCompleted(ILogger logger, int convertedCount, int totalCount);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug,
        Message = "Platsbanken hit {ExternalId} hoppas över — saknar obligatoriska fält.")]
    private static partial void LogHitSkipped(ILogger logger, string externalId);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning,
        Message = "Platsbanken snapshot trunkerad mid-stream (attempt={Attempt}, parsade={Total} före brott) — gör om hämtning. Redan-yieldade items är idempotenta via UNIQUE-index.")]
    private static partial void LogSnapshotTruncatedRetrying(
        ILogger logger, Exception exception, int attempt, int total);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning,
        Message = "Platsbanken snapshot trunkerad mid-stream — bounded retry uttömd efter {Attempt} försök ({ConvertedCount}/{TotalCount} konverterade). Avslutar gracefully; hybrid stream-katch-up + nästa cron fyller resten (ADR 0032-amendment 2026-05-16). Ingen Hangfire-retry-storm.")]
    private static partial void LogSnapshotTruncatedGivingUp(
        ILogger logger, Exception exception, int attempt, int convertedCount, int totalCount);
}
