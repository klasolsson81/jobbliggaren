using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Implementation av <see cref="IJobTechStreamClient"/> via HttpClient + per-line
/// JSON-parsing. Resilience-pipelinen (retry+CB+rate-limit) appliceras på
/// HttpClient i DI (AddJobSources) — denna klass bryr sig bara om wire-format.
/// </summary>
internal sealed partial class JobTechStreamClient(
    HttpClient httpClient,
    ILogger<JobTechStreamClient> logger) : IJobTechStreamClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    // #509 — cap for the per-element malformed-skip Warnings. Whole-corpus schema
    // drift would otherwise emit ~50k Warning lines per run; the first samples give
    // the diagnostics (index + id), the summary log carries the authoritative total.
    private const int MaxLoggedMalformedElements = 10;

    public async IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // /v2/snapshot returnerar en JSON-array över alla öppna annonser
        // (~300 MB, web-verifierat 2026-05-16 mot JobTech officiell doc).
        // Enumereras per element så hela arrayen aldrig materialiseras till
        // minne — tidigare DeserializeAsync<List<>> OOM:ade Fas 2 single-task
        // Fargate (root-cause-fix 2026-05-16). Samma streaming-mönster som
        // StreamChangesAsync nedan.
        using var response = await httpClient.GetAsync(
            "/v2/snapshot",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var hit in EnumerateHitsTolerantlyAsync(stream, cancellationToken))
            yield return hit;
    }

    public async IAsyncEnumerable<JobTechHit> StreamChangesAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // /v2/stream?updated-after=YYYY-MM-DDTHH:MM:SS returnerar en JSON-array
        // av events. Polymorft schema diskrimineras via <c>removed: true</c>-
        // flaggan (web-verifierat 2026-05-13 mot JobStream 2.1.1 swagger).
        // Format YYYY-MM-DDTHH:MM:SS (utan Z) per swagger-spec — UTC implicit.
        var dateParam = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var url = $"/v2/stream?updated-after={Uri.EscapeDataString(dateParam)}";

        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var hit in EnumerateHitsTolerantlyAsync(stream, cancellationToken))
            yield return hit;
    }

    // #509 — SHARED tolerant per-element parse for both wire paths (CTO F1b: the two
    // methods were byte-for-byte the same DeserializeAsyncEnumerable<JobTechHit> loop,
    // and "en regel med två normaliserare ÄR två regler").
    //
    // The load-bearing separation of the two JsonException sources is STRUCTURAL:
    //   * DATA error (schema drift: wrong type, malformed date INSIDE one complete
    //     element) throws from element.Deserialize<JobTechHit>() INSIDE the per-element
    //     try → skip + count + capped log. Refetching cannot fix a data error — the old
    //     behaviour retried the identical poison element 3× (snapshot) or let it escape
    //     to Hangfire AutomaticRetry (stream), losing the whole tail every night.
    //   * TRANSPORT error (mid-stream truncation = incomplete JSON syntax, IOException,
    //     HttpRequestException) throws from the JsonElement enumerator's MoveNextAsync —
    //     OUTSIDE the try — and propagates to the caller unchanged, preserving
    //     PlatsbankenJobSource's enumeration-boundary truncation-retry (ADR 0032).
    // Never widen the inner catch to the enumerator boundary — that would re-merge the
    // two error classes this method exists to separate.
    //
    // DeserializeAsyncEnumerable<JsonElement> still streams per element (each element
    // becomes its own transient JsonDocument) — the 2026-05-16 OOM fix is preserved.
    // A skipped element deflates the source's ParsedTotal, so mass drift trips the
    // snapshot floor guards (absolute 30k / relative 0.80×max7d) → fail-safe.
    private async IAsyncEnumerable<JobTechHit> EnumerateHitsTolerantlyAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = -1;
        var malformedSkipped = 0;

        await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
            stream, JsonOptions, cancellationToken))
        {
            index++;

            JobTechHit? hit;
            try
            {
                hit = element.Deserialize<JobTechHit>(JsonOptions);
            }
            catch (JsonException ex)
            {
                malformedSkipped++;
                if (malformedSkipped <= MaxLoggedMalformedElements)
                    LogMalformedElementSkipped(logger, ex, index, TryReadElementId(element));
                continue;
            }

            if (hit is null)
                continue;

            yield return hit;
        }

        // Only reached on clean enumeration end (a transport throw skips it — the
        // truncation Warnings own that diagnosis, and the attempt is retried anyway).
        if (malformedSkipped > 0)
        {
            LogMalformedElementsSummary(
                logger, malformedSkipped, Math.Min(malformedSkipped, MaxLoggedMalformedElements));
        }
    }

    // Best-effort id for the skip log — the JobTech ad id is not PII and is already
    // logged by the ACL's own skip path (LogHitSkipped). Null on non-object elements.
    private static string? TryReadElementId(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    [LoggerMessage(EventId = 5011, Level = LogLevel.Warning,
        Message = "JobTech-element {ElementIndex} (id={ExternalId}) kunde inte deserialiseras — hoppas över (data-fel, inte trunkering; refetch kan inte laga det). Schema-drift mot JobTechHit-POCO:n är trolig orsak.")]
    private static partial void LogMalformedElementSkipped(
        ILogger logger, Exception exception, int elementIndex, string? externalId);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Warning,
        Message = "JobTech-ström klar med {MalformedSkippedCount} överhoppade missformade element (per-element-loggen är cap:ad till de första {LoggedCount}). Ihållande hög andel = schema-drift — uppdatera JobTechHit-POCO:n.")]
    private static partial void LogMalformedElementsSummary(
        ILogger logger, int malformedSkippedCount, int loggedCount);
}
