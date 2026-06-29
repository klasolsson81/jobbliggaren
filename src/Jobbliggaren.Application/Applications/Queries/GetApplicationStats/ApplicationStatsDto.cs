namespace Jobbliggaren.Application.Applications.Queries.GetApplicationStats;

/// <summary>
/// Application statistics read model (issue #313, BUILD.md §6.2 — "avslags-analys,
/// pipeline-konvertering"). Deterministic projection — NO AI. Per-user, computed
/// in <see cref="ApplicationStatsCalculator"/> from a bounded materialised
/// projection (senior-cto-advisor bind 2026-06-29, Approach B).
///
/// Civic-honesty contract (CLAUDE.md §5 — a number is never opaque): every rate
/// ships as a (numerator, denominator, percent) triple so the FE labels it
/// truthfully and cannot show a percentage without its base.
///
/// Two distinct denominators, deliberately:
/// <list type="bullet">
/// <item><see cref="TotalApplications"/> counts EVERY application (drafts
/// included) — the user's raw activity.</item>
/// <item><see cref="TotalSent"/> = applications ever submitted
/// (<c>AppliedAt != null</c>) — the canonical denominator for ALL rates and the
/// funnel. A never-sent draft cannot have been responded to or rejected, so it is
/// excluded from every rate.</item>
/// </list>
/// </summary>
public sealed record ApplicationStatsDto(
    int TotalApplications,
    int TotalSent,
    IReadOnlyList<StatusCountDto> StatusCounts,
    ApplicationRateDto ResponseRate,
    ApplicationRateDto InterviewRate,
    ApplicationRateDto RejectionRate,
    IReadOnlyList<FunnelStageDto> Funnel,
    // v1-limitation signal (senior-cto-advisor bind 2026-06-29, Q2 implementation
    // note). The number of SENT applications whose CURRENT status is an off-spine
    // terminal (Rejected/Withdrawn/Ghosted). The aggregate persists no stage
    // history (only the current status + LastStatusChangeAt, which every
    // transition overwrites), so an application that progressed mid-funnel before
    // exiting off-spine — e.g. interviewed-then-rejected — is credited only at the
    // "Sent" stage of <see cref="Funnel"/>, not at the stage it actually reached.
    // When this is &gt; 0 the FE shows a footnote that the funnel may under-count
    // mid-funnel reach (§5 — a reduced-precision metric is FLAGGED, never silently
    // mis-reported). Surfacing the count (not re-derivable cleanly on the FE
    // without re-implementing the funnel's off-spine definition) keeps the funnel
    // semantics single-sourced on the backend.
    int OffFunnelExitCount,
    IReadOnlyList<MonthlyApplicationCountDto> MonthlyApplications);

/// <summary>
/// Count of applications in one <see cref="Domain.Applications.ApplicationStatus"/>
/// value. All ten statuses are emitted in ordinal order, zero-filled, so the FE
/// table is stable and complete (raw transparency; drafts are included here).
/// <paramref name="Status"/> is the SmartEnum name (serialised consistently with
/// <see cref="ApplicationDto.Status"/>).
/// </summary>
public sealed record StatusCountDto(string Status, int Count);

/// <summary>
/// A rate shipped as its numerator, denominator and rounded integer percent so
/// the FE never computes or mislabels it (§5). <paramref name="Percent"/> is 0
/// when <paramref name="Denominator"/> is 0 (the FE renders that as "inga skickade
/// ännu", not "0 %").
/// </summary>
public sealed record ApplicationRateDto(int Numerator, int Denominator, int Percent);

/// <summary>
/// One stage of the cumulative-from-sent conversion funnel. <paramref name="Stage"/>
/// is a stable contract key (see the <c>Stage*</c> constants on
/// <see cref="ApplicationStatsCalculator"/>) the FE maps to a Swedish label.
/// <paramref name="Count"/> = applications that reached this stage OR BEYOND;
/// <paramref name="PercentOfSent"/> = count over <see cref="ApplicationStatsDto.TotalSent"/>.
/// </summary>
public sealed record FunnelStageDto(string Stage, int Count, int PercentOfSent);

/// <summary>
/// One month bucket of the rolling 12-month applications-over-time series,
/// bucketed on <c>AppliedAt</c> (UTC). Buckets are zero-filled and ordered
/// oldest → newest; applications applied before the window are excluded from the
/// series only (still counted in the totals/status/funnel).
/// </summary>
public sealed record MonthlyApplicationCountDto(int Year, int Month, int Count);

/// <summary>
/// Minimal owner-scoped projection row <see cref="ApplicationStatsCalculator"/>
/// consumes — the current status name plus the idempotent apply date. No JobAd
/// metadata, no encrypted fields (stats need neither). The calculator is a pure
/// function of a list of these plus a reference instant.
/// </summary>
public sealed record ApplicationStatRow(string Status, DateTimeOffset? AppliedAt);
