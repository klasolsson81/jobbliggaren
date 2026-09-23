using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// Finds the Identity account behind an address, for <see cref="LoginSubjectResolver"/> alone. It is its own
/// port with one consumer rather than a member of <c>IUserAccountService</c>, whose rule is that it offers no
/// bare "does this account exist" (senior-cto-advisor, 2026-09-19). A bare lookup is safe only where it is
/// called: off the request path, or after the inbox is proven.
/// </summary>
public interface ILoginAccountLookup
{
    Task<LoginAccount?> FindAccountAsync(string email, CancellationToken ct);
}

/// <summary>
/// The Identity row that holds an address. <see cref="Email"/> is the spelling the ROW stores, which need not
/// be the spelling that found it: Identity's lookup normaliser folds case, and with it a few non-ASCII letters
/// (<c>ſ</c> finds <c>s</c>).
/// </summary>
public sealed record LoginAccount(Guid UserId, string Email);

/// <summary>What an address is, for the login challenge. A closed set: only the variants nested here exist.</summary>
public abstract record LoginSubject
{
    private LoginSubject()
    {
    }

    /// <summary>No Identity account holds the address.</summary>
    public sealed record NoAccount : LoginSubject;

    /// <summary>
    /// An address an Identity row holds. <see cref="AccountEmail"/> is the row's own spelling, never the
    /// submitted one.
    /// </summary>
    public abstract record KnownAccount : LoginSubject
    {
        private protected KnownAccount(Guid userId, string accountEmail) =>
            (UserId, AccountEmail) = (userId, accountEmail);

        public Guid UserId { get; init; }

        public string AccountEmail { get; init; }
    }

    /// <summary>An account with a live profile: the one kind that may be given a session.</summary>
    public sealed record Active(Guid UserId, string AccountEmail) : KnownAccount(UserId, AccountEmail);

    /// <summary>An account in its restore window (its profile is soft-deleted).</summary>
    public sealed record PendingDeletion(Guid UserId, string AccountEmail, DateTimeOffset DeletedAt)
        : KnownAccount(UserId, AccountEmail);

    /// <summary>An Identity row with no profile (#1349).</summary>
    public sealed record ProfileMissing(Guid UserId, string AccountEmail) : KnownAccount(UserId, AccountEmail);
}

/// <summary>
/// The one place that classifies an address for the login challenge: at issue time to choose the mail, and
/// after proof to choose the outcome. The profile read is the #1349 rule <c>LoginCommandHandler</c> applies:
/// a soft-deleted profile and a missing one are both refused a session.
/// </summary>
public sealed class LoginSubjectResolver(ILoginAccountLookup accounts, IAppDbContext db)
{
    public async Task<LoginSubject> ResolveAsync(string email, CancellationToken ct)
    {
        if (await accounts.FindAccountAsync(email, ct) is not { } account)
            return new LoginSubject.NoAccount();

        var profile = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(js => js.UserId == account.UserId)
            .Select(js => new { js.DeletedAt })
            .FirstOrDefaultAsync(ct);

        return profile switch
        {
            null => new LoginSubject.ProfileMissing(account.UserId, account.Email),
            { DeletedAt: { } deletedAt } => new LoginSubject.PendingDeletion(account.UserId, account.Email, deletedAt),
            _ => new LoginSubject.Active(account.UserId, account.Email),
        };
    }
}
