using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// ADR 0087 D6/D7 (#311 PR-2b C2) — <see cref="IEmployerDisambiguationQuery"/> implementation. The
/// projection lives here (not in a handler over IAppDbContext) because it uses PostgreSQL <c>ILIKE</c>
/// + <c>GROUP BY</c> over the STORED <c>organization_number</c> shadow column — Npgsql-assembly LINQ
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
            .Where(j => EF.Property<string?>(j, "OrganizationNumber") != null
                        && EF.Functions.ILike(j.Company.Name, pattern, LikeEscape))
            .GroupBy(j => new
            {
                OrganizationNumber = EF.Property<string?>(j, "OrganizationNumber")!,
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

    // Escape the LIKE metacharacters (%, _, and the escape char itself) so a user term like "50%"
    // matches literally rather than as a wildcard.
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
