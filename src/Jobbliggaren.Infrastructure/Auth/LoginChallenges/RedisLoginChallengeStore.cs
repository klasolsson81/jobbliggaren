using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Infrastructure.Auth.Access;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// Redis-backed <see cref="ILoginChallengeStore"/> (ADR 0142 D1), on the volatile instance. A login challenge
/// is one hash with two fields:
/// <c>p</c>, the DataProtector-protected payload (address, code, link-secret hash), and <c>a</c>, the code
/// arm's attempt counter. A Redis reader is in scope (D1's threat model), so nothing that tells whether the
/// address has an account — no subject flag, no bare link hash — sits outside the protected payload.
/// <para>
/// A bound challenge (#1739) has the same two fields in a key family of its own. Its payload holds the address,
/// the code and the user id, and is protected under a sub-purpose per <see cref="ChallengePurpose"/>, so a
/// payload written for one purpose cannot be opened as another's. Its index key carries the purpose's number
/// and a fingerprint of the user id.
/// </para>
/// </summary>
internal sealed partial class RedisLoginChallengeStore : ILoginChallengeStore
{
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.LoginChallenge.v2";
    internal const string LegacyProtectorPurpose = "Jobbliggaren.Auth.LoginChallenge.v1";

    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisSessionStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";
    private const string PayloadField = "p";
    private const string AttemptsField = "a";
    private const int LinkSecretLength = 16;

    // The link arm's stand-in, compared against when there is nothing real to compare, so every path pays one
    // Unprotect and one fixed-time compare of the same length. The code arm's is ChallengeCodeArm.DummyCode.
    private static readonly byte[] DummyLinkHash = new byte[SHA256.HashSizeInBytes];
    private static readonly string FullestCode = new('0', LoginChallengePolicy.CodeLength);
    private static readonly string FullestLinkHash = new('<', Convert.ToBase64String(DummyLinkHash).Length);

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _legacyProtector;
    private readonly ILogger<RedisLoginChallengeStore> _logger;
    private readonly byte[] _dummyPayload;

    public RedisLoginChallengeStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        ILogger<RedisLoginChallengeStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _legacyProtector = dataProtection.CreateProtector(LegacyProtectorPurpose);
        _logger = logger;
        _dummyPayload = _protector.Protect(
            Padded(new ChallengePayload("dummy@example.invalid", null, null, SecurityProofPayload.From(AccountAccessProof.Legacy))));
    }

    public Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var (mintsCode, mintsLink) = challenge.Credentials switch
            {
                ChallengeCredentials.None => (false, false),
                ChallengeCredentials.CodeOnly => (true, false),
                ChallengeCredentials.LinkOnly => (false, true),
                ChallengeCredentials.CodeAndLink => (true, true),
                var other => throw new UnreachableException($"Unmapped challenge credentials {other}."),
            };
            LoginCode? code = mintsCode ? ChallengeCodeArm.Mint() : null;
            var secret = mintsLink ? RandomNumberGenerator.GetBytes(LinkSecretLength) : null;

            var payload = new ChallengePayload(
                challenge.Recipient,
                code?.Reveal(),
                secret is null ? null : Convert.ToBase64String(SHA256.HashData(secret)),
                SecurityProofPayload.From(challenge.Access));
            var protectedPayload = _protector.Protect(Padded(payload));

            var segment = RecordSegment(challenge.Id);
            var recordKey = RecordKey(segment);

            // The record and its TTL travel together; so does the index swap, whose GET returns the record it
            // displaced. Only the last swap for an address survives any interleaving, and each Put deletes
            // exactly the predecessor it displaced.
            var transaction = db.CreateTransaction();
            var write = transaction.HashSetAsync(
                recordKey,
                [new HashEntry(PayloadField, protectedPayload), new HashEntry(AttemptsField, 0)]);
            var expire = transaction.KeyExpireAsync(recordKey, LoginChallengePolicy.ChallengeTtl);
            var displaced = challenge.ReplacesLiveChallenge
                ? transaction.StringSetAndGetAsync(IndexKey(challenge.Recipient), segment, LoginChallengePolicy.ChallengeTtl)
                : null;
            await transaction.ExecuteAsync();
            await write;
            await expire;

            if (displaced is not null)
            {
                var previous = await displaced;
                if (!previous.IsNull && previous != segment)
                    await db.KeyDeleteAsync(RecordKey(previous.ToString()));
                var legacy = await db.StringGetAsync(Legacy(IndexKey(challenge.Recipient)));
                if (!legacy.IsNull)
                    await db.KeyDeleteAsync(Legacy(RecordKey(legacy.ToString())));
            }

            var link = secret is null
                ? (LoginLinkToken?)null
                : LoginLinkToken.FromRaw(Base64Url.EncodeToString([.. IdBytes(challenge.Id), .. secret]));
            return new IssuedCredentials(code, link);
        });

    public Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var presentedBytes = Encoding.ASCII.GetBytes(presented.Reveal());
            var recordKey = RecordKey(RecordSegment(id));

            var result = await db.ScriptEvaluateAsync(ChallengeCodeArm.ConsumeScript, [recordKey]);
            var legacy = result.IsNull;
            if (legacy)
            {
                recordKey = Legacy(recordKey);
                result = await db.ScriptEvaluateAsync(ChallengeCodeArm.ConsumeScript, [recordKey]);
            }
            if (result.IsNull)
            {
                PayDummyCompare(presentedBytes);
                return ChallengeVerdict.Missing;
            }

            var parts = (RedisResult[])result!;
            var attempt = (long)parts[0];

            // The code arm is burned; the record, and with it the link, lives on to its TTL.
            if (attempt > LoginChallengePolicy.MaxAttempts)
            {
                PayDummyCompare(presentedBytes);
                return ChallengeVerdict.Burned;
            }

            var payload = Open((byte[]?)parts[1], legacy);
            if (payload is null)
                return ChallengeVerdict.Missing;

            // A record without a code compares against a stand-in and can never match, so it answers Wrong,
            // Wrong, Burned exactly as a record whose code was guessed wrong (security-auditor, Q18 (A) (i)).
            var stored = payload.Code is null ? ChallengeCodeArm.DummyCode : Encoding.ASCII.GetBytes(payload.Code);
            var matched = CryptographicOperations.FixedTimeEquals(presentedBytes, stored) && payload.Code is not null;

            if (matched)
            {
                // Single use: of two concurrent hits, or a hit racing the link or a newer mint, exactly one
                // delete returns true.
                return await db.KeyDeleteAsync(recordKey)
                    ? ChallengeVerdict.Verified(new LoginChallengeProof(payload.Recipient) { Access = payload.Security!.Decode()! })
                    : ChallengeVerdict.Missing;
            }

            return attempt >= LoginChallengePolicy.MaxAttempts
                ? ChallengeVerdict.Burned
                : ChallengeVerdict.Wrong(LoginChallengePolicy.MaxAttempts - (int)attempt);
        });

    public Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var decoded = DecodeLinkToken(token.Reveal());
            if (decoded is null)
                return null;

            var (id, secretHash) = decoded.Value;
            var recordKey = RecordKey(RecordSegment(id));

            // Read-only: the link arm never reads or writes the attempt counter, so a scanner posting links
            // cannot burn the owner's code.
            var protectedPayload = (byte[]?)await db.HashGetAsync(recordKey, PayloadField);
            var legacy = protectedPayload is null;
            if (legacy)
            {
                recordKey = Legacy(recordKey);
                protectedPayload = (byte[]?)await db.HashGetAsync(recordKey, PayloadField);
            }
            if (protectedPayload is null)
            {
                PayDummyLinkCompare(secretHash);
                return null;
            }

            var payload = Open(protectedPayload, legacy);
            if (payload is null)
                return null;

            var stored = payload.LinkHash is null ? DummyLinkHash : Convert.FromBase64String(payload.LinkHash);
            var matched = CryptographicOperations.FixedTimeEquals(secretHash, stored) && payload.LinkHash is not null;
            if (!matched)
                return null;

            return await db.KeyDeleteAsync(recordKey)
                ? new LoginChallengeProof(payload.Recipient) { Access = payload.Security!.Decode()! } : null;
        });

    public Task<LoginCode> PutBoundAsync(NewBoundChallenge challenge, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var binding = challenge.Binding;
            var code = ChallengeCodeArm.Mint();

            var original = challenge.Access == AccountAccessProof.Legacy
                ? new AccountAccessProof(0, binding.UserId, 0) : challenge.Access;
            var protectedPayload = BoundProtectorFor(binding.Purpose).Protect(PaddedBound(
                new BoundChallengePayload(challenge.Recipient, code.Reveal(), binding.UserId, SecurityProofPayload.From(original))
                { Request = challenge.EmailChangeRequest }));

            var segment = RecordSegment(challenge.Id);
            var recordKey = BoundRecordKey(segment);

            // The index swap is unconditional, and it is what bounds guessing: one live bound challenge per user
            // and purpose, so minting again never adds a second set of attempts.
            var transaction = db.CreateTransaction();
            var write = transaction.HashSetAsync(
                recordKey,
                [new HashEntry(PayloadField, protectedPayload), new HashEntry(AttemptsField, 0)]);
            var expire = transaction.KeyExpireAsync(recordKey, LoginChallengePolicy.ChallengeTtl);
            var displaced = transaction.StringSetAndGetAsync(
                BoundIndexKey(binding), segment, LoginChallengePolicy.ChallengeTtl);
            await transaction.ExecuteAsync();
            await write;
            await expire;

            var previous = await displaced;
            if (!previous.IsNull && previous != segment)
                await db.KeyDeleteAsync(BoundRecordKey(previous.ToString()));
            var legacy = await db.StringGetAsync(Legacy(BoundIndexKey(binding)));
            if (!legacy.IsNull)
                await db.KeyDeleteAsync(Legacy(BoundRecordKey(legacy.ToString())));

            return code;
        });

    public Task<LoginChallengeProof?> ReadEmailChangeRequestAsync(ChallengeId id, Guid userId, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var payload = OpenBound((byte[]?)await db.HashGetAsync(BoundRecordKey(RecordSegment(id)), PayloadField),
                ChallengePurpose.ChangeEmail);
            return payload is { Request.IsValid: true } && payload.UserId == userId
                && string.Equals(payload.Request.RequestId, id.Reveal(), StringComparison.Ordinal)
                ? new LoginChallengeProof(payload.Recipient)
                { Access = payload.Security!.Decode()!, EmailChangeRequest = payload.Request }
                : null;
        });

    public Task RevokeBoundAsync(ChallengeId id, ChallengeBinding expected, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var key = BoundRecordKey(RecordSegment(id));
            var protectedPayload = (byte[]?)await db.HashGetAsync(key, PayloadField);
            var payload = OpenBound(protectedPayload, expected.Purpose);
            if (payload is null || payload.UserId != expected.UserId)
                return false;
            await db.ScriptEvaluateAsync("""
                if redis.call('HGET', KEYS[1], 'p') ~= ARGV[1] then return 0 end
                return redis.call('UNLINK', KEYS[1])
                """, [key], [protectedPayload]);
            return true;
        });

    public Task<ChallengeVerdict> ConsumeBoundCodeAsync(
        ChallengeId id, LoginCode presented, ChallengeBinding expected, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var presentedBytes = Encoding.ASCII.GetBytes(presented.Reveal());
            var recordKey = BoundRecordKey(RecordSegment(id));

            var result = await db.ScriptEvaluateAsync(ChallengeCodeArm.ConsumeScript, [recordKey]);
            var legacy = result.IsNull;
            if (legacy)
            {
                recordKey = Legacy(recordKey);
                result = await db.ScriptEvaluateAsync(ChallengeCodeArm.ConsumeScript, [recordKey]);
            }
            if (result.IsNull)
            {
                PayDummyCompare(presentedBytes);
                return ChallengeVerdict.Missing;
            }

            var parts = (RedisResult[])result!;
            var attempt = (long)parts[0];

            // Another purpose cannot open the payload, and another user is refused here: both before the
            // attempt count is answered, so whoever does not own the record learns nothing about it.
            var payload = OpenBound((byte[]?)parts[1], expected.Purpose, legacy);
            if (payload is null || payload.UserId != expected.UserId
                || (expected.Purpose == ChallengePurpose.ChangeEmail
                    && (legacy || payload.Request?.IsValid != true
                        || !string.Equals(payload.Request.RequestId, id.Reveal(), StringComparison.Ordinal))))
            {
                _ = CryptographicOperations.FixedTimeEquals(presentedBytes, ChallengeCodeArm.DummyCode);
                return ChallengeVerdict.Missing;
            }

            var matched = CryptographicOperations.FixedTimeEquals(presentedBytes, Encoding.ASCII.GetBytes(payload.Code));
            if (attempt > LoginChallengePolicy.MaxAttempts)
                return ChallengeVerdict.Burned;

            if (matched)
            {
                return await db.KeyDeleteAsync(recordKey)
                    ? ChallengeVerdict.Verified(new LoginChallengeProof(payload.Recipient)
                    { Access = payload.Security!.Decode()!, EmailChangeRequest = payload.Request })
                    : ChallengeVerdict.Missing;
            }

            return attempt >= LoginChallengePolicy.MaxAttempts
                ? ChallengeVerdict.Burned
                : ChallengeVerdict.Wrong(LoginChallengePolicy.MaxAttempts - (int)attempt);
        });

    private void PayDummyCompare(byte[] presented)
    {
        _ = Open(_dummyPayload);
        _ = CryptographicOperations.FixedTimeEquals(presented, ChallengeCodeArm.DummyCode);
    }

    private void PayDummyLinkCompare(byte[] secretHash)
    {
        _ = Open(_dummyPayload);
        _ = CryptographicOperations.FixedTimeEquals(secretHash, DummyLinkHash);
    }

    // A payload that cannot be opened — a lost keyring, or a malformed body — reads as a missing
    // record, which is the answer D1 names for a lost keyring ("degrades to expired").
    private ChallengePayload? Open(byte[]? protectedPayload, bool legacy = false)
    {
        if (protectedPayload is null)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<ChallengePayload>(
                (legacy ? _legacyProtector : _protector).Unprotect(protectedPayload));
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

    // Another purpose's protector, a lost keyring and a malformed body all read as a missing record.
    private BoundChallengePayload? OpenBound(byte[]? protectedPayload, ChallengePurpose purpose, bool legacy = false)
    {
        if (protectedPayload is null)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<BoundChallengePayload>(
                BoundProtectorFor(purpose, legacy).Unprotect(protectedPayload));
            if (legacy && payload is not null)
                return payload with { Security = SecurityProofPayload.From(new AccountAccessProof(0, payload.UserId, 0)) };
            return payload?.Security?.Decode() is { } proof && proof.UserId == payload.UserId ? payload : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

    // The purpose's NUMBER, as RedisGrantStore.ProtectorFor has it. ChallengeBinding admits defined purposes only.
    private IDataProtector BoundProtectorFor(ChallengePurpose purpose, bool legacy = false) =>
        (legacy ? _legacyProtector : _protector).CreateProtector(((int)purpose).ToString(CultureInfo.InvariantCulture));

    private static (ChallengeId Id, byte[] SecretHash)? DecodeLinkToken(string raw)
    {
        // The OperationStatus overload, not TryDecodeFromChars: the latter's "Try" covers only the destination
        // size and throws on an invalid character, and a link token is attacker-controlled input.
        Span<byte> bytes = stackalloc byte[ChallengeId.ByteLength + LinkSecretLength + 8];
        if (Base64Url.DecodeFromChars(raw, bytes, out var consumed, out var written) != OperationStatus.Done
            || consumed != raw.Length
            || written != ChallengeId.ByteLength + LinkSecretLength)
        {
            return null;
        }

        var id = ChallengeId.FromRaw(Base64Url.EncodeToString(bytes[..ChallengeId.ByteLength]));
        return (id, SHA256.HashData(bytes[ChallengeId.ByteLength..written]));
    }

    private static byte[] IdBytes(ChallengeId id) => Base64Url.DecodeFromChars(id.Reveal());

    // Padded with trailing JSON whitespace to the length the address's fullest record would serialise to,
    // so the protected length of `p` is the same whichever credentials the record carries. The longest
    // link hash is one whose every character the default encoder escapes.
    private static byte[] Padded(ChallengePayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var ceiling = JsonSerializer.SerializeToUtf8Bytes(
            payload with { Code = FullestCode, LinkHash = FullestLinkHash, Security = SecurityProofPayload.Maximum }).Length;
        var padded = new byte[ceiling];
        json.CopyTo(padded, 0);
        padded.AsSpan(json.Length).Fill((byte)' ');
        return padded;
    }

    private static byte[] PaddedBound(BoundChallengePayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var ceiling = JsonSerializer.SerializeToUtf8Bytes(payload with
        {
            Recipient = new string('"', EmailAddressRules.MaximumLength),
            Code = FullestCode,
            Security = SecurityProofPayload.Maximum,
            Request = new EmailChangeRequestProof(new string('A', 22), DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
        }).Length;
        var padded = new byte[ceiling];
        json.CopyTo(padded, 0);
        padded.AsSpan(json.Length).Fill((byte)' ');
        return padded;
    }

    private static string Legacy(string key) => key.Replace("/v2/", "/v1/", StringComparison.Ordinal);

    // The id is hashed before it becomes a key, so a Redis dump shows no live challenge id (parity
    // RedisSessionStore's session keys).
    internal static string RecordSegment(ChallengeId id) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Reveal())));

    internal static string RecordKey(string segment) => $"{KeyPrefix}auth/challenge/v2/{segment}";

    internal static string IndexKey(string email) =>
        $"{KeyPrefix}auth/challenge-by-address/v2/{SubjectFingerprint.Hex(email)}";

    // A family of its own (ADR 0142 D1: a record-shape change costs a new segment), so the login arm cannot see a
    // bound record and a bound consume cannot see a login record.
    internal static string BoundRecordKey(string segment) => $"{KeyPrefix}auth/challenge-bound/v2/{segment}";

    internal static string BoundIndexKey(ChallengeBinding binding) =>
        $"{KeyPrefix}auth/challenge-by-user/v2/{(int)binding.Purpose}/{SubjectFingerprint.Hex(binding.UserId.ToString())}";

    [LoggerMessage(1012, LogLevel.Warning,
        "Login challenge payload unreadable ({ErrorType}) — answered as a missing challenge")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    internal sealed record ChallengePayload(
        [property: JsonPropertyName("e")] string Recipient,
        [property: JsonPropertyName("c")] string? Code,
        [property: JsonPropertyName("l")] string? LinkHash,
        [property: JsonPropertyName("g")] SecurityProofPayload? Security = null);

    // No link field: a bound challenge cannot carry one.
    internal sealed record BoundChallengePayload(
        [property: JsonPropertyName("e")] string Recipient,
        [property: JsonPropertyName("c")] string Code,
        [property: JsonPropertyName("u")] Guid UserId,
        [property: JsonPropertyName("g")] SecurityProofPayload? Security = null)
    {
        [JsonPropertyName("q")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EmailChangeRequestProof? Request { get; init; }
    }
}
