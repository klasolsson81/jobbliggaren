using Jobbliggaren.Domain.Matching;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatches;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — one row in the dedicated "Mina matchningar" view: a persisted
/// <c>UserJobAdMatch</c> joined to its job ad's public details (title/company/url — public data
/// only, NO CV content). <see cref="Grade"/> is the named notifiable category (Goodhart — never a
/// number). <see cref="IsNew"/> is computed against the user's last-seen watermark (CreatedAt &gt;
/// LastSeenMatchesAt) so the view can highlight what arrived since the last visit — even though
/// opening the view advances the watermark (the flag reflects the watermark AT FETCH).
/// </summary>
public sealed record MatchListItemDto(
    Guid JobAdId,
    string Title,
    string Company,
    string Url,
    NotifiableMatchGrade Grade,
    DateTimeOffset CreatedAt,
    bool IsNew);
