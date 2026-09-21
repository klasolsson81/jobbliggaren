using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// Redis-backed <see cref="ILoginChallengeStore"/> (ADR 0142 D1), on the volatile instance. A challenge is
/// one hash with two fields:
/// <c>p</c>, the DataProtector-protected payload (address, code, link-secret hash), and <c>a</c>, the code
/// arm's attempt counter. A Redis reader is in scope (D1's threat model), so nothing that tells whether the
/// address has an account — no subject flag, no bare link hash — sits outside the protected payload.
/// </summary>
internal sealed partial class RedisLoginChallengeStore : ILoginChallengeStore
{
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.LoginChallenge.v1";

    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisSessionStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";
    private const string PayloadField = "p";
    private const string AttemptsField = "a";
    private const int LinkSecretLength = 16;

    // One atomic step: the existence guard keeps HINCRBY from recreating an expired or unknown record without
    // a TTL (a client chooses the id), and the increment happens BEFORE any compare, so a parallel burst of
    // guesses gets distinct attempt numbers.
    private const string ConsumeCodeScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then return false end
        local n = redis.call('HINCRBY', KEYS[1], 'a', 1)
        return { n, redis.call('HGET', KEYS[1], 'p') }
        """;

    // Stand-ins compared against when there is nothing real to compare, so every path pays one Unprotect and
    // one fixed-time compare of the same length.
    private static readonly byte[] DummyCode = Encoding.ASCII.GetBytes(new string('0', LoginChallengePolicy.CodeLength));
    private static readonly byte[] DummyLinkHash = new byte[SHA256.HashSizeInBytes];
    private static readonly int CodeSpace = (int)Math.Pow(10, LoginChallengePolicy.CodeLength);
    private static readonly string FullestCode = new('0', LoginChallengePolicy.CodeLength);
    private static readonly string FullestLinkHash = new('<', Convert.ToBase64String(DummyLinkHash).Length);

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<RedisLoginChallengeStore> _logger;
    private readonly byte[] _dummyPayload;

    public RedisLoginChallengeStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        ILogger<RedisLoginChallengeStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
        _dummyPayload = _protector.Protect(
            JsonSerializer.SerializeToUtf8Bytes(new ChallengePayload("dummy@example.invalid", null, null)));
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
            LoginCode? code = mintsCode ? MintCode() : null;
            var secret = mintsLink ? RandomNumberGenerator.GetBytes(LinkSecretLength) : null;

            var payload = new ChallengePayload(
                challenge.Recipient,
                code?.Reveal(),
                secret is null ? null : Convert.ToBase64String(SHA256.HashData(secret)));
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

            var result = await db.ScriptEvaluateAsync(ConsumeCodeScript, [recordKey]);
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

            var payload = Open((byte[]?)parts[1]);
            if (payload is null)
                return ChallengeVerdict.Missing;

            // A record without a code compares against a stand-in and can never match, so it answers Wrong,
            // Wrong, Burned exactly as a record whose code was guessed wrong (security-auditor, Q18 (A) (i)).
            var stored = payload.Code is null ? DummyCode : Encoding.ASCII.GetBytes(payload.Code);
            var matched = CryptographicOperations.FixedTimeEquals(presentedBytes, stored) && payload.Code is not null;

            if (matched)
            {
                // Single use: of two concurrent hits, or a hit racing the link or a newer mint, exactly one
                // delete returns true.
                return await db.KeyDeleteAsync(recordKey)
                    ? ChallengeVerdict.Verified(new LoginChallengeProof(payload.Recipient))
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
            if (protectedPayload is null)
            {
                PayDummyLinkCompare(secretHash);
                return null;
            }

            var payload = Open(protectedPayload);
            if (payload is null)
                return null;

            var stored = payload.LinkHash is null ? DummyLinkHash : Convert.FromBase64String(payload.LinkHash);
            var matched = CryptographicOperations.FixedTimeEquals(secretHash, stored) && payload.LinkHash is not null;
            if (!matched)
                return null;

            return await db.KeyDeleteAsync(recordKey) ? new LoginChallengeProof(payload.Recipient) : null;
        });

    // RandomNumberGenerator.GetInt32 draws uniformly over the range (the runtime rejects biased samples), so
    // no `% 1_000_000` skew exists to correct for (ADR 0142 D10).
    private static LoginCode MintCode() =>
        LoginCode.FromRaw(RandomNumberGenerator.GetInt32(0, CodeSpace)
            .ToString("D" + LoginChallengePolicy.CodeLength, CultureInfo.InvariantCulture));

    private void PayDummyCompare(byte[] presented)
    {
        _ = Open(_dummyPayload);
        _ = CryptographicOperations.FixedTimeEquals(presented, DummyCode);
    }

    private void PayDummyLinkCompare(byte[] secretHash)
    {
        _ = Open(_dummyPayload);
        _ = CryptographicOperations.FixedTimeEquals(secretHash, DummyLinkHash);
    }

    // A payload that cannot be opened — a lost keyring, or a malformed body — reads as a missing
    // record, which is the answer D1 names for a lost keyring ("degrades to expired").
    private ChallengePayload? Open(byte[]? protectedPayload)
    {
        if (protectedPayload is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChallengePayload>(_protector.Unprotect(protectedPayload));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

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
            payload with { Code = FullestCode, LinkHash = FullestLinkHash }).Length;
        var padded = new byte[ceiling];
        json.CopyTo(padded, 0);
        padded.AsSpan(json.Length).Fill((byte)' ');
        return padded;
    }

    // The id is hashed before it becomes a key, so a Redis dump shows no live challenge id (parity
    // RedisSessionStore's session keys).
    internal static string RecordSegment(ChallengeId id) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Reveal())));

    internal static string RecordKey(string segment) => $"{KeyPrefix}auth/challenge/v1/{segment}";

    internal static string IndexKey(string email) =>
        $"{KeyPrefix}auth/challenge-by-address/v1/{SubjectFingerprint.Hex(email)}";

    [LoggerMessage(1012, LogLevel.Warning,
        "Login challenge payload unreadable ({ErrorType}) — answered as a missing challenge")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    internal sealed record ChallengePayload(
        [property: JsonPropertyName("e")] string Recipient,
        [property: JsonPropertyName("c")] string? Code,
        [property: JsonPropertyName("l")] string? LinkHash);
}
