using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.BuildMatchProfileFromPreferences;

/// <summary>
/// Maps the current user's stored <c>MatchPreferences</c> onto a
/// <see cref="CandidateMatchProfile"/> (F4-12, ADR 0076). A thin delegation to
/// <see cref="IMatchProfileBuilder"/> — the SSOT for the preference→profile rule,
/// shared with the F4-13 page-scoped match-tag batch handler (senior-cto-advisor
/// 2026-06-19 Decision B = B2; DRY without a handler-invokes-handler anti-pattern).
/// NO CV read, NO DEK, no PII; absent user/JobSeeker → honest empty profile.
/// Owner-scoping + the empty-profile fallback live in the collaborator.
/// </summary>
public sealed class BuildMatchProfileFromPreferencesQueryHandler(IMatchProfileBuilder builder)
    : IQueryHandler<BuildMatchProfileFromPreferencesQuery, CandidateMatchProfile>
{
    public ValueTask<CandidateMatchProfile> Handle(
        BuildMatchProfileFromPreferencesQuery query, CancellationToken cancellationToken)
        => builder.BuildFromPreferencesAsync(cancellationToken);
}
