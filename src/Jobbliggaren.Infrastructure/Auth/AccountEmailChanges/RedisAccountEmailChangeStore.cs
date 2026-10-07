using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;

/// <summary>
/// Redis-backed <see cref="IAccountEmailChangeStore"/> (#1975, ADR 0153), on the volatile instance, in two key families
/// of its own. The record is keyed by the new address's fingerprint, so whoever holds the code reaches it in one script
/// and a miss costs what a hit costs. It is one hash: <c>p</c>, the protected payload (the new address, the code, the
/// user id, the current address's fingerprint and both instants); <c>a</c>, the attempt counter; <c>o</c>, the owner
/// marker; and <c>n</c>/<c>x</c>, plain copies of the two instants for the administrator's read, which never
/// unprotects. The not-before is enforced from inside <c>p</c>, by the server's clock.
/// <para>
/// The index, keyed by the user id's fingerprint, names the account's one record. It is overwritten and never deleted,
/// so a stale entry outlives a completed or cancelled change, and every operation that reaches a record through it
/// re-checks the owner marker in the same script as its read or delete.
/// </para>
/// <para>
/// Every command is one the volatile ACL grants these two families: HMSET, HGET, HINCRBY, EXISTS, EXPIRE, UNLINK and
/// scripts on the record; SET with GET and EX, and GET, on the index. Guarded writes are one-key scripts and never a
/// StackExchange.Redis <c>Condition</c>, which sends WATCH.
/// </para>
/// </summary>
internal sealed partial class RedisAccountEmailChangeStore : IAccountEmailChangeStore
{
    internal const string ProtectorPurpose = "Jobbliggaren.Auth.AccountEmailChange.v1";

    private const string KeyPrefix = "jobbliggaren:";

    // Written only while the address is free or already this owner's, with the TTL in the same script.
    private const string PutScript = """
        local owner = redis.call('HGET', KEYS[1], 'o')
        if owner and owner ~= ARGV[2] then return 0 end
        redis.call('HMSET', KEYS[1], 'p', ARGV[1], 'a', 0, 'o', ARGV[2], 'n', ARGV[3], 'x', ARGV[4])
        redis.call('EXPIRE', KEYS[1], ARGV[5])
        return 1
        """;

    // Reached through the index, so the address it names may by now hold another account's change.
    private const string OwnedDeleteScript = """
        if redis.call('HGET', KEYS[1], 'o') ~= ARGV[1] then return 0 end
        return redis.call('UNLINK', KEYS[1])
        """;

    private const string OwnedReadScript = """
        if redis.call('HGET', KEYS[1], 'o') ~= ARGV[1] then return false end
        return { redis.call('HGET', KEYS[1], 'a'), redis.call('HGET', KEYS[1], 'n'), redis.call('HGET', KEYS[1], 'x') }
        """;

    // Guarded on the payload rather than on existence: the key is the address, so a newer change to the same address
    // takes the key over between this store's read and its write.
    private const string PayloadDeleteScript = """
        if redis.call('HGET', KEYS[1], 'p') ~= ARGV[1] then return 0 end
        return redis.call('UNLINK', KEYS[1])
        """;

    private const string PayloadRefundScript = """
        if redis.call('HGET', KEYS[1], 'p') ~= ARGV[1] then return 0 end
        return redis.call('HINCRBY', KEYS[1], 'a', -1)
        """;

    private static readonly byte[] DummyFingerprint = Encoding.ASCII.GetBytes(SubjectFingerprint.Hex(string.Empty));

    private readonly VolatileRedisConnection _redis;
    private readonly IDataProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RedisAccountEmailChangeStore> _logger;
    private readonly byte[] _dummyPayload;

    public RedisAccountEmailChangeStore(
        VolatileRedisConnection redis,
        IDataProtectionProvider dataProtection,
        IDateTimeProvider clock,
        ILogger<RedisAccountEmailChangeStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _clock = clock;
        _logger = logger;
        _dummyPayload = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new ChangePayload(
            "dummy@example.invalid",
            new string('0', LoginChallengePolicy.CodeLength),
            Guid.Empty,
            SubjectFingerprint.Hex(string.Empty),
            0,
            0)));
    }

    public Task<AccountEmailChangePut> PutAsync(NewAccountEmailChange change, CancellationToken ct) =>
        _redis.ExecuteAsync<AccountEmailChangePut>(async db =>
        {
            // Millisecond precision, so the instants answered here are the ones stored.
            var now = DateTimeOffset.FromUnixTimeMilliseconds(_clock.UtcNow.ToUnixTimeMilliseconds());
            var completableFrom = now + AccountEmailChangePolicy.Delay;
            var expiresAt = now + AccountEmailChangePolicy.Ttl;
            var code = ChallengeCodeArm.Mint();
            var owner = OwnerMarker(change.UserId);
            var segment = RecordSegment(change.NewEmail);
            var recordKey = RecordKey(segment);

            var protectedPayload = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new ChangePayload(
                change.NewEmail,
                code.Reveal(),
                change.UserId,
                SubjectFingerprint.Hex(change.CurrentEmail),
                completableFrom.ToUnixTimeMilliseconds(),
                expiresAt.ToUnixTimeMilliseconds())));

            var written = (long)await db.ScriptEvaluateAsync(
                PutScript,
                [recordKey],
                [
                    protectedPayload,
                    owner,
                    completableFrom.ToUnixTimeMilliseconds(),
                    expiresAt.ToUnixTimeMilliseconds(),
                    (long)AccountEmailChangePolicy.Ttl.TotalSeconds,
                ]);
            if (written == 0)
                return AccountEmailChangePut.AddressPendingForAnotherAccount.Instance;

            // The index swap returns the record it displaced, which is removed only while it is still this owner's.
            // A same-address request keeps the key it just wrote.
            var previous = await db.StringSetAndGetAsync(IndexKey(change.UserId), segment, AccountEmailChangePolicy.Ttl);
            if (!previous.IsNull && previous != segment)
                await db.ScriptEvaluateAsync(OwnedDeleteScript, [RecordKey(previous.ToString())], [owner]);

            return new AccountEmailChangePut.Written(
                code, completableFrom, expiresAt, new RedisReceipt(recordKey, protectedPayload));
        });

    public Task RevokeAsync(AccountEmailChangeReceipt receipt, CancellationToken ct)
    {
        if (receipt is not RedisReceipt issued)
            throw new ArgumentException("The receipt was not issued by this store.", nameof(receipt));

        return _redis.ExecuteAsync(db =>
            db.ScriptEvaluateAsync(PayloadDeleteScript, [issued.RecordKey], [issued.Payload]));
    }

    public Task<AccountEmailChangeVerdict> ConsumeAsync(
        string newEmail, string currentEmail, LoginCode code, CancellationToken ct) =>
        _redis.ExecuteAsync<AccountEmailChangeVerdict>(async db =>
        {
            var presentedCode = Encoding.ASCII.GetBytes(code.Reveal());
            var presentedCurrent = Encoding.ASCII.GetBytes(SubjectFingerprint.Hex(currentEmail));
            var segment = RecordSegment(newEmail);
            var recordKey = RecordKey(segment);

            // EXISTS before HINCRBY: a bare increment on a missing key would create a hash with no TTL.
            var result = await db.ScriptEvaluateAsync(ChallengeCodeArm.ConsumeScript, [recordKey]);
            if (result.IsNull)
            {
                PayDummyWork(presentedCode, presentedCurrent);
                return AccountEmailChangeVerdict.Unusable.Instance;
            }

            var parts = (RedisResult[])result!;
            var attempt = (long)parts[0];
            if (attempt > LoginChallengePolicy.MaxAttempts)
            {
                PayDummyWork(presentedCode, presentedCurrent);
                return AccountEmailChangeVerdict.Unusable.Instance;
            }

            var protectedPayload = (byte[]?)parts[1];
            var payload = Open(protectedPayload);
            var storedCode = payload is null
                ? ChallengeCodeArm.DummyCode
                : Encoding.ASCII.GetBytes(payload.Code);
            var storedCurrent = payload is null
                ? DummyFingerprint
                : Encoding.ASCII.GetBytes(payload.CurrentFingerprint);

            // Both compared in this one counted attempt, neither short-circuiting the other.
            var codeMatches = CryptographicOperations.FixedTimeEquals(presentedCode, storedCode);
            var currentMatches = CryptographicOperations.FixedTimeEquals(presentedCurrent, storedCurrent);

            if (payload is null)
                return AccountEmailChangeVerdict.Unusable.Instance;

            if (!(codeMatches & currentMatches))
            {
                if (attempt >= LoginChallengePolicy.MaxAttempts)
                    LogChangeBurned(_logger, payload.UserId);

                return AccountEmailChangeVerdict.Unusable.Instance;
            }

            var now = _clock.UtcNow;
            var completableFrom = DateTimeOffset.FromUnixTimeMilliseconds(payload.NotBefore);
            if (now < completableFrom)
            {
                await db.ScriptEvaluateAsync(PayloadRefundScript, [recordKey], [protectedPayload]);
                return new AccountEmailChangeVerdict.NotYet(completableFrom);
            }

            // Past its usable window, or no longer the record the account's pointer names (a fault between a put's
            // index swap and its removal of the record it displaced): a refusal after a match consumes the change.
            var usable = now < DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAt)
                         && await db.StringGetAsync(IndexKey(payload.UserId)) == segment;

            var consumed = (long)await db.ScriptEvaluateAsync(PayloadDeleteScript, [recordKey], [protectedPayload]);
            if (!usable || consumed != 1)
                return AccountEmailChangeVerdict.Unusable.Instance;

            return new AccountEmailChangeVerdict.Verified(new AccountEmailChangeProof(
                payload.UserId, payload.NewEmail, new ExpectedCurrentAddress(payload.CurrentFingerprint)));
        });

    public Task<bool> CancelAsync(Guid userId, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var segment = await db.StringGetAsync(IndexKey(userId));
            if (segment.IsNull)
                return false;

            var removed = (long)await db.ScriptEvaluateAsync(
                OwnedDeleteScript, [RecordKey(segment.ToString())], [OwnerMarker(userId)]);
            return removed == 1;
        });

    public Task<PendingAccountEmailChange?> FindPendingAsync(Guid userId, CancellationToken ct) =>
        _redis.ExecuteAsync(async db =>
        {
            var segment = await db.StringGetAsync(IndexKey(userId));
            if (segment.IsNull)
                return null;

            var result = await db.ScriptEvaluateAsync(
                OwnedReadScript, [RecordKey(segment.ToString())], [OwnerMarker(userId)]);
            if (result.IsNull)
                return null;

            var fields = (RedisResult[])result!;
            var attempts = (long)fields[0];
            return new PendingAccountEmailChange(
                attempts >= LoginChallengePolicy.MaxAttempts
                    ? PendingAccountEmailChangeState.CodeBurned
                    : PendingAccountEmailChangeState.Pending,
                DateTimeOffset.FromUnixTimeMilliseconds((long)fields[1]),
                DateTimeOffset.FromUnixTimeMilliseconds((long)fields[2]));
        });

    // A missing or burned record pays what a real miss pays: one Unprotect of this store's purpose and both compares.
    private void PayDummyWork(byte[] presentedCode, byte[] presentedCurrent)
    {
        _ = Open(_dummyPayload);
        _ = CryptographicOperations.FixedTimeEquals(presentedCode, ChallengeCodeArm.DummyCode);
        _ = CryptographicOperations.FixedTimeEquals(presentedCurrent, DummyFingerprint);
    }

    // A lost keyring or a malformed body reads as an unusable change.
    private ChangePayload? Open(byte[]? protectedPayload)
    {
        if (protectedPayload is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChangePayload>(_protector.Unprotect(protectedPayload));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            LogPayloadUnreadable(_logger, ex.GetType().Name);
            return null;
        }
    }

    internal static string RecordSegment(string newEmail) => SubjectFingerprint.Hex(newEmail);

    internal static string RecordKey(string segment) => $"{KeyPrefix}auth/account-email-change/v1/{segment}";

    internal static string IndexKey(Guid userId) =>
        $"{KeyPrefix}auth/account-email-change-by-user/v1/{OwnerMarker(userId)}";

    private static string OwnerMarker(Guid userId) =>
        SubjectFingerprint.Hex(userId.ToString("D", CultureInfo.InvariantCulture));

    [LoggerMessage(1013, LogLevel.Warning,
        "Account email change payload unreadable ({ErrorType}) — answered as an unusable change")]
    private static partial void LogPayloadUnreadable(ILogger logger, string errorType);

    [LoggerMessage(1029, LogLevel.Warning,
        "Account email change burned by wrong attempts for user {TargetUserId}")]
    private static partial void LogChangeBurned(ILogger logger, Guid targetUserId);

    private sealed record RedisReceipt(string RecordKey, byte[] Payload) : AccountEmailChangeReceipt
    {
        public override string ToString() => "AccountEmailChangeReceipt(redacted)";
    }

    internal sealed record ChangePayload(
        [property: JsonPropertyName("e")] string NewEmail,
        [property: JsonPropertyName("c")] string Code,
        [property: JsonPropertyName("u")] Guid UserId,
        [property: JsonPropertyName("f")] string CurrentFingerprint,
        [property: JsonPropertyName("n")] long NotBefore,
        [property: JsonPropertyName("x")] long ExpiresAt)
    {
        public override string ToString() => "ChangePayload(redacted)";
    }
}
