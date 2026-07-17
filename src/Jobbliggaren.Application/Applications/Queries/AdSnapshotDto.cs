using Jobbliggaren.Application.JobAds.Queries;

namespace Jobbliggaren.Application.Applications.Queries;

/// <summary>
/// The preserved ("sparad kopia") copy of a JobAd's text, captured onto the
/// application at apply-time (issue #315, ADR 0086). Detail-only — surfaced by
/// <c>GetApplicationByIdQueryHandler</c> from the materialised aggregate's
/// <c>AdSnapshot</c> owned value object. <see cref="Location"/> is the municipality
/// name resolved at read-time from the snapshot's frozen concept-id via the
/// taxonomy ACL (ADR 0086 D4 — the write side stays free of the ACL port); null
/// when absent or unresolvable. Unlike <see cref="JobAdSummaryDto"/> (live JobAd
/// metadata, no body), this carries the full <see cref="Description"/> — the FE
/// falls back to it when the live JobAd is archived. <see cref="Description"/> is
/// null once the application reached a terminal status (retention, ADR 0086 D3).
/// </summary>
/// <remarks>
/// <see cref="Contacts"/> (#842 PR4) is the apply-time frozen recruiter contact
/// block — the follow-up person for THIS application (re-bind R2: "this is the
/// purpose"). Projected through the shared <see cref="JobAdContactDto"/> (CTO
/// 2026-07-17: one crossing type, one fail-closed mapper — both surfaces project
/// the same domain VO). Never null; <c>[]</c> when the ad held none at capture or
/// once the application reached a terminal status (<c>WithoutAdBody</c> drops the
/// body AND the contacts — the follow-up purpose is spent, ADR 0086 D3 / re-bind
/// R4(b)).
/// </remarks>
public sealed record AdSnapshotDto(
    string Title,
    string Company,
    string? Location,
    string? Url,
    string Source,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string? Description,
    IReadOnlyList<JobAdContactDto> Contacts,
    DateTimeOffset CapturedAt);
