using System.Text.Json;
using JobbPilot.Application.Landing.Common;
using Microsoft.Extensions.Caching.Distributed;

namespace JobbPilot.Infrastructure.Landing;

/// <summary>
/// Redis-backad <see cref="ILandingStatsCache"/> via
/// <see cref="IDistributedCache"/> (ADR 0064 Variant B). JSON-serialisering
/// via <see cref="System.Text.Json"/>.
///
/// <para>
/// Nyckel: <c>landing:stats:v1</c>. <c>InstanceName</c>-prefixet
/// (<c>jobbpilot:</c>) sätts av Redis-DI-konfigurationen, så fullständig
/// nyckel i Redis blir <c>jobbpilot:landing:stats:v1</c>.
/// Versionssuffixet <c>v1</c> tillåter framtida schema-byte (ny shape på
/// <see cref="LandingStatsDto"/>) utan att läsa gamla bytes — bumpa <c>v2</c>.
/// </para>
/// <para>
/// TTL: 1 timme. Worker-jobb refreshar var 5:e min, så TTL = 12× refresh-
/// fönster ger marginal vid transient Worker-outage (deploy, restart,
/// 5-10 min:s nätverksgupp) utan att servera oändligt gammal data om
/// Worker dör permanent.
/// </para>
/// </summary>
public sealed class RedisLandingStatsCache(IDistributedCache cache) : ILandingStatsCache
{
    private const string Key = "landing:stats:v1";

    private static readonly DistributedCacheEntryOptions Options = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    };

    // Wire-konsistens med JobbPilots minimal-API JSON-default (camelCase).
    // ASP.NET Core använder JsonSerializerDefaults.Web by default, BCL-default
    // är PascalCase — utan denna ändring skulle Redis-payloaden skilja sig
    // shape-mässigt från övrig HTTP-yta (debugging via redis-cli, framtida
    // konsumenter). dotnet-architect-rekommendation 2026-05-23 (agentId
    // ae0f3583e4ed741e3, Minor 2). Round-trip-säker så länge båda sidor
    // använder samma options-instans.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LandingStatsDto?> GetAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await cache.GetAsync(Key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // security-auditor 2026-05-23 Min-3 (agentId a5d1509436995d094) —
            // Redis-outage (connection/timeout) får inte ge 500 på publik landing.
            // Avsvälja undantaget och returnera null = cache-miss; handler-fallback
            // till Floor (IsStale=true) ger civilt degraderat UX. Cancellation
            // bubblar fortfarande (request-abort respekteras).
            return null;
        }

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LandingStatsDto>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            // Schema-drift (gammal serialisering i Redis efter ouppgraderad
            // nyckel-version): behandla som cache-miss; nästa Worker-tick
            // skriver om nyckeln med aktuell shape.
            return null;
        }
    }

    public async Task SetAsync(LandingStatsDto stats, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stats, JsonOptions);
        await cache.SetAsync(Key, bytes, Options, cancellationToken).ConfigureAwait(false);
    }
}
