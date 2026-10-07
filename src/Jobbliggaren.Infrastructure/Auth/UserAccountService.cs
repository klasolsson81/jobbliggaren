using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Auth;

public sealed partial class UserAccountService(
    UserManager<ApplicationUser> userManager,
    ILogger<UserAccountService> logger,
    IDbExceptionInspector dbExceptionInspector,
    IAccountAccessCoordinator coordinator,
    IAccountAccessReader access)
    : IUserAccountService, ILoginAccountLookup, IPasswordlessAccountCreator
{
    /// <summary>
    /// #1737 (ADR 0142 D10) — a confirmed user with no password: <c>CreateAsync(user)</c> runs no password
    /// validator. The user name IS the address, and that is what keeps the address unique: the unique index
    /// is on the normalised user name, while <c>RequireUniqueEmail</c> is a validator that reads before it
    /// writes. <c>CreatedAt</c> is left to the database, so the orphan sweep's grace window reads one clock
    /// for every account.
    /// </summary>
    public async Task<Result<Guid>> CreatePasswordlessUserAsync(string email, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        await using var scope = await coordinator.BeginAsync([userId], false, ct);
        var created = await CreatePasswordlessUserAsync(userId, email, ct);
        if (created.IsSuccess)
            await scope.CommitAsync(ct);
        return created;
    }

    public async Task<Result<Guid>> CreatePasswordlessUserAsync(Guid userId, string email, CancellationToken ct)
    {
        if (!coordinator.Holds(userId))
            throw new InvalidOperationException("Account creation requires its protected transaction.");
        if (!StorableAddress.IsStorable(email))
            return Result.Failure<Guid>(EmailNotStorableFailure());

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        return CreatedOrFailure(await userManager.CreateAsync(user), user);
    }

    private static Result<Guid> CreatedOrFailure(IdentityResult result, ApplicationUser user)
    {
        if (!result.Succeeded)
        {
            var error = result.Errors.First();

            // #481 Low — Identity's duplicate errors carry a raw English message that echoes the
            // submitted address ("Username 'x@y.z' is already taken"); collapse them to one code that
            // names neither the field nor the address. Other Identity codes stay specific (e.g.
            // InvalidEmail).
            if (IsDuplicateAccountError(error.Code))
                return Result.Failure<Guid>(
                    DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage));

            return Result.Failure<Guid>(
                DomainError.Validation($"Auth.{error.Code}", error.Description));
        }

        return Result.Success(user.Id);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return [];

        var roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.Email;
    }

    public async Task<AccountSummary?> GetAccountSummaryAsync(Guid userId, CancellationToken ct)
    {
        // #828 — ONE FindByIdAsync for the /me probe's address + roles, instead of GetRolesAsync +
        // GetEmailAsync each resolving the same row. FindByIdAsync/GetRolesAsync take no CancellationToken
        // (Identity API limitation, same as the two granular methods above). Null row => null summary; a
        // present row with a null Email is surfaced honestly (broken #822 invariant), not coalesced here.
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        var roles = await userManager.GetRolesAsync(user);
        return new AccountSummary(user.Email, roles.ToList());
    }

    public async Task<Result> CheckAddressIsFreeAsync(Guid userId, string newEmail, CancellationToken ct)
    {
        if (!StorableAddress.IsStorable(newEmail))
            return Result.Failure(EmailNotStorableFailure());

        if (await userManager.FindByEmailAsync(newEmail) is not null)
            return Result.Failure(EmailTakenFailure());

        // The user name too: a swap that failed after its first write leaves the new address as a user name and
        // not as an address, and the unique index holds it there (security-auditor, #1790). Only for someone else:
        // on the caller's own row it is that swap, and the retry completes it.
        var nameHolder = await userManager.FindByNameAsync(newEmail);
        return nameHolder is null || nameHolder.Id == userId ? Result.Success() : Result.Failure(EmailTakenFailure());
    }

    // ILoginAccountLookup — one consumer, LoginSubjectResolver (#1735). A separate port rather than an
    // IUserAccountService member, so this service still offers no bare existence check to its callers.
    // A row with no stored address cannot be mailed and answers like no account.
    async Task<LoginAccount?> ILoginAccountLookup.FindAccountAsync(string email, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is { Email: { } accountEmail }
            ? new LoginAccount(user.Id, accountEmail, user.IsSuspended, user.AccessRevision) : null;
    }

    public async Task<Result<AddressSwapped>> SwapConfirmedAddressAsync(
        Guid userId, string newEmail, SwapPrecondition precondition, CancellationToken ct)
    {
        if (!coordinator.Holds(userId))
            throw new InvalidOperationException("An address swap requires its protected transaction.");
        var account = await access.ReadAsync(userId, ct);
        if (account is null)
            return Result.Failure<AddressSwapped>(DomainError.NotFound(
                AuthErrorCodes.UserNotFound, "Användaren hittades inte."));
        if (!account.CanAuthenticate)
            return Result.Failure<AddressSwapped>(DomainError.Gone(
                AuthErrorCodes.AccountEmailChangeStale, AuthErrorCodes.AccountEmailChangeStaleMessage));
        if (!StorableAddress.IsStorable(newEmail))
            return Result.Failure<AddressSwapped>(EmailNotStorableFailure());

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure<AddressSwapped>(
                DomainError.NotFound(AuthErrorCodes.UserNotFound, "Användaren hittades inte."));

        // #1975 — checked on the instance just loaded, whose concurrency stamp guards the user-name write below: the
        // owner's own swap or a role grant that lands after this load fails that write (Optimistic Offline Lock).
        if (precondition is SwapPrecondition.FromAdministrator fromAdministrator
            && !await StillFitsAsync(user, fromAdministrator.Expected))
        {
            return Result.Failure<AddressSwapped>(DomainError.Conflict(
                AuthErrorCodes.AccountEmailChangeStale, AuthErrorCodes.AccountEmailChangeStaleMessage));
        }

        var previousEmail = user.Email;
        if (string.Equals(previousEmail, newEmail, StringComparison.Ordinal))
            return Result.Failure<AddressSwapped>(DomainError.Conflict(
                AuthErrorCodes.EmailUnchanged, AuthErrorCodes.EmailUnchangedMessage));

        // The user name FIRST, and its refusal is fatal (#1739). The unique index is on the normalised USER
        // NAME; the e-mail index is not unique, and RequireUniqueEmail reads before it writes. Login resolves
        // an account by its e-mail, so two swaps that both wrote the address would leave one inbox opening
        // either account. Taking the name first lets the index refuse the loser with nothing written.
        IdentityResult userNameResult;
        try
        {
            userNameResult = await userManager.SetUserNameAsync(user, newEmail);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Both swaps passed the validator's read; the index refused this one's write.
            return Result.Failure<AddressSwapped>(EmailTakenFailure());
        }

        if (!userNameResult.Succeeded)
        {
            return Result.Failure<AddressSwapped>(userNameResult.Errors.Any(error => IsDuplicateAccountError(error.Code))
                ? EmailTakenFailure()
                : EmailChangeIncompleteFailure());
        }

        // ChangeEmailAsync sets Email + NormalizedEmail + EmailConfirmed=true, re-runs RequireUniqueEmail and
        // rotates the security stamp again. Its token argument proves nothing: the code proven in the new inbox
        // was the credential, and the token is minted after the user-name write rotated the stamp.
        var swapToken = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var changeResult = await userManager.ChangeEmailAsync(user, newEmail, swapToken);
        if (!changeResult.Succeeded)
        {
            LogAddressWriteRefused(userId);
            return Result.Failure<AddressSwapped>(EmailChangeIncompleteFailure());
        }

        return Result.Success(new AddressSwapped(previousEmail));
    }

    // An administrator-initiated change fits the account only while the account still holds the address the change
    // was started from, by the one normaliser every key uses, and does not hold Admin.
    private async Task<bool> StillFitsAsync(ApplicationUser user, ExpectedCurrentAddress expected) =>
        user.Email is { } email
        && string.Equals(SubjectFingerprint.Hex(email), expected.Fingerprint, StringComparison.Ordinal)
        && !await userManager.IsInRoleAsync(user, Roles.Admin);

    // Identity IdentityErrorDescriber codes (== the describer method names) for a taken username /
    // email. With UserName == Email + RequireUniqueEmail, a duplicate create trips both (#481 Low).
    private const string IdentityDuplicateUserNameCode = "DuplicateUserName";
    private const string IdentityDuplicateEmailCode = "DuplicateEmail";

    private static bool IsDuplicateAccountError(string code) =>
        code == IdentityDuplicateUserNameCode || code == IdentityDuplicateEmailCode;

    private static DomainError EmailNotStorableFailure() =>
        DomainError.Validation(AuthErrorCodes.EmailNotStorable, AuthErrorCodes.EmailNotStorableMessage);

    private static DomainError EmailTakenFailure() =>
        DomainError.Conflict(AuthErrorCodes.EmailTaken, AuthErrorCodes.EmailTakenMessage);

    private static DomainError EmailChangeIncompleteFailure() =>
        DomainError.Conflict(AuthErrorCodes.EmailChangeIncomplete, AuthErrorCodes.EmailChangeIncompleteMessage);

    [LoggerMessage(4001, LogLevel.Warning,
        "[UserAccountService] Change-email refused for user {UserId}; its transaction must roll back")]
    private partial void LogAddressWriteRefused(Guid userId);
}
