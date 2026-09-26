using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// The composite the <c>/me</c> session probe needs in ONE query intention (#828): the account's address
/// plus its roles. <c>Email</c> is nullable and carries the TRUE absence of an address — a present account
/// with no email is a broken #822 invariant (the address is the account's SSOT, never a claim), distinct
/// from the account row being gone (which <see cref="IUserAccountService.GetAccountSummaryAsync"/> signals
/// with a <c>null</c> summary). The "empty string, not a 401" coalescing is a handler policy and stays out
/// of this port. <c>Roles</c> is still populated when <c>Email</c> is null.
/// </summary>
public sealed record AccountSummary(string? Email, IReadOnlyList<string> Roles);

public interface IUserAccountService
{
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct);
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Reads the account's address AND roles in ONE identity round-trip (#828), for the <c>/me</c> probe
    /// that runs on every (app) render. Collapses the handler's former <see cref="GetRolesAsync"/> +
    /// <see cref="GetEmailAsync"/> pair — two <c>FindByIdAsync</c> calls that only coincidentally cost one
    /// SELECT because the second is served from the scoped identity change tracker; this method makes the
    /// single round-trip a contract, not an implementation accident. Returns <c>null</c> when no account
    /// row exists for <paramref name="userId"/>; the granular methods remain for their other single-purpose
    /// callers (claims transformation, re-auth, email-change, digest/background-match dispatch).
    /// </summary>
    Task<AccountSummary?> GetAccountSummaryAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Whether a change-email may name <paramref name="newEmail"/> (#1739): a failure when the address carries a
    /// character no stored address may hold (<c>Auth.EmailNotStorable</c>), or when some account holds it as its
    /// address OR its user name (<c>Auth.EmailTaken</c>). The user name counts because a swap that failed after its
    /// first write leaves a row whose user name is the new address and whose address is the old one, and that row
    /// holds the address against everyone but <paramref name="userId"/>, whose retry completes the swap.
    /// Authoritative uniqueness is still the swap's.
    /// </summary>
    Task<Result> CheckAddressIsFreeAsync(Guid userId, string newEmail, CancellationToken ct);

    /// <summary>
    /// Moves the account to <paramref name="newEmail"/>, whose inbox a change-email grant has proven (#1739, ADR 0142
    /// D5). The user name is written first and its refusal is fatal, because the unique index is on the user name:
    /// a taken name (the validator's refusal or the index's) is <c>Auth.EmailTaken</c>, any other refusal
    /// <c>Auth.EmailChangeIncomplete</c>. The address write follows, and its failure is fatal as
    /// <c>Auth.EmailChangeIncomplete</c>, leaving the user name moved and the address kept, a state no one else can
    /// take and a retry completes. The security stamp rotates with each write.
    /// </summary>
    Task<Result> SwapConfirmedAddressAsync(Guid userId, string newEmail, CancellationToken ct);
}
