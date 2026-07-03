using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;

/// <summary>
/// #444 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — projects the signed-in user's OWN
/// application history grouped by employer org.nr. Owner-scoped on <c>JobSeekerId</c> (resolved from
/// <c>ICurrentUser</c>, never the wire — M2 / IDOR). JOINs applications to public <c>job_ads</c> to
/// read the employer org.nr (STORED shadow column <c>"OrganizationNumber"</c>, ADR 0087 D1) and the
/// company name at READ (ADR 0087 D2 — never a denormalised snapshot). Only submitted applications
/// (<c>AppliedAt != null</c>) are history; drafts are intent (Art. 6(1)(b) purpose, ADR 0090 D1).
///
/// <para>
/// <b>Archived-ad honesty (DPIA #456 finding).</b> The JOIN inherits <c>job_ads</c>' global
/// soft-delete query-filter (ADR 0048): an application whose ad has since been retracted resolves to
/// no live ad, so its org.nr is unresolvable and it is NOT attributed to an employer here.
/// <c>AdSnapshot</c> (#315 / ADR 0086) captures the company NAME at apply-time but NOT the org.nr, so
/// archived applications cannot be grouped by employer in v1 — a known, honestly-recorded limitation,
/// not a silent drop. Manual postings (no <c>JobAdId</c>) likewise carry no employer org.nr and are
/// excluded.
/// </para>
///
/// <para>
/// <b>org.nr surfacing (FORK C1 / D8(c)).</b> The raw org.nr is read SERVER-SIDE only, to GROUP BY;
/// a personnummer-shaped value is masked to null + flagged before it leaves the handler, and is never
/// logged (<c>OrganizationNumberSurfacingGuardTests</c> covers this source). The org.nr predicate uses
/// the <c>EF.Property&lt;string?&gt;</c> shadow column — NEVER a strongly-typed VO in <c>Contains</c>
/// (the VO-in-<c>Contains</c> → 500 translation trap).
/// </para>
/// </summary>
public sealed class GetEmployerApplicationHistoryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetEmployerApplicationHistoryQuery, IReadOnlyList<EmployerApplicationHistoryDto>>
{
    public async ValueTask<IReadOnlyList<EmployerApplicationHistoryDto>> Handle(
        GetEmployerApplicationHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        // Owner-scoped submitted applications joined to their employer identity on public job_ads.
        // GroupJoin + SelectMany(DefaultIfEmpty) mirrors GetApplicationsQueryHandler's proven-
        // translatable LEFT JOIN over the nullable-struct FK (ADR 0048): a retracted ad inherits the
        // global soft-delete filter -> j == null -> OrgNr null -> filtered out below (the archived-ad
        // gap, documented above). OrgNr is the STORED "OrganizationNumber" shadow column read
        // SERVER-SIDE to group on; Status is a value-converted SmartEnum projected WHOLE (its .Name is
        // read in memory, never in the expression tree). Bounded to one user's own submitted
        // applications (no pagination, parity ListCompanyWatches).
        var rows = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId && a.AppliedAt != null)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                OrgNr = j != null ? EF.Property<string?>(j, "OrganizationNumber") : null,
                CompanyName = j != null ? j.Company.Name : null,
                x.a.AppliedAt,
                x.a.Status,
            })
            .Where(r => r.OrgNr != null)
            .ToListAsync(cancellationToken);

        // Group by employer org.nr in memory (the set is one user's bounded history). Mask + flag a
        // personnummer-shaped org.nr (FORK C1 / D8(c)) via FromTrusted — the value came from the
        // already-validated STORED column (parity DisambiguateEmployersQueryHandler); the raw value
        // never leaves this handler un-flagged.
        return rows
            .GroupBy(r => r.OrgNr!)
            .Select(g =>
            {
                var isProtected = OrganizationNumber.FromTrusted(g.Key).IsPersonnummerShaped();
                var entries = g
                    .OrderByDescending(r => r.AppliedAt!.Value)
                    .Select(r => new ApplicationHistoryEntryDto(r.AppliedAt!.Value, r.Status.Name))
                    .ToList();
                return new EmployerApplicationHistoryDto(
                    OrganizationNumber: isProtected ? null : g.Key,
                    IsProtectedIdentity: isProtected,
                    // One legal entity = one name; take the most-recently-applied ad's employer name.
                    CompanyName: g.OrderByDescending(r => r.AppliedAt!.Value)
                        .Select(r => r.CompanyName)
                        .FirstOrDefault(),
                    ApplicationCount: entries.Count,
                    Applications: entries);
            })
            // Most-recently-applied employer first (parity ListCompanyWatches OrderByDescending).
            .OrderByDescending(e => e.Applications[0].AppliedAt)
            .ToList();
    }
}
