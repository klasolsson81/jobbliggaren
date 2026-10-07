using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #1979 — stages a race on the real submit path, the way <see cref="JobSeekerSaveRace"/> does for job_seekers.
/// Armed for one owner, it holds that owner's next feedback save at SaveChanges until a second, real request has
/// committed: the held request has already read that its key is new and whether the page's suppression exists, so the
/// second request lands between those reads and the held INSERTs, which then meet the unique index. One-shot and
/// disarmed by default, so every other save — the competitor's own and the held request's retry included — passes
/// straight through.
/// </summary>
internal sealed class FeedbackSaveRace : SaveChangesInterceptor
{
    private readonly Lock _gate = new();
    private JobSeekerId _owner;
    private Func<CancellationToken, Task>? _interleave;
    private int _fired;

    /// <summary>How many held saves have let the competing request commit first.</summary>
    public int Fired
    {
        get
        {
            lock (_gate)
                return _fired;
        }
    }

    public void Arm(JobSeekerId owner, Func<CancellationToken, Task> interleave)
    {
        lock (_gate)
        {
            _owner = owner;
            _interleave = interleave;
            _fired = 0;
        }
    }

    public void Disarm()
    {
        lock (_gate)
            _interleave = null;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var interleave = TryClaim(eventData.Context);
        if (interleave is not null)
            await interleave(cancellationToken);

        return result;
    }

    private Func<CancellationToken, Task>? TryClaim(DbContext? context)
    {
        if (context is null)
            return null;

        lock (_gate)
        {
            if (_interleave is not { } interleave)
                return null;

            var submits = context.ChangeTracker.Entries<FeedbackSubmission>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.JobSeekerId == _owner);
            if (!submits)
                return null;

            _interleave = null;
            _fired++;
            return interleave;
        }
    }
}
