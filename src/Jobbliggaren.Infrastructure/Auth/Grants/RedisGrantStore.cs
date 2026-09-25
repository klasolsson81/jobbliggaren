using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Validation;
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
                ProtectorFor(subject.Purpose).Protect(Padded(ToPayload(subject))),
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
            // GETDEL is the single use: of two redemptions, one reads the value and the other nothing. It runs
            // once whatever the assertion names, so trying a second purpose can never meet a consumed grant.
            var payload = Open(await db.StringGetDeleteAsync(Key(token)), expected.Purposes);

            // Fail-closed: a purpose number this build does not define, or a purpose without its fields, is
            // no grant.
            GrantSubject? subject = (GrantPurpose?)payload?.Purpose switch
            {
                GrantPurpose.LoginComplete when !string.IsNullOrEmpty(payload.Email) =>
                    new GrantSubject.LoginComplete(payload.Email),
                GrantPurpose.Reauthentication when payload.UserId is { } userId && userId != Guid.Empty =>
                    new GrantSubject.Reauthentication(userId),
                GrantPurpose.ChangeEmail when payload.UserId is { } userId && userId != Guid.Empty
                                              && !string.IsNullOrEmpty(payload.Email) =>
                    new GrantSubject.ChangeEmail(userId, payload.Email),
                GrantPurpose.LoginCompleteExternal when !string.IsNullOrEmpty(payload.Email)
                                                        && ExternalProviderKey.TryParse(payload.Provider, out var provider)
                                                        && ExternalSubject.TryCreate(payload.Subject) is { } external =>
                    new GrantSubject.LoginCompleteExternal(payload.Email, provider, external),
                _ => null,
            };

            if (subject is null || !expected.Purposes.Contains(subject.Purpose))
                return null;

            return expected.Binding is null || subject == expected.Binding ? subject : null;
        });

    // Unknown, expired and already used all arrive here as a null value. A payload that no asserted purpose's
    // protector opens — a lost keyring, another purpose's grant, a malformed body — reads the same way, and is
    // logged once, only when every protector has refused it.
    private GrantPayload? Open(RedisValue stored, IReadOnlyList<GrantPurpose> purposes)
    {
        if (stored.IsNull)
            return null;

        string? lastError = null;
        foreach (var purpose in purposes)
        {
            try
            {
                return JsonSerializer.Deserialize<GrantPayload>(ProtectorFor(purpose).Unprotect((byte[])stored!));
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                lastError = ex.GetType().Name;
            }
        }

        LogPayloadUnreadable(_logger, lastError ?? nameof(CryptographicException));
        return null;
    }

    // The purpose's NUMBER, not its name: renaming the member must not orphan live records.
    private IDataProtector ProtectorFor(GrantPurpose purpose) =>
        _protector.CreateProtector(((int)purpose).ToString(CultureInfo.InvariantCulture));

    private static GrantPayload ToPayload(GrantSubject subject) => subject switch
    {
        GrantSubject.LoginComplete complete =>
            new GrantPayload((int)GrantPurpose.LoginComplete, complete.ProvenEmail, UserId: null),
        GrantSubject.Reauthentication reauthentication =>
            new GrantPayload((int)GrantPurpose.Reauthentication, Email: null, reauthentication.UserId),
        GrantSubject.ChangeEmail changeEmail =>
            new GrantPayload((int)GrantPurpose.ChangeEmail, changeEmail.NewEmail, changeEmail.UserId),
        GrantSubject.LoginCompleteExternal external =>
            new GrantPayload((int)GrantPurpose.LoginCompleteExternal, external.ProvenEmail, UserId: null)
            {
                Provider = external.Provider.Value,
                Subject = external.Subject.Reveal(),
            },
        _ => throw new InvalidOperationException($"No grant payload for {subject.GetType().Name}."),
    };

    // Padded with trailing JSON whitespace to ONE ceiling for every purpose: the length a payload carrying every
    // field at its maximum would serialise to, with every character escaped. The purposes share one key family,
    // so an unpadded value would tell a Redis reader which purpose a grant was issued for, and how long its
    // address is (security-auditor, #1793).
    private static byte[] Padded(GrantPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var padded = new byte[PayloadCeiling];
        json.CopyTo(padded, 0);
        padded.AsSpan(json.Length).Fill((byte)' ');
        return padded;
    }

    // Every field at its bound, as characters the default encoder escapes to six bytes each: the longest address
    // a validator or VerifiedEmail admits, a user id, the longest provider key and the longest subject OIDC allows.
    // Computed once; no payload can exceed it, because each field reaching a grant passed a type that reads the
    // same bound.
    private static readonly int PayloadCeiling = JsonSerializer.SerializeToUtf8Bytes(
        new GrantPayload(
            (int)GrantPurpose.LoginCompleteExternal, new string('"', EmailAddressRules.MaximumLength), Guid.Empty)
        {
            Provider = new string('"', ExternalProviderKey.Known.Max(k => k.Value.Length)),
            Subject = new string('"', ExternalSubject.MaximumLength),
        }).Length;

    internal static string Key(GrantToken token) =>
        $"{KeyPrefix}auth/grant/v1/{Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Reveal())))}";

    [LoggerMessage(1017, LogLevel.Warning, "Grant payload unreadable ({ErrorType}) — answered as no grant")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    // The two external-login members are nullable and omitted when null, so purposes 1-3 serialise exactly as
    // they did before them: no live record changes shape, and no new key segment is owed (ADR 0142 D1).
    internal sealed record GrantPayload(
        [property: JsonPropertyName("p")] int Purpose,
        [property: JsonPropertyName("e")] string? Email,
        [property: JsonPropertyName("u")] Guid? UserId)
    {
        [JsonPropertyName("pr")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Provider { get; init; }

        [JsonPropertyName("s")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Subject { get; init; }
    }
}
