using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.UnitTests.Admin.ServiceObservations;

/// <summary>A wall clock the test moves by hand.</summary>
internal sealed class MutableClock(DateTimeOffset start) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Stands in for <c>HangfireWorkerHeartbeatSource</c>, which is tested against real Hangfire storage in the Api
/// integration tests. The default reading is a storage with no server record and no heartbeat; a test sets the one
/// it needs. <see cref="Throws"/> stands for the storage driver failing (a Postgres that is down or has no schema).
/// </summary>
internal sealed class ScriptedWorkerSource : IWorkerHeartbeatSource
{
    public WorkerHeartbeatReading Reading { get; set; }

    public Exception? Throws { get; set; }

    public Action? OnRead { get; set; }

    public WorkerHeartbeatReading Read()
    {
        OnRead?.Invoke();
        return Throws is { } ex ? throw ex : Reading;
    }
}

/// <summary>
/// Stands in for <c>ReadinessVolatileStoreProbe</c>. <see cref="Hangs"/> never completes and ignores its token, the way
/// StackExchange.Redis's <c>PingAsync</c> (which takes none) does, and is the case the sampler's own deadline exists for.
/// It records the token it was handed, so a test can prove that shutdown reaches the source.
/// </summary>
internal sealed class ScriptedStoreProbe : IVolatileStoreProbe
{
    public bool Reachable { get; set; } = true;

    public Exception? Throws { get; set; }

    public bool Hangs { get; set; }

    public Action? OnRead { get; set; }

    public CancellationToken ReceivedToken { get; private set; }

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        ReceivedToken = cancellationToken;
        OnRead?.Invoke();
        if (Hangs)
        {
            return new TaskCompletionSource<bool>().Task;
        }

        return Throws is { } ex ? throw ex : Task.FromResult(Reachable);
    }
}

/// <summary>
/// Stands in for <c>SqlImportRunRecords</c>, which is tested against a real audit log written by the real auditor.
/// It records what it was asked for, so a test can prove the window and the source come from the caller.
/// </summary>
internal sealed class ScriptedImportRecords : IImportRunRecords
{
    public StreamImportRun? Latest { get; set; }

    public Exception? Throws { get; set; }

    public Action? OnRead { get; set; }

    public JobSource? AskedSource { get; private set; }

    public DateTimeOffset? AskedNotBefore { get; private set; }

    public Task<StreamImportRun?> LatestStreamRunAsync(
        JobSource source, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        AskedSource = source;
        AskedNotBefore = notBefore;
        OnRead?.Invoke();
        return Throws is { } ex ? throw ex : Task.FromResult(Latest);
    }
}
