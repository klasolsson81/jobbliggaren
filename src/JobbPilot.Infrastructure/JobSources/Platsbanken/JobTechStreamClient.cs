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
        // /snapshot returnerar en JSON-array över alla öppna annonser.
        // Streamen är typiskt ~50-100 MB komprimerad — vi deserialiserar via
        // Stream istället för string för att undvika OOM på worker-noden.
        using var response = await httpClient.GetAsync(
            "/snapshot",
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
        // /stream?date=ISO8601 returnerar en JSON-array av events. Polymorft
        // schema diskrimineras via <c>removed: true</c>-flaggan. ISO-8601 med
        // explicit Z för UTC (JobTech docs 2026-05-12).
        var dateParam = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var url = $"/stream?date={Uri.EscapeDataString(dateParam)}";

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
