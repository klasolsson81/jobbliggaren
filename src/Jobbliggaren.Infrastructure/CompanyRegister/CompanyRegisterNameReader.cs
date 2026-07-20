using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #994 — the <see cref="ICompanyRegisterNameReader"/> impl: a bounded batch lookup of
/// <c>company_name</c> by org.nr against the local <c>company_register</c> replica, read through the
/// concrete <see cref="AppDbContext"/> (the register is NOT a <c>DbSet</c> on <c>IAppDbContext</c> —
/// DPIA C-D4/M-C5; the entity + status enum are Infrastructure-internal, reachable only here).
///
/// <para>
/// <b>Plain EF LINQ, NOT raw SQL</b> — a deliberate divergence from the sibling register ports.
/// <see cref="CompanyRegisterSearchQuery"/> / <c>CompanyWatchBrowseQuery</c> drop to Npgsql ONLY
/// because their predicates need the GIN array-overlap <c>&amp;&amp;</c> and the functional
/// <c>lower()</c>-prefix index shapes LINQ cannot emit. This lookup is a primary-key
/// <c>organization_number = ANY(...)</c> — EF translates it cleanly and the PK index serves it, so
/// there is no index-shape reason to leave LINQ (§3.6: <c>AsNoTracking</c> read, no repository).
/// </para>
///
/// <para>
/// Lifecycle status is NOT filtered (interface doc): a followed company that has since deregistered
/// keeps a resolvable name — the register retains deregistered rows exactly so company_watch
/// history stays resolvable (ADR 0091), and the name is public legal-entity data.
/// </para>
/// </summary>
internal sealed class CompanyRegisterNameReader(AppDbContext db) : ICompanyRegisterNameReader
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<string, string>> GetNamesByOrganizationNumbersAsync(
        IReadOnlyList<string> organizationNumbers, CancellationToken cancellationToken)
    {
        if (organizationNumbers.Count == 0)
            return Empty;

        // organization_number is the natural PK (unique) → the ToDictionary below cannot key-clash.
        var orgNrs = organizationNumbers.Distinct(StringComparer.Ordinal).ToArray();

        var rows = await db.Set<ScbCompanyRegisterEntry>()
            .AsNoTracking()
            .Where(e => orgNrs.Contains(e.OrganizationNumber))
            .Select(e => new { e.OrganizationNumber, e.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.OrganizationNumber, r => r.Name, StringComparer.Ordinal);
    }
}
