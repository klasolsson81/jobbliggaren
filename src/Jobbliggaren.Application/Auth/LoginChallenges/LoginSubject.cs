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
    Task<Guid?> FindUserIdAsync(string email, CancellationToken ct);
}

/// <summary>What an address is, for the login challenge. A closed set: only the variants nested here exist.</summary>
public abstract record LoginSubject
{
    private LoginSubject()
    {
    }

    /// <summary>No Identity account holds the address.</summary>
    public sealed record NoAccount : LoginSubject;

    /// <summary>An account with a live profile: the one kind that may be given a session.</summary>
    public sealed record Active(Guid UserId) : LoginSubject;

    /// <summary>An account in its restore window (its profile is soft-deleted).</summary>
    public sealed record PendingDeletion(Guid UserId, DateTimeOffset DeletedAt) : LoginSubject;

    /// <summary>An Identity row with no profile (#1349): treated like no account.</summary>
    public sealed record ProfileMissing(Guid UserId) : LoginSubject;
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
        if (await accounts.FindUserIdAsync(email, ct) is not { } userId)
            return new LoginSubject.NoAccount();

        var profile = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => new { js.DeletedAt })
            .FirstOrDefaultAsync(ct);

        return profile switch
        {
            null => new LoginSubject.ProfileMissing(userId),
            { DeletedAt: { } deletedAt } => new LoginSubject.PendingDeletion(userId, deletedAt),
            _ => new LoginSubject.Active(userId),
        };
    }
}
