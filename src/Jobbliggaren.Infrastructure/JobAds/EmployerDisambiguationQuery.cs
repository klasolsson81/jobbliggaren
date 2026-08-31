using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// ADR 0087 D6/D7 (#311 PR-2b C2) — <see cref="IEmployerDisambiguationQuery"/> implementation. The
/// projection lives here (not in a handler over IAppDbContext) because it uses PostgreSQL <c>ILIKE</c>
/// + <c>GROUP BY</c> over the mapped <c>organization_number</c> column — Npgsql-assembly LINQ
/// the architecture test forbids in Application (parity <see cref="JobAdSearchQuery"/> /
/// <c>FacetCountsAsync</c>, ADR 0062). A SEPARATE read concern from <see cref="JobAdSearchQuery"/>
/// (ADR 0087 D6/D7 — the disambiguation list must NOT be folded into <c>IJobAdSearchQuery</c>).
/// <para>
/// Returns the RAW org.nr grouped by legal entity; the personnummer guard (ADR 0087 D8(c)) is the
/// handler's job (masking at the surfacing boundary — Infrastructure does no masking).
/// </para>
/// </summary>
internal sealed class EmployerDisambiguationQuery(AppDbContext db) : IEmployerDisambiguationQuery
{
    // The LIKE escape character (Postgres default is '\'); passed to ILIKE so the escaped %/_ in the
    // user term are treated literally.
    private const string LikeEscape = "\\";

    public async ValueTask<IReadOnlyList<EmployerAdGroup>> SearchAsync(
        string nameQuery, int limit, CancellationToken cancellationToken)
    {
        // Case-insensitive CONTAINS on company_name; the user's term is LIKE-escaped so %/_ match
        // literally (correctness, not a security hole on public data). JobAd carries no query filter
        // (no soft-delete axis, #821). Ads with a NULL org.nr are excluded (partial-index
        // predicate). GROUP BY on the RAW org.nr server-side — never on a masked value (a null would
        // collapse distinct sole-props into one phantom row). company_name is stable per org.nr (one
        // legal entity = one registered name), so GROUP BY (org.nr, name) yields one row per entity.
        // Order by ad count desc (most-prolific = most-likely-intended first), name + org.nr tiebreak
        // for a deterministic order, then cap.
        var pattern = $"%{EscapeLike(nameQuery)}%";

        var groups = await db.JobAds
            .AsNoTracking()
            .Where(j => j.OrganizationNumber != null
                        && EF.Functions.ILike(j.Company.Name, pattern, LikeEscape))
            .GroupBy(j => new
            {
                OrganizationNumber = j.OrganizationNumber!,
                j.Company.Name,
            })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .ThenBy(g => g.Key.OrganizationNumber)
            .Take(limit)
            .Select(g => new EmployerAdGroup(g.Key.OrganizationNumber, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken);

        return groups;
    }

    public async ValueTask<IReadOnlyList<EmployerAdGroup>> SuggestActiveEmployersAsync(
        string nameTerm, int limit, CancellationToken cancellationToken) =>
        await SuggestActiveEmployersQuery(db, nameTerm, limit).ToListAsync(cancellationToken);

    /// <summary>
    /// The suggest projection as an un-executed <see cref="IQueryable{T}"/>, so the planner oracle can
    /// <c>EXPLAIN</c> PRODUCTION'S OWN expression tree instead of a hand-copied SQL string (the
    /// <c>PnrShapedPrefilterQueryPlanTests.PrefilterSql</c> precedent). That distinction is the whole
    /// point here: a copied string would keep asserting that <c>lower(company_name) LIKE …</c> is
    /// index-served long after this method had been changed to emit something else, which is exactly
    /// the failure mode <c>Migrate --explain-search</c> hit in #1603.
    /// </summary>
    internal static IQueryable<EmployerAdGroup> SuggestActiveEmployersQuery(
        AppDbContext db, string nameTerm, int limit)
    {
        // ⚠ `Like(Name.ToLower(), …)`, NEVER `ILike(Name, …)` as SearchAsync above uses. The index
        // #1546 shipped is keyed on the EXPRESSION `lower(company_name)`
        // (ix_job_ads_company_name_lower_trgm, migration 20260830133506), and an ILIKE over the bare
        // column does not contain that expression, so the planner cannot use the index for it. The
        // two forms are semantically identical and differ only in whether they are index-served —
        // which is why the mistake is invisible in a behavioural test and why
        // JobAdPlannerUsabilityOracleTests.EmployerSuggest_IsIndexServed exists. This is the byte-
        // shape JobAdSearchComposition:240 already emits for the same index; keep them identical.
        // On a per-keystroke surface the difference is a sequential scan of job_ads per keystroke.
        //
        // No explicit ESCAPE argument: '\' is PostgreSQL's default LIKE escape, so the C#-side
        // EscapeLike() plus the two-arg overload is semantically identical to SearchAsync's three-arg
        // ILIKE and produces the operator shape proven index-served.
        //
        // Status == Active is the whole reason this method exists beside SearchAsync — see the port's
        // docblock. It is the same predicate JobAdSearchComposition:82 applies to `?employer=`, so a
        // suggestion's ad count is the count the chip then shows.
#pragma warning disable CA1304, CA1311
        var pattern = $"%{EscapeLike(nameTerm).ToLowerInvariant()}%";

        return db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active
                        && j.OrganizationNumber != null
                        && EF.Functions.Like(j.Company.Name.ToLower(), pattern))
            .GroupBy(j => new
            {
                OrganizationNumber = j.OrganizationNumber!,
                j.Company.Name,
            })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Name)
            .ThenBy(g => g.Key.OrganizationNumber)
            .Take(limit)
            .Select(g => new EmployerAdGroup(g.Key.OrganizationNumber, g.Key.Name, g.Count()));
#pragma warning restore CA1304, CA1311
    }

    // Escape the LIKE metacharacters (%, _, and the escape char itself) so a user term like "50%"
    // matches literally rather than as a wildcard.
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
