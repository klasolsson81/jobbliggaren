using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Implementation av <see cref="IJobTechStreamClient"/> via HttpClient + per-line
/// JSON-parsing. Resilience-pipelinen (retry+CB+rate-limit) appliceras på
/// HttpClient i DI (AddJobSources) — denna klass bryr sig bara om wire-format.
/// </summary>
internal sealed class JobTechStreamClient(HttpClient httpClient) : IJobTechStreamClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<JobTechHit>> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        // /v2/snapshot returnerar en JSON-array över alla öppna annonser
        // (web-verifierat 2026-05-13 mot JobStream 2.1.1 swagger).
        // Streamen är typiskt ~50-100 MB komprimerad — vi deserialiserar via
        // Stream istället för string för att undvika OOM på worker-noden.
        using var response = await httpClient.GetAsync(
            "/v2/snapshot",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var hits = await JsonSerializer.DeserializeAsync<List<JobTechHit>>(
            stream, JsonOptions, cancellationToken);

        return hits ?? [];
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

        // IAsyncEnumerable<T> via DeserializeAsyncEnumerable bevarar streaming-
        // semantik utan att läsa hela arrayen till minne (System.Text.Json 8+).
        await foreach (var hit in JsonSerializer.DeserializeAsyncEnumerable<JobTechHit>(
            stream, JsonOptions, cancellationToken))
        {
            if (hit is null)
                continue;

            yield return hit;
        }
    }
}
