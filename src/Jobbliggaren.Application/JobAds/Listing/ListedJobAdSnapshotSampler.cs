using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Listing;

/// <summary>
/// Reads the listed set once per <see cref="SampleAsync"/> call and publishes it as the newest snapshot (ADR 0174). The
/// sitemap query judges the snapshot's age against the clock when it is read; nothing is judged here.
///
/// <para>
/// <b>Lifetime: singleton,</b> composed by the Api alone (<c>ListedJobAdSnapshotCompositionTests</c>) and called from its
/// hosted service. It opens one scope per read because <see cref="IAppDbContext"/> is scoped. It is not a Mediator
/// message: a synthetic one per tick would put rows in the per-handler latency dataset.
/// </para>
///
/// <para>
/// <b>Honesty.</b> A read that throws, or that reaches the ceiling, publishes nothing, so the previous snapshot keeps its
/// own stamp and ages out at the bound. A read that finds nothing is published as a measured zero. An older read never
/// replaces a newer one. The one log line written here carries the ceiling and nothing from a row; a failure is logged
/// by the hosted service's guard.
/// </para>
/// </summary>
public sealed partial class ListedJobAdSnapshotSampler : IListedJobAdSnapshotReader, IListedJobAdSnapshotSampler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ListedJobAdSnapshotSampler> _logger;
    private readonly int _ceiling;
    private ListedJobAdSnapshot? _current;

    public ListedJobAdSnapshotSampler(
        IServiceScopeFactory scopes, IDateTimeProvider clock, ILogger<ListedJobAdSnapshotSampler> logger)
        : this(scopes, clock, logger, ListedJobAdSnapshotLimits.Ceiling)
    {
    }

    /// <summary>A lower ceiling for a test. The container only builds through the public constructor.</summary>
    internal ListedJobAdSnapshotSampler(
        IServiceScopeFactory scopes, IDateTimeProvider clock, ILogger<ListedJobAdSnapshotSampler> logger, int ceiling)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _ceiling = ceiling;
    }

    public ListedJobAdSnapshot? Current => Volatile.Read(ref _current);

    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        var asOf = _clock.UtcNow;

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var ids = await db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(asOf))
            .OrderBy(j => j.Id)
            .Take(_ceiling + 1)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count > _ceiling)
        {
            LogCeilingReached(_logger, _ceiling);
            return;
        }

        Publish(ListedJobAdSnapshot.Create(asOf, ids.Select(id => id.Value)));
    }

    private void Publish(ListedJobAdSnapshot next)
    {
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (current is not null && current.AsOf > next.AsOf)
                return;

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, current), current))
                return;
        }
    }

    [LoggerMessage(EventId = 6234, Level = LogLevel.Error,
        Message = "ListedJobAdSnapshotSampler: the listed set exceeds the ceiling of {Ceiling} ids, so nothing was published; the sitemap answers 503 once the snapshot outlives its bound.")]
    private static partial void LogCeilingReached(ILogger logger, int ceiling);
}
