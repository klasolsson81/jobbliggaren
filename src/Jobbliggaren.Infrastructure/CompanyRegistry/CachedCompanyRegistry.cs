using System.Text.Json;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegistry;

/// <summary>
/// #454 (ADR 0088 D6) — read-through cache decorator (GoF Decorator) over the inner
/// <see cref="ICompanyRegistry"/> via Redis <see cref="IDistributedCache"/>. Mechanics mirror
/// <see cref="Landing.RedisLandingStatsCache"/> verbatim: versioned key
/// (<c>company-registry:v1:&lt;orgnr&gt;</c>, full Redis key <c>jobbliggaren:company-registry:v1:…</c>
/// via InstanceName), <see cref="JsonSerializerDefaults.Web"/> wire shape, Redis faults
/// (non-cancellation) swallowed to a cache-miss (never a 500 on the lookup path), and
/// <see cref="JsonException"/> (schema drift) treated as a miss.
///
/// <para>
/// <b>Cache content policy (issue #454 binding + ADR 0088 D6):</b> ONLY positive
/// (<see cref="CompanyRegistryStatus.Found"/>) entries, holding ONLY org.nr→name — public
/// legal-entity data (ADR 0087 D8(a) plaintext class), never owner/person fields.
/// <c>Unavailable</c> is NEVER cached (a downed source must not look "found"/"not found" later);
/// <c>NotFound</c> is NOT cached in v1 — negative caching is deferred to the SCB-activation PR
/// where the real upstream budget (10 calls/10 s) exists to tune against.
/// </para>
///
/// <para>
/// <b>Personnummer fail-closed gate AT THE CACHE LAYER (security-auditor MUST, 2026-07-02):</b>
/// both the read and the write are gated on <c>!IsPersonnummerShaped()</c> — even though the
/// handler already refuses upstream (ADR 0088 D4), no FUTURE code path may poison Redis with a
/// personnummer-keyed entry (a raw personnummer must never appear in the Redis keyspace — keys
/// leak via SLOWLOG/MONITOR/diagnostics). Plaintext key is CORRECT for legal entities (hashing a
/// public org.nr adds nothing). Never logs the org.nr.
/// </para>
/// </summary>
internal sealed class CachedCompanyRegistry(
    ICompanyRegistry inner,
    IDistributedCache cache,
    IOptions<CompanyRegistryOptions> options) : ICompanyRegistry
{
    private const string KeyPrefix = "company-registry:v1:";

    // Wire-konsistens med minimal-API:ts JSON-default (camelCase) — parity RedisLandingStatsCache.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Cached value shape — deliberately ONLY the name (see cache content policy above).</summary>
    private sealed record CachedEntry(string Name);

    public async ValueTask<CompanyRegistryLookup> LookupAsync(
        OrganizationNumber organizationNumber, CancellationToken cancellationToken)
    {
        // Fail-closed: a personnummer-shaped value never touches the cache (read OR write) — pass
        // straight through to the inner provider (which the handler's D4 refusal normally prevents
        // from ever being reached; this bypass is the cache layer's own independent guard).
        if (organizationNumber.IsPersonnummerShaped())
            return await inner.LookupAsync(organizationNumber, cancellationToken);

        var key = KeyPrefix + organizationNumber.Value;

        var cached = await TryGetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return CompanyRegistryLookup.Found(
                new CompanyRegistryEntry(organizationNumber.Value, cached.Name));
        }

        var lookup = await inner.LookupAsync(organizationNumber, cancellationToken);

        // Positive-only (D6): Found → cache org.nr→name; NotFound/Unavailable → never cached in v1.
        if (lookup.Status == CompanyRegistryStatus.Found && lookup.Entry is not null)
            await TrySetAsync(key, new CachedEntry(lookup.Entry.Name), cancellationToken);

        return lookup;
    }

    private async Task<CachedEntry?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Redis-outage → miss (never a 500 on the lookup path); cancellation still bubbles.
            return null;
        }

        if (bytes is null || bytes.Length == 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<CachedEntry>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            // Schema drift on an un-bumped key version → treat as miss; the write below repairs it.
            return null;
        }
    }

    private async Task TrySetAsync(string key, CachedEntry entry, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(options.Value.PositiveCacheTtlDays),
        };

        try
        {
            await cache.SetAsync(key, bytes, entryOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed cache-write must not fail a successful lookup — next call re-populates.
        }
    }
}
