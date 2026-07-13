using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;

/// <summary>
/// #444 (ADR 0087 D2 read-model; DPIA #456; ADR 0090 D1 — the GO ruling + Art. 6(1)(b) basis, which is
/// the ONLY thing D1 actually says here; it carries no archived-ad passage, see #824) — projects the
/// signed-in user's OWN application history
/// grouped by employer org.nr. Owner-scoped on <c>JobSeekerId</c> (resolved from <c>ICurrentUser</c>,
/// never the wire — M2 / IDOR). JOINs applications to public <c>job_ads</c> to read the employer
/// org.nr (STORED shadow column <c>"OrganizationNumber"</c>, ADR 0087 D1) and the company name at
/// READ (ADR 0087 D2 — never a denormalised snapshot). Only submitted applications
/// (<c>AppliedAt != null</c>) are history; drafts are intent (Art. 6(1)(b) purpose, ADR 0090 D1).
///
/// <para>
/// <b>Attribution is governed by the ad's AGE — not by archival, and not by soft delete (#824).</b>
/// The org.nr shadow column is a STORED generated column derived from <c>raw_payload</c>
/// (<c>HasComputedColumnSql("raw_payload->'employer'->>'organization_number'")</c>, ADR 0087 D1), and
/// <c>PurgeStaleRawPayloadsJob</c> nulls <c>raw_payload</c> once an ad is older than
/// <c>RawPayloadRetentionDays</c> (30) from <c>PublishedAt</c> — at which point Postgres RECOMPUTES
/// the generated column to NULL. So:
/// <list type="bullet">
/// <item>an ARCHIVED but recent ad still joins and IS attributed (archival does not hide a row —
/// <c>JobAd.DeletedAt</c> has no writer anywhere in <c>src/</c> and its query filter is vacuous, #821);</item>
/// <item>an ACTIVE but old ad has a NULL org.nr and is NOT attributed.</item>
/// </list>
/// Pinned by <c>GetEmployerApplicationHistoryQueryHandlerIntegrationTests</c> (both directions). The
/// value additionally <b>thrashes daily</b> until #841 lands: the 02:00 full-backfill sync rewrites
/// <c>raw_payload</c> and the 04:30 purge nulls it again, so an old-but-still-listed ad resolves its
/// org.nr for ~2.5h/day and not for the other ~21.5h.
/// </para>
///
/// <para>
/// <b>The residue is DROPPED here, and that is a known open defect (#824).</b> The
/// <c>.Where(r =&gt; r.OrgNr != null)</c> below silently omits every application whose ad has aged out.
/// DPIA #456 §8 <b>mandated</b> the opposite — <i>"the projection must degrade honestly ('företag
/// okänt' or fall back to the snapshot company name), never fabricate"</i> — and that mitigation was
/// never built. Honest degradation lands in #824 PR 3 (a separate, explicitly-marked bucket keyed on
/// the APPLICATION; the company name may be used as a LABEL, never as a grouping KEY — name-keying
/// would trade an undercount for a fabrication, which ADR 0087 D1 made org.nr canonical to prevent).
/// The root cause — durable projections derived from a purgeable base column — is #841. Manual
/// postings (no <c>JobAdId</c>) carry no employer org.nr and are excluded by design.
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
        // translatable LEFT JOIN over the nullable-struct FK (ADR 0048). NOTE: for a JobAd-LINKED
        // application, the ad row is NOT what goes missing -- archiving an ad does not hide it (#821).
        // What goes missing is the org.nr VALUE, once the purge nulls raw_payload and Postgres
        // recomputes the generated column to NULL (see the type remarks + #824). j IS still null for a
        // manually-logged application (JobAdId == null) -- that case is real and separately tested; it
        // is simply not the archived-ad case the old docs conflated it with.
        // OrgNr is the STORED "OrganizationNumber" shadow column read SERVER-SIDE to group
        // on; Status is a value-converted SmartEnum projected WHOLE (its .Name is
        // read in memory, never in the expression tree). Bounded to one user's own submitted
        // applications (no pagination v1, parity ListCompanyWatches). Unlike a curated company-watch
        // set this history grows monotonically; if the hub ever becomes a hot path the preselected
        // remedy is a server-side count + recent-N (or a soft cap) — LoggingBehavior already measures
        // the latency as the observe-only ratchet signal (ADR 0045 / §2.5).
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
            // #824: this is the silent drop DPIA #456 §8 forbade. It stays until #824 PR 3 replaces it
            // with an honest, separately-labelled bucket -- changing it here would be a behaviour change
            // in a truth-only PR. Do not "fix" it by grouping the residue on CompanyName: that fabricates
            // a legal-entity identity we do not have (ADR 0087 D1).
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
                // Sort the employer's applications once (most-recent first); both the entry list and
                // the display name (one legal entity = one name -> the latest ad's name) derive from it.
                var ordered = g.OrderByDescending(r => r.AppliedAt!.Value).ToList();
                var isProtected = OrganizationNumber.FromTrusted(g.Key).IsPersonnummerShaped();
                var entries = ordered
                    .Select(r => new ApplicationHistoryEntryDto(r.AppliedAt!.Value, r.Status.Name))
                    .ToList();
                return new EmployerApplicationHistoryDto(
                    OrganizationNumber: isProtected ? null : g.Key,
                    IsProtectedIdentity: isProtected,
                    CompanyName: ordered[0].CompanyName,
                    ApplicationCount: entries.Count,
                    Applications: entries);
            })
            // Most-recently-applied employer first (parity ListCompanyWatches OrderByDescending).
            .OrderByDescending(e => e.Applications[0].AppliedAt)
            .ToList();
    }
}
