namespace Jobbliggaren.Application.Applications.Queries;

public sealed record ApplicationDetailDto(
    Guid Id,
    Guid JobSeekerId,
    Guid? JobAdId,
    Guid? ResumeVersionId,
    string Status,
    string? CoverLetter,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FollowUpDto> FollowUps,
    IReadOnlyList<NoteDto> Notes,
    // ADR 0092 D4: the append-only status-transition timeline (from → to, when),
    // newest-first display decided FE-side. Empty for pre-timeline applications
    // (no backfill). Detail-only — NOT on the list ApplicationDto (CQRS list !=
    // detail; the list carries only the LastStatusChangeAt scalar).
    IReadOnlyList<StatusChangeDto> StatusChanges,
    JobAdSummaryDto? JobAd,
    // #315 (ADR 0086): the preserved ("sparad kopia") snapshot of the ad text,
    // captured at apply-time, surfaced from the aggregate's AdSnapshot owned VO.
    // null for manual/cover-letter-only applications (no JobAd link) and for
    // pre-#315 applications. Detail-only — NOT on the list ApplicationDto (CQRS
    // list != detail). The FE shows it as the fallback when JobAd is archived.
    AdSnapshotDto? PreservedAd);
