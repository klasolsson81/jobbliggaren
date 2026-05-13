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

    public async Task<JobAdSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        LogSnapshotStarted(logger);

        var hits = await streamClient.FetchSnapshotAsync(cancellationToken);

        var items = new List<JobAdImportItem>(hits.Count);
        foreach (var hit in hits)
        {
            if (hit.Removed == true)
                continue; // Snapshot innehåller bara aktiva; defensive skip.

            var item = TryConvertToImportItem(hit);
            if (item is not null)
                items.Add(item);
        }

        LogSnapshotCompleted(logger, items.Count, hits.Count);

        return new JobAdSnapshot(items, clock.UtcNow);
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
}
