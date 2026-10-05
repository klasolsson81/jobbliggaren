using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth.AccountEmailChanges;

/// <summary>
/// A change an administrator starts for an account (#1975). <see cref="NewEmail"/> is the administrator's spelling,
/// the address the account moves to; <see cref="CurrentEmail"/> is the address the server read for the account, which
/// the store keeps only as a fingerprint.
/// </summary>
public sealed record NewAccountEmailChange(Guid UserId, string NewEmail, string CurrentEmail)
{
    public override string ToString() => $"NewAccountEmailChange({UserId}, addresses redacted)";
}

/// <summary>
/// The record one <see cref="IAccountEmailChangeStore.PutAsync"/> wrote, so a send that fails afterwards removes exactly
/// that record and never a newer one. Opaque: only the store that issued it reads it.
/// </summary>
public abstract record AccountEmailChangeReceipt;

/// <summary>What a put did.</summary>
public abstract record AccountEmailChangePut
{
    private AccountEmailChangePut()
    {
    }

    /// <summary>
    /// The change is stored with the code for the new address, and can be completed from
    /// <see cref="CompletableFrom"/> until <see cref="ExpiresAt"/>.
    /// </summary>
    public sealed record Written(
        LoginCode Code,
        DateTimeOffset CompletableFrom,
        DateTimeOffset ExpiresAt,
        AccountEmailChangeReceipt Receipt) : AccountEmailChangePut
    {
        public override string ToString() => $"Written(code redacted, {CompletableFrom:O}, {ExpiresAt:O})";
    }

    /// <summary>Another account's change already holds the new address; nothing was written.</summary>
    public sealed record AddressPendingForAnotherAccount : AccountEmailChangePut
    {
        public static AddressPendingForAnotherAccount Instance { get; } = new();
    }
}

/// <summary>
/// A presented change that matched in full: the account it belongs to, the address it moves to (the record's spelling,
/// never the request's), and the address it was started from, as the swap's precondition.
/// </summary>
public sealed record AccountEmailChangeProof(Guid UserId, string NewEmail, ExpectedCurrentAddress ExpectedCurrent)
{
    public override string ToString() => $"AccountEmailChangeProof({UserId}, address redacted)";
}

/// <summary>What a presented change did. Only a full match is ever told apart from <see cref="Unusable"/>.</summary>
public abstract record AccountEmailChangeVerdict
{
    private AccountEmailChangeVerdict()
    {
    }

    /// <summary>A full match, consumed: the change can be completed.</summary>
    public sealed record Verified(AccountEmailChangeProof Proof) : AccountEmailChangeVerdict;

    /// <summary>A full match before the delay has run. Nothing is consumed and no attempt is spent.</summary>
    public sealed record NotYet(DateTimeOffset CompletableFrom) : AccountEmailChangeVerdict;

    /// <summary>
    /// Everything else, as one answer: no change, an expired, cancelled, burned or completed one, a wrong code or a
    /// wrong current address.
    /// </summary>
    public sealed record Unusable : AccountEmailChangeVerdict
    {
        public static Unusable Instance { get; } = new();
    }
}

/// <summary>Whether a pending change can still be completed with its code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PendingAccountEmailChangeState>))]
public enum PendingAccountEmailChangeState
{
    Pending,
    CodeBurned,
}

/// <summary>A pending change as the administrator sees it: no address, two instants.</summary>
public sealed record PendingAccountEmailChange(
    PendingAccountEmailChangeState State,
    DateTimeOffset CompletableFrom,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The store of address changes an administrator starts (#1975, ADR 0153). It is not <see cref="ILoginChallengeStore"/>
/// because this is a third kind of record: an anonymous holder finds it by the new address, an administrator finds it
/// by the account, and it carries a second factor. It exposes the invariants, never the verbs.
/// </summary>
public interface IAccountEmailChangeStore
{
    /// <summary>
    /// Stores the change and mints its code. One change per account: a put displaces the account's earlier change. One
    /// change per new address: a put for an address another account's change holds is refused and displaces nothing.
    /// </summary>
    Task<AccountEmailChangePut> PutAsync(NewAccountEmailChange change, CancellationToken ct);

    /// <summary>Removes the record <paramref name="receipt"/> names, while it is still that record.</summary>
    Task RevokeAsync(AccountEmailChangeReceipt receipt, CancellationToken ct);

    /// <summary>
    /// Presents a change, found by the NEW address. The attempt is counted before anything is compared; the code and the
    /// current address are compared in fixed time within that one counted attempt, and the last allowed miss burns the
    /// change. A full match before the delay has run answers <see cref="AccountEmailChangeVerdict.NotYet"/> and gives
    /// the attempt back. A full match after it consumes the change, once, and only while the account's own pointer still
    /// names it.
    /// </summary>
    Task<AccountEmailChangeVerdict> ConsumeAsync(
        string newEmail, string currentEmail, LoginCode code, CancellationToken ct);

    /// <summary>Removes the account's pending change. True when one was removed.</summary>
    Task<bool> CancelAsync(Guid userId, CancellationToken ct);

    /// <summary>The account's pending change, or null when it has none.</summary>
    Task<PendingAccountEmailChange?> FindPendingAsync(Guid userId, CancellationToken ct);
}
