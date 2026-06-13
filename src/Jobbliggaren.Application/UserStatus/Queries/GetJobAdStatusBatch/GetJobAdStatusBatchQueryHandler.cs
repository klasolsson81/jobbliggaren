using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch;

/// <summary>
/// ADR 0063 — handler. Två separata <c>.AsNoTracking()</c>-queries (CLAUDE.md
/// §3.6) — en mot SavedJobAds, en mot Applications. Returnerar endast distinct
/// IDs (Set-storage i FE).
///
/// <para>
/// Anonym user (utan <see cref="ICurrentUser.UserId"/>) → tom DTO. Endpoint är
/// medvetet INTE <c>.RequireAuthorization()</c>-gated per ADR 0063 §Kontext
/// + CTO-dom 2026-05-23 (agentId a5b8f9db1079a1a12 Minor 9 Variant A) — denna
/// gren är levande primär-väg, INTE defensiv backstop. Rensa ej som "död kod".
/// </para>
/// </summary>
public sealed class GetJobAdStatusBatchQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetJobAdStatusBatchQuery, JobAdStatusBatchDto>
{
    public async ValueTask<JobAdStatusBatchDto> Handle(
        GetJobAdStatusBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.JobAdIds.Count == 0)
            return new JobAdStatusBatchDto([], []);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return new JobAdStatusBatchDto([], []);

        // EF Core 10 + Npgsql: Contains() över strongly-typed-VO-projektioner ger
        // translation-runtime-fel (500 observerat i CI 2026-05-23 runs 26340816593
        // + 26341061119 — både `List<JobAdId>.Contains(s.JobAdId)` OCH post-Select
        // `Where(id => list.Contains(id))` failade). Pragmatisk fix: ladda hela
        // seekerens SavedJobAd/Application JobAdId-lista (bounded — typiskt
        // <100 rader per seeker, GetJobAdStatusBatchQueryValidator capps:ar
        // request-batchen på 100 ändå) och filtrera client-side mot
        // batch-request-set:n. Server-side projektion + HashSet O(1)-lookup.
        var requestedIds = new HashSet<Guid>(query.JobAdIds);

        var allSavedForSeeker = await db.SavedJobAds
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId)
            .Select(s => s.JobAdId.Value)
            .ToListAsync(cancellationToken);
        var savedIds = allSavedForSeeker
            .Where(id => requestedIds.Contains(id))
            .Distinct()
            .ToList();

        var allAppliedForSeeker = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId && a.JobAdId != null)
            .Select(a => a.JobAdId!.Value.Value)
            .ToListAsync(cancellationToken);
        var appliedIds = allAppliedForSeeker
            .Where(id => requestedIds.Contains(id))
            .Distinct()
            .ToList();

        return new JobAdStatusBatchDto(savedIds, appliedIds);
    }
}
