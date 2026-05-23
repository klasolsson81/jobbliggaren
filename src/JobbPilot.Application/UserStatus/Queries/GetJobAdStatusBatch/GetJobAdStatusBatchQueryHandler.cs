using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.UserStatus.Queries.GetJobAdStatusBatch;

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

        // EF Core 10: List<Guid>.Contains översätts till SQL ANY på Npgsql.
        // JobAdId-strongly-typed → konvertera via .Select(id => new JobAdId(id))
        // för id-comparison. Plain Guid räcker eftersom EF mappar HasConversion.
        var jobAdIds = query.JobAdIds
            .Select(id => new JobAdId(id))
            .ToList();

        var savedIds = await db.SavedJobAds
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId && jobAdIds.Contains(s.JobAdId))
            .Select(s => s.JobAdId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var appliedIds = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId
                        && a.JobAdId != null
                        && jobAdIds.Contains(a.JobAdId.Value))
            .Select(a => a.JobAdId!.Value.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new JobAdStatusBatchDto(savedIds, appliedIds);
    }
}
