namespace Jobbliggaren.Application.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172) — the account-activity record: the minute an account last signed in and the minute it last made
/// an authenticated request. Best effort by contract: the implementation logs a failed write, so neither a login nor
/// a request fails on it. Its callers are <c>PasswordlessSessionGrant</c> and the Api's
/// request middleware, and nothing else (pinned by an architecture test).
/// </summary>
public interface IAccountActivityRecorder
{
    /// <summary>Records a login: both the login minute and the activity minute.</summary>
    Task RecordLoginAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Records an authenticated request, at most once per account per UTC minute in this process.</summary>
    Task RecordRequestAsync(Guid userId, CancellationToken cancellationToken);
}
