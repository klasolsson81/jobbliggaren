using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.Grants;

/// <summary>
/// The grant store on the non-persisted Redis (ADR 0142 D3, Amendment 2026-09-19 (2)). One string per grant:
/// the key is a hash of the token, the value the DataProtector-protected subject, and the lifetime is the
/// key's TTL, set by the same command that writes it. A Redis reader is in scope (D1), and the token is the
/// whole bearer, so a key listing must not hand out live tokens.
/// </summary>
internal sealed partial class RedisGrantStore : IGrantStore
{
    /// <summary>The protector's root purpose. Each <see cref="GrantPurpose"/> protects under its own
    /// sub-purpose, so a payload issued for one purpose cannot be opened as another's.</summary>
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.Grant.v1";

    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisLoginChallengeStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<RedisGrantStore> _logger;

    public RedisGrantStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        ILogger<RedisGrantStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public Task<GrantToken> IssueAsync(GrantSubject subject, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var token = GrantToken.Generate();

            // One SET with NX and the lifetime: a fresh token never overwrites a live grant, and no grant
            // exists without a TTL, as a write followed by an EXPIRE would allow.
            var written = await db.StringSetAsync(
                Key(token),
                ProtectorFor(subject.Purpose).Protect(JsonSerializer.SerializeToUtf8Bytes(ToPayload(subject))),
                LoginChallengePolicy.GrantTtl,
                When.NotExists);

            // A token that was not written would redeem to whatever subject holds that key.
            return written
                ? token
                : throw new InvalidOperationException("A freshly minted grant token already had a record.");
        });

    public Task<GrantSubject?> RedeemAsync(GrantToken token, GrantAssertion expected, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            // GETDEL is the single use: of two redemptions, one reads the value and the other nothing.
            var payload = Open(await db.StringGetDeleteAsync(Key(token)), expected.Purpose);

            // Fail-closed: a purpose number this build does not define, or a purpose without its field, is
            // no grant.
            GrantSubject? subject = (GrantPurpose?)payload?.Purpose switch
            {
                GrantPurpose.LoginComplete when !string.IsNullOrEmpty(payload.Email) =>
                    new GrantSubject.LoginComplete(payload.Email),
                _ => null,
            };

            if (subject is null || subject.Purpose != expected.Purpose)
                return null;

            return expected.Binding is null || subject == expected.Binding ? subject : null;
        });

    // Unknown, expired and already used all arrive here as a null value. A payload that cannot be opened — a
    // lost keyring, another purpose's protector, a malformed body — reads the same way.
    private GrantPayload? Open(RedisValue stored, GrantPurpose purpose)
    {
        if (stored.IsNull)
            return null;

        try
        {
            return JsonSerializer.Deserialize<GrantPayload>(ProtectorFor(purpose).Unprotect((byte[])stored!));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

    // The purpose's NUMBER, not its name: renaming the member must not orphan live records.
    private IDataProtector ProtectorFor(GrantPurpose purpose) =>
        _protector.CreateProtector(((int)purpose).ToString(CultureInfo.InvariantCulture));

    private static GrantPayload ToPayload(GrantSubject subject) => subject switch
    {
        GrantSubject.LoginComplete complete => new GrantPayload((int)GrantPurpose.LoginComplete, complete.ProvenEmail),
        _ => throw new InvalidOperationException($"No grant payload for {subject.GetType().Name}."),
    };

    internal static string Key(GrantToken token) =>
        $"{KeyPrefix}auth/grant/v1/{Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Reveal())))}";

    [LoggerMessage(1017, LogLevel.Warning, "Grant payload unreadable ({ErrorType}) — answered as no grant")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    internal sealed record GrantPayload(
        [property: JsonPropertyName("p")] int Purpose,
        [property: JsonPropertyName("e")] string? Email);
}
