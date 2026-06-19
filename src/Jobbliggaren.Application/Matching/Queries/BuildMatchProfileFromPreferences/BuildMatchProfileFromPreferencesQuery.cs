using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.BuildMatchProfileFromPreferences;

/// <summary>
/// Builds a <see cref="CandidateMatchProfile"/> for the current user from their STATED
/// match preferences (F4-12, ADR 0076). Pure preference→profile mapping: it reads NO CV
/// content and requires NO field-encryption key (the CV's deep influence on matching
/// begins at F4-15, skills → must-have/nice-to-have). A user with no JobSeeker / no
/// preferences yields an honest EMPTY profile (empty lists → the match dimensions report
/// <c>NotAssessed</c>, never <c>NoMatch</c>), never an error.
/// </summary>
public sealed record BuildMatchProfileFromPreferencesQuery
    : IQuery<CandidateMatchProfile>, IAuthenticatedRequest;
