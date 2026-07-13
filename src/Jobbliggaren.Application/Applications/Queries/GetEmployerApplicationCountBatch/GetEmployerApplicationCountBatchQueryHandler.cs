using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;

/// <summary>
/// #446 (#311; ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1) — composes the /jobb "tidigare
/// ansökningar till detta företag" overlay for one page. Two BOUNDED, page-level round-trips (never a
/// count-per-card — ADR 0045 / CLAUDE.md §2.5):
/// <list type="number">
/// <item>the page ads' employer org.nr, server-side, via <see cref="IJobAdEmployerReader"/> (#455's
/// <c>= ANY</c> shadow-column reader — org.nr never surfaced, ADR 0087 D8(c));</item>
/// <item>the caller's OWN submitted applications joined to their ad's employer org.nr — the SAME
/// translatable <c>GroupJoin</c>/<c>SelectMany(DefaultIfEmpty)</c> + in-memory group idiom as
/// <see cref="GetEmployerApplicationHistory.GetEmployerApplicationHistoryQueryHandler"/> (#444), bounded
/// to one user's own history.</item>
/// </list>
/// The two maps are joined on org.nr in memory to produce a JobAdId → count map (positive-only).
///
/// <para>
/// <b>Owner-scoped (M2 / IDOR).</b> <c>JobSeekerId</c> is resolved from <see cref="ICurrentUser"/>, never
/// the wire; the history query filters on it and never enumerates another user's applications. Anonymous
/// or seeker-less caller → empty (the endpoint is also auth-gated — defence-in-depth).
/// </para>
///
/// <para>
/// <b>org.nr stays server-side (ADR 0087 D8 / CLAUDE.md §5).</b> The org.nr is used ONLY as the join key
/// between the two maps and never leaves this handler; the result is a plain <c>int</c> per JobAdId. A
/// sole-proprietorship org.nr can equal a personnummer, but nothing here surfaces it, so there is no M1
/// value to mask — the count reveals no identity.
/// </para>
///
/// <para>
/// <b>The count UNDERCOUNTS, and it is not yet honest about it (#824).</b> Attribution here is
/// governed by the ad's AGE — not by archival and not by soft delete. The org.nr on both sides of the
/// join is a STORED generated column derived from <c>raw_payload</c>; <c>PurgeStaleRawPayloadsJob</c>
/// nulls <c>raw_payload</c> 30 days after <c>PublishedAt</c>, and Postgres then RECOMPUTES that column
/// to NULL. So an ARCHIVED but recent ad still counts (archival hides no row — <c>JobAd.DeletedAt</c>
/// has no writer in <c>src/</c> and its filter is vacuous, #821), while an ACTIVE but old ad does not.
/// Worse, until #841 lands the value <b>thrashes daily for an ad still listed in the Platsbanken feed</b>
/// (the 02:00 full-backfill sync rewrites <c>raw_payload</c>; the 04:30 purge nulls it again) — so the
/// same application is counted for ~2.5h/day and not for the other ~21.5h, which makes the number the
/// user is shown NON-DETERMINISTIC. (An ad that has LEFT the feed does not thrash: it is never rewritten,
/// so its org.nr is permanently NULL.) That is the
/// substance of #824's Art. 5(1)(d) finding: <c>"Du har {count} tidigare ansökningar till detta
/// företag"</c> is an unhedged factual claim to the data subject about her own data, and it is not
/// reliably true. The copy is hedged in #824 PR 4; the root cause is fixed in #841.
/// </para>
///
/// <para>
/// <b>Why the residue cannot simply be recovered here.</b> Unlike #444, this handler has no name axis
/// to fall back on: it joins the caller's applied org.nrs against THIS PAGE's ad org.nrs. Attributing
/// an unresolvable application to the page's employer would require matching on company name — a
/// fabrication, and a more dangerous one than in #444, because a false positive tells the user she has
/// applied to a company she may never have applied to (and she may then decline to apply). An
/// undercount is safe; a name-guessed overcount is not. Keep dropping; hedge the copy.
/// </para>
/// </summary>
public sealed class GetEmployerApplicationCountBatchQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IJobAdEmployerReader employerReader)
    : IQueryHandler<GetEmployerApplicationCountBatchQuery, EmployerApplicationCountBatchDto>
{
    private static readonly EmployerApplicationCountBatchDto Empty =
        new(new Dictionary<Guid, int>());

    public async ValueTask<EmployerApplicationCountBatchDto> Handle(
        GetEmployerApplicationCountBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.JobAdIds.Count == 0)
            return Empty;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Empty;

        // (1) Page ads -> employer org.nr, server-side (#455 reader: `= ANY` raw SQL + EF.Property shadow
        // column). The reader composes with the global soft-delete filter, but that filter is VACUOUS
        // (#821) -- an archived ad still resolves its org.nr. What actually removes an ad from this map
        // is the org.nr having been recomputed to NULL after the raw_payload purge (#824/#841), not any
        // kind of deletion. org.nr is server-side-only here.
        var orgNrByJobAdId = await employerReader.GetOrganizationNumbersByJobAdIdsAsync(
            query.JobAdIds, cancellationToken);

        // The distinct employers actually present on this page — nothing to count if none carry an org.nr
        // (B2 not-yet-re-ingested ads are all-null; so is any ad whose raw_payload has been purged, #824).
        var pageOrgNrs = orgNrByJobAdId.Values
            .Where(orgNr => orgNr is not null)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (pageOrgNrs.Count == 0)
            return Empty;

        // (2) The caller's OWN submitted applications joined to their ad's employer org.nr. Byte-for-byte
        // #444's proven-translatable shape (GroupJoin + SelectMany(DefaultIfEmpty) over the nullable
        // JobAdId FK, ADR 0048 LEFT JOIN; org.nr via the EF.Property shadow column read SERVER-SIDE; group
        // in memory). Bounded to one user's own history (parity #444 — no pagination). InMemory hides the
        // `= ANY`/shadow-column translation, so the Testcontainers integration tests are the oracle.
        var appliedOrgNrs = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId && a.AppliedAt != null)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(
                x => x.ja.DefaultIfEmpty(),
                (x, j) => j != null ? EF.Property<string?>(j, "OrganizationNumber") : null)
            // #824: the silent undercount. An application whose ad has aged past the raw_payload horizon
            // has a NULL org.nr and drops out here. It stays (the copy is hedged in #824 PR 4; the root
            // cause dies in #841). Do NOT recover it by matching on CompanyName: a name-guessed overcount
            // would tell the user she has already applied to a company she may never have applied to.
            .Where(orgNr => orgNr != null)
            .ToListAsync(cancellationToken);

        // Group the caller's applications by employer org.nr in memory (bounded set, parity #444). Only
        // employers on the current page matter — the rest are dropped on lookup below.
        var countByOrgNr = appliedOrgNrs
            .Where(orgNr => pageOrgNrs.Contains(orgNr!))
            .GroupBy(orgNr => orgNr!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        if (countByOrgNr.Count == 0)
            return Empty;

        // Join the two maps on org.nr → JobAdId : count (positive-only, parity JobAdMatchBatchDto). org.nr
        // never leaves the handler; the DTO carries only the JobAdId and the int count.
        var counts = new Dictionary<Guid, int>();
        foreach (var (jobAdId, orgNr) in orgNrByJobAdId)
        {
            if (orgNr is not null && countByOrgNr.TryGetValue(orgNr, out var count) && count > 0)
                counts[jobAdId] = count;
        }

        return new EmployerApplicationCountBatchDto(counts);
    }
}
