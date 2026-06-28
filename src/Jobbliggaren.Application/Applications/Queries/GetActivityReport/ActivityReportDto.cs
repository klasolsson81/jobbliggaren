namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// The AF activity-report view for one month (issue #316): the resolved month
/// (echoed back so the FE month picker can reflect the server-computed default)
/// plus every application the user submitted in that month, one item per sought
/// job. The "minst 6" counter is <c>Applications.Count</c> — derived on the FE,
/// not stored.
/// </summary>
public sealed record ActivityReportDto(
    int Year,
    int Month,
    IReadOnlyList<ActivityReportItemDto> Applications);
