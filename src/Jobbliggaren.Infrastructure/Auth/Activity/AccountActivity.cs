using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Infrastructure.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172) — one row of <c>account_activity</c>: the minute an account last signed in and the minute it
/// last made an authenticated request. Infrastructure-internal, the ADR 0139 form: written only by
/// <see cref="SqlAccountActivityRecorder"/>'s upsert, so it is mapped for the migration schema and is never a
/// <c>DbSet</c> on <c>IAppDbContext</c>.
/// </summary>
internal sealed class AccountActivity
{
    private AccountActivity()
    {
    }

    public JobSeekerId JobSeekerId { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset LastActiveAt { get; private set; }
}
