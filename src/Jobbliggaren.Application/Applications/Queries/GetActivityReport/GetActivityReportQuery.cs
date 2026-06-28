using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// AF activity-report query (issue #316). <paramref name="Year"/> /
/// <paramref name="Month"/> are optional: when both are null the handler
/// defaults to the PREVIOUS month (you report the prior month's activities
/// between the 1st and the 14th). Both-or-neither — the validator rejects a
/// half-specified pair. Read-only, JobSeeker-scoped in the handler.
/// </summary>
public sealed record GetActivityReportQuery(int? Year = null, int? Month = null)
    : IQuery<ActivityReportDto>;
