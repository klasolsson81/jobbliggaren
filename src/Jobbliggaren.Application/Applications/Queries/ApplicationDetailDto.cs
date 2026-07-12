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
    // list != detail).
    //
    // #805-3 truth-sync: the FE shows this as the fallback when the source ad is
    // ARCHIVED — i.e. when JobAd.Status == "Archived", read off
    // JobAdSummaryDto.Status. It is NOT keyed on "JobAd == null": that was the
    // original (false) claim, and it made this panel unreachable in production
    // for two releases, because JobAd.DeletedAt has no writer and JobAd therefore
    // never resolves to null for a JobAd-linked application (#821).
    AdSnapshotDto? PreservedAd);
