using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.Access;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// The started OAuth flows on the non-persisted Redis (#1744, ADR 0142 D1, D8). One string per flow: the key is a
/// hash of the state, the value the DataProtector-protected flow (provider, PKCE verifier, post-login path), and the
/// lifetime is the key's TTL, set by the same command that writes it. The verifier is a secret, so the value is
/// protected.
/// </summary>
internal sealed partial class RedisOAuthStateStore : IOAuthStateStore
{
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.OAuthState.v2";
    internal const string LegacyProtectorPurpose = "Jobbliggaren.Auth.OAuthState.v1";

    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisGrantStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _legacyProtector;
    private readonly ILogger<RedisOAuthStateStore> _logger;

    public RedisOAuthStateStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        ILogger<RedisOAuthStateStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _legacyProtector = dataProtection.CreateProtector(LegacyProtectorPurpose);
        _logger = logger;
    }

    public Task<OAuthState> PutAsync(OAuthFlow flow, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var state = OAuthState.Generate();
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new FlowPayload(flow.Provider.Value, flow.Verifier.Reveal(), flow.Next, SecurityProofPayload.From(flow.Access)));

            // One SET with NX and the lifetime: no flow exists without a TTL, and a fresh state never overwrites one.
            var written = await db.StringSetAsync(
                Key(state), _protector.Protect(payload), ExternalLoginPolicy.StateTtl, When.NotExists);

            return written
                ? state
                : throw new InvalidOperationException("A freshly minted OAuth state already had a record.");
        });

    public Task<OAuthFlow?> TakeAsync(OAuthState state, ExternalProviderKey expected, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            // GETDEL is the single use, and it runs before the provider is compared: a flow presented to another
            // provider's callback is spent by the attempt.
            var stored = await db.StringGetDeleteAsync(Key(state));
            var legacy = stored.IsNull;
            if (legacy)
                stored = await db.StringGetDeleteAsync(Key(state).Replace("/v2/", "/v1/", StringComparison.Ordinal));
            var payload = Open(stored, legacy);

            if (payload is null
                || !ExternalProviderKey.TryParse(payload.Provider, out var provider)
                || provider != expected
                || string.IsNullOrEmpty(payload.Verifier))
            {
                return null;
            }

            return new OAuthFlow(provider, PkceVerifier.FromRaw(payload.Verifier), payload.Next)
            {
                Access = payload.Security!.Decode()!,
            };
        });

    // Unknown, expired and already used all arrive here as a null value; an unreadable payload reads the same way.
    private FlowPayload? Open(RedisValue stored, bool legacy)
    {
        if (stored.IsNull)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<FlowPayload>((legacy ? _legacyProtector : _protector).Unprotect((byte[])stored!));
            if (legacy && payload is not null)
                return payload with { Security = SecurityProofPayload.From(AccountAccessProof.Legacy) };
            return payload?.Security?.Decode() is not null ? payload : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

    internal static string Key(OAuthState state) =>
        $"{KeyPrefix}auth/oauth-state/v2/{Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Reveal())))}";

    [LoggerMessage(1026, LogLevel.Warning, "OAuth state payload unreadable ({ErrorType}) — answered as no flow")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    internal sealed record FlowPayload(
        [property: JsonPropertyName("p")] string? Provider,
        [property: JsonPropertyName("v")] string? Verifier,
        [property: JsonPropertyName("n")] string? Next,
        [property: JsonPropertyName("g")] SecurityProofPayload? Security = null);
}
