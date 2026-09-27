using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// ADR 0146 — stages a race on the real pipeline. Armed for one user, it holds that user's next
/// <c>job_seekers</c> write at SaveChanges until a second, real request has committed: the held
/// request's handler has already read the row, so the second request lands between that read and
/// the held UPDATE. Disarmed by default, so every other test's saves pass straight through; a save
/// made while one is held (the second request's own) also passes through.
/// </summary>
internal sealed class JobSeekerSaveRace : SaveChangesInterceptor
{
    private readonly Lock _gate = new();
    private Guid _userId;
    private int _remaining;
    private bool _holding;
    private int _fired;
    private Func<CancellationToken, Task>? _interleave;

    /// <summary>How many held saves have let the second request commit first.</summary>
    public int Fired
    {
        get
        {
            lock (_gate)
                return _fired;
        }
    }

    /// <summary>Holds the next <paramref name="times"/> writes of this user's row.</summary>
    public void Arm(Guid userId, int times, Func<CancellationToken, Task> interleave)
    {
        lock (_gate)
        {
            _userId = userId;
            _remaining = times;
            _holding = false;
            _fired = 0;
            _interleave = interleave;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            _remaining = 0;
            _holding = false;
            _interleave = null;
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var interleave = TryClaim(eventData.Context);
        if (interleave is null)
            return result;

        try
        {
            await interleave(cancellationToken);
        }
        finally
        {
            lock (_gate)
                _holding = false;
        }

        return result;
    }

    private Func<CancellationToken, Task>? TryClaim(DbContext? context)
    {
        if (context is null)
            return null;

        lock (_gate)
        {
            if (_interleave is null || _remaining == 0 || _holding)
                return null;

            var writesTheRow = context.ChangeTracker.Entries<JobSeeker>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted
                          && e.Entity.UserId == _userId);
            if (!writesTheRow)
                return null;

            _holding = true;
            _remaining--;
            _fired++;
            return _interleave;
        }
    }
}
