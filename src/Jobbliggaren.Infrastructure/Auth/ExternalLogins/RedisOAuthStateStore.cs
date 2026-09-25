using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.ExternalLogins;
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
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.OAuthState.v1";

    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisGrantStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<RedisOAuthStateStore> _logger;

    public RedisOAuthStateStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        ILogger<RedisOAuthStateStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public Task<OAuthState> PutAsync(OAuthFlow flow, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var state = OAuthState.Generate();
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new FlowPayload(flow.Provider.Value, flow.Verifier.Reveal(), flow.Next));

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
            var payload = Open(await db.StringGetDeleteAsync(Key(state)));

            if (payload is null
                || !ExternalProviderKey.TryParse(payload.Provider, out var provider)
                || provider != expected
                || string.IsNullOrEmpty(payload.Verifier))
            {
                return null;
            }

            return new OAuthFlow(provider, PkceVerifier.FromRaw(payload.Verifier), payload.Next);
        });

    // Unknown, expired and already used all arrive here as a null value; an unreadable payload reads the same way.
    private FlowPayload? Open(RedisValue stored)
    {
        if (stored.IsNull)
            return null;

        try
        {
            return JsonSerializer.Deserialize<FlowPayload>(_protector.Unprotect((byte[])stored!));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

    internal static string Key(OAuthState state) =>
        $"{KeyPrefix}auth/oauth-state/v1/{Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Reveal())))}";

    [LoggerMessage(1026, LogLevel.Warning, "OAuth state payload unreadable ({ErrorType}) — answered as no flow")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    internal sealed record FlowPayload(
        [property: JsonPropertyName("p")] string? Provider,
        [property: JsonPropertyName("v")] string? Verifier,
        [property: JsonPropertyName("n")] string? Next);
}
