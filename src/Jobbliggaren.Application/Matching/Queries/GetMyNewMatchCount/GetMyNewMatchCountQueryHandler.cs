using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — counts the authenticated user's background matches created since their
/// last-seen watermark. Owner-scoped (reads only the current user's matches + watermark). No
/// authenticated user / no JobSeeker → honest 0. The soft-delete query filter on
/// <c>UserJobAdMatch</c> excludes erased rows automatically. NO AI/LLM.
/// <para>
/// Deliberately status-AGNOSTIC: counts a match regardless of its <c>NotificationStatus</c>
/// (Pending/Queued/Sent/Failed) — match visibility is independent of notification delivery
/// (TD-114). Do NOT add a status filter: a real (e.g. email-Failed) match must still count.
/// </para>
/// </summary>
public sealed class GetMyNewMatchCountQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyNewMatchCountQuery, MyNewMatchCountDto>
{
    public async ValueTask<MyNewMatchCountDto> Handle(
        GetMyNewMatchCountQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return MyNewMatchCountDto.Zero;

        // The last-seen watermark lives on the JobSeeker (a first-class column, ADR 0080 Beslut 6).
        // null = never opened the matches view → every match is "new".
        var lastSeen = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenMatchesAt)
            .FirstOrDefaultAsync(cancellationToken);

        var matches = db.UserJobAdMatches.Where(m => m.UserId == userId);
        if (lastSeen is { } seen)
            matches = matches.Where(m => m.CreatedAt > seen);

        var count = await matches.CountAsync(cancellationToken);
        return new MyNewMatchCountDto(count);
    }
}
