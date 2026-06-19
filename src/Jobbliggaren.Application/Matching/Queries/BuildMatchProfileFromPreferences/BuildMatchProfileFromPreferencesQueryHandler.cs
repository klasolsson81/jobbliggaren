using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Queries.BuildMatchProfileFromPreferences;

/// <summary>
/// Maps the current user's stored <c>MatchPreferences</c> onto a
/// <see cref="CandidateMatchProfile"/> (F4-12, ADR 0076). NO CV read, NO DEK / no
/// <c>IRequiresFieldEncryptionKey</c>, no PII. <c>Title</c> is always empty (the
/// preference path carries no CV title → the title-similarity dimension reports
/// <c>NotAssessed</c>). Absent user/JobSeeker → honest empty profile (never null/error).
/// Owner-scoped: only the current user's preferences are read.
/// </summary>
public sealed class BuildMatchProfileFromPreferencesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<BuildMatchProfileFromPreferencesQuery, CandidateMatchProfile>
{
    private static readonly CandidateMatchProfile Empty = new(
        Title: string.Empty,
        SsykGroupConceptIds: [],
        PreferredRegionConceptIds: [],
        PreferredEmploymentTypeConceptIds: []);

    public async ValueTask<CandidateMatchProfile> Handle(
        BuildMatchProfileFromPreferencesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Empty;

        // Load the aggregate (parity with GetMyProfileQueryHandler) rather than
        // projecting the value-converted VO directly — avoids EF translation
        // quirks with strongly-typed VOs (memory: ef_strongly_typed_vo_contains).
        var jobSeeker = await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Empty;

        var preferences = jobSeeker.MatchPreferences;
        return new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: preferences.PreferredOccupationGroups,
            PreferredRegionConceptIds: preferences.PreferredRegions,
            PreferredEmploymentTypeConceptIds: preferences.PreferredEmploymentTypes);
    }
}
