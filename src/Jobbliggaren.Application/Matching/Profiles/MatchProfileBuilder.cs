using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Profiles;

/// <summary>
/// The SSOT preference→<see cref="CandidateMatchProfile"/> mapper (F4-12/F4-13,
/// ADR 0076; senior-cto-advisor 2026-06-19 Decision B = B2). Reads only the current
/// user's stored <c>MatchPreferences</c> — NO CV read, NO DEK / no
/// <c>IRequiresFieldEncryptionKey</c>, no PII. <c>Title</c> is always empty (the
/// preference path carries no CV title → the title-similarity dimension reports
/// <c>NotAssessed</c>; CV influence begins at F4-15). Absent user / JobSeeker / prefs →
/// honest empty profile (never null/error). Owner-scoped: only the current user's
/// preferences are read.
/// <para>
/// It lives in the Application layer (not Infrastructure) because it touches only
/// Application abstractions (<see cref="IAppDbContext"/> + <see cref="ICurrentUser"/>) —
/// no Npgsql/EF shadow-column secret crosses it (unlike <c>MatchScorer</c>, which IS
/// Infrastructure). This keeps the rule unit-testable without a DB and consumable by
/// both the explicit query handler and the F4-13 batch handler.
/// </para>
/// </summary>
public sealed class MatchProfileBuilder(IAppDbContext db, ICurrentUser currentUser)
    : IMatchProfileBuilder
{
    private static readonly CandidateMatchProfile Empty = new(
        Title: string.Empty,
        SsykGroupConceptIds: [],
        PreferredRegionConceptIds: [],
        PreferredEmploymentTypeConceptIds: []);

    public async ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
        CancellationToken cancellationToken)
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
