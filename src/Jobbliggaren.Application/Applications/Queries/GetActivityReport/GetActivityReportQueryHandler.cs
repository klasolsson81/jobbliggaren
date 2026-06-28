using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// AF activity-report read model (issue #316). Deterministic projection — NO AI.
/// Lists every application the current user submitted in the selected month,
/// one item per sought job, so the FE can offer a per-field copy button.
///
/// "Submitted in month M" = <c>AppliedAt ∈ [start, end)</c> regardless of the
/// application's CURRENT status: the person applied that month even if the
/// thread has since moved to Rejected/Accepted/Ghosted (senior-cto-advisor
/// 2026-06-28 D3). Draft applications have a null <c>AppliedAt</c> and are
/// excluded; soft-deleted applications are excluded by the global query filter.
///
/// Month boundaries are UTC-derived (half-open). When the caller passes no
/// month the handler defaults to the previous month from
/// <see cref="IDateTimeProvider"/> (CLAUDE.md §5 — never <c>DateTime.UtcNow</c>).
/// </summary>
public sealed class GetActivityReportQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IDateTimeProvider clock)
    : IQueryHandler<GetActivityReportQuery, ActivityReportDto>
{
    public async ValueTask<ActivityReportDto> Handle(
        GetActivityReportQuery query, CancellationToken cancellationToken)
    {
        var (year, month) = ResolveMonth(query);
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);

        if (!currentUser.UserId.HasValue)
            return new ActivityReportDto(year, month, []);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return new ActivityReportDto(year, month, []);

        // ADR 0048: EN LEFT JOIN job_ads via GroupJoin/DefaultIfEmpty FÖRE
        // materialisering. JobAd:s globala query-filter (DeletedAt == null) ärvs
        // → soft-deletad JobAd ger j == null → ingen metadata (önskat; vi visar
        // inte detaljer för en annons användaren inte längre får se).
        // IgnoreQueryFilters / manuellt DeletedAt-predikat FÖRBJUDET (ADR 0048 c).
        var rows = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId
                        && a.AppliedAt != null
                        && a.AppliedAt >= start
                        && a.AppliedAt < end)
            .OrderBy(a => a.AppliedAt)
            .ThenBy(a => a.Id)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // "Ort"-källa: kommun-concept-id är en shadow-prop (ACL, ADR
                // 0043/0067 — ingen Domain-koppling till JobTech-taxonomin),
                // härledd STORED ur raw_payload. Manuell ansökan + soft-deletad
                // annons → j == null → ingen ort.
                MunicipalityConceptId =
                    j != null ? EF.Property<string?>(j, "MunicipalityConceptId") : null
            })
            .Select(r => new
            {
                r.a.Id,
                AppliedAt = r.a.AppliedAt!.Value,
                Employer = r.j != null
                    ? r.j.Company.Name
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Company : null,
                Title = r.j != null
                    ? r.j.Title
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Title : null,
                Url = r.j != null
                    ? r.j.Url
                    : r.a.ManualPosting != null ? r.a.ManualPosting.Url : null,
                Source = r.j != null
                    ? r.j.Source.Value
                    : r.a.ManualPosting != null ? "Manual" : null,
                r.MunicipalityConceptId
            })
            .ToListAsync(cancellationToken);

        var locationByConceptId = await ResolveLocationsAsync(
            rows.Select(r => r.MunicipalityConceptId), cancellationToken);

        var items = rows
            .Select(r => new ActivityReportItemDto(
                r.Id.Value,
                r.AppliedAt,
                r.Employer,
                r.Title,
                r.MunicipalityConceptId is not null
                    && locationByConceptId.TryGetValue(r.MunicipalityConceptId, out var loc)
                        ? loc
                        : null,
                r.Source,
                r.Url))
            .ToList();

        return new ActivityReportDto(year, month, items);
    }

    private (int Year, int Month) ResolveMonth(GetActivityReportQuery query)
    {
        if (query.Year.HasValue && query.Month.HasValue)
            return (query.Year.Value, query.Month.Value);

        // Default = previous month. Validator guarantees both-or-neither, so we
        // only reach here when both are null.
        var previous = clock.UtcNow.AddMonths(-1);
        return (previous.Year, previous.Month);
    }

    /// <summary>
    /// Batch-resolve distinct municipality concept-ids to human labels via the
    /// taxonomy ACL (one call, bounded). A concept-id that does not resolve
    /// (taxonomy drift) yields the port's "Okänd kod (id)" fallback — we drop
    /// it rather than surface the opaque id in a civic report (§5).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveLocationsAsync(
        IEnumerable<string?> conceptIds, CancellationToken cancellationToken)
    {
        var distinct = conceptIds
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToList();

        if (distinct.Count == 0)
            return new Dictionary<string, string>();

        var labels = await taxonomy.ResolveLabelsAsync(distinct, cancellationToken);

        var map = new Dictionary<string, string>(labels.Count);
        foreach (var label in labels)
        {
            // Drop the graceful-degradation fallback (TaxonomyReadModel.cs:49
            // — "Okänd kod ({id})") so an unresolved code renders as "—", never
            // as a leaked concept-id.
            if (label.Label != $"Okänd kod ({label.ConceptId})")
                map[label.ConceptId] = label.Label;
        }
        return map;
    }
}
