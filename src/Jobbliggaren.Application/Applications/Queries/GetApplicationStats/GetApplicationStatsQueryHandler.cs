using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetApplicationStats;

/// <summary>
/// Application-statistics read handler (issue #313, BUILD.md §6.2). Deterministic
/// projection — NO AI. Materialises a minimal owner-scoped row set then delegates
/// every metric to the pure <see cref="ApplicationStatsCalculator"/>
/// (senior-cto-advisor bind 2026-06-29, Approach B): the I/O lives here, the
/// civic-honesty math lives in the calculator (SSOT, unit-testable without EF).
///
/// Anonymous / no-seeker callers get the same all-zero DTO the calculator returns
/// for an empty set — a single code path, never a throw.
/// </summary>
public sealed class GetApplicationStatsQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    : IQueryHandler<GetApplicationStatsQuery, ApplicationStatsDto>
{
    // Safety valve (TD-8 pattern): an individual job-seeker's application count is
    // realistically tens–low-hundreds. This caps materialisation for a
    // pathological account — the rows never leave the handler (only the computed
    // DTO crosses the boundary), so this is NOT a paginated client list (§3.6).
    private const int MaxRows = 2000;

    public async ValueTask<ApplicationStatsDto> Handle(
        GetApplicationStatsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (!currentUser.UserId.HasValue)
            return ApplicationStatsCalculator.Calculate([], now);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return ApplicationStatsCalculator.Calculate([], now);

        // Minimal owner-scoped projection — current status + apply date only. No
        // JobAd join (stats need no ad metadata). Soft-delete is carried SOLELY by
        // the Application global query filter (ADR 0048 c — no manual DeletedAt
        // predicate, no IgnoreQueryFilters). OrderByDescending(CreatedAt) makes the
        // .Take deterministic (keeps the most recent rows if the valve ever trips).
        // Status carries a value converter (→ name string); materialise it then
        // read .Name in-memory so the projection translates on both Npgsql and the
        // EF InMemory provider used by the handler unit tests.
        var raw = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxRows)
            .Select(a => new { a.Status, a.AppliedAt })
            .ToListAsync(cancellationToken);

        var rows = raw
            .Select(r => new ApplicationStatRow(r.Status.Name, r.AppliedAt))
            .ToList();

        return ApplicationStatsCalculator.Calculate(rows, now);
    }
}
