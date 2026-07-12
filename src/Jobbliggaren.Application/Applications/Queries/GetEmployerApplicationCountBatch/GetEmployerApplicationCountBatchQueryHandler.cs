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
/// <b>⚠ DISPUTED — see #824 (raised by #805-3). The claim below is believed FALSE and is preserved
/// verbatim only so the disputed text is traceable; do NOT rely on it.</b> <c>JobAd.DeletedAt</c> has
/// no writer anywhere in <c>src/</c>, so the global soft-delete filter is vacuous (#821). A retracted
/// ad is ARCHIVED (<c>Status = "Archived"</c>) and <b>still joins</b> — its org.nr resolves and the
/// application IS counted. That is the opposite of the "known limitation" recorded below, and it also
/// means #445's premise needs re-examination. #824 decides the intended behaviour and updates the
/// DPIA. Original claim:
/// <para>
/// <b>Archived-ad honesty (#444 / DPIA #456 parity).</b> Only submitted applications joined to a LIVE
/// (non-soft-deleted) ad contribute org.nr (the <c>SelectMany</c> filters <c>OrgNr != null</c>; the
/// reader inherits the global soft-delete filter). An application whose ad was retracted has no
/// resolvable org.nr and is not attributed — the known, honestly-recorded #444 limitation (#445 tracks
/// capturing org.nr on <c>AdSnapshot</c>), not a silent miscount.
/// </para>
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
        // column, inherits the global soft-delete filter — which is VACUOUS, see the DISPUTED note on
        // the class: an archived ad still resolves its org.nr, #824). org.nr is server-side-only here.
        var orgNrByJobAdId = await employerReader.GetOrganizationNumbersByJobAdIdsAsync(
            query.JobAdIds, cancellationToken);

        // The distinct employers actually present on this page — nothing to count if none carry an org.nr
        // (B2 not-yet-re-ingested ads are all-null; a soft-deleted id is simply absent from the map).
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
