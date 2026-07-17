using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// #311 #455 (ADR 0087 D2/D8(c); senior-cto-advisor 2026-07-01) — the <see cref="IJobAdEmployerReader"/>
/// impl. Reads the STORED <c>organization_number</c> shadow column for a set of ads in ONE round-trip.
/// Lives in Infrastructure because the id-set filter needs parameterized <c>= ANY</c> raw SQL and the
/// shadow column is read via <c>EF.Property</c> (both are Npgsql-assembly concerns arch-test-forbidden in
/// Application, ADR 0062 — parity <c>MatchScorer.ScoreBatchAsync</c> / <c>JobAdSearchQuery</c>).
///
/// <para>
/// EF Core 10 + Npgsql cannot translate <c>Contains()</c> over the strongly-typed <c>JobAdId</c> key
/// (memory <c>ef_strongly_typed_vo_contains</c> — both <c>List&lt;JobAdId&gt;.Contains(j.Id)</c> AND the
/// post-Select <c>.Value</c> form fail at runtime); <c>job_ads</c> is unbounded, so the status-batch
/// "load-all-then-client-filter" escape does not apply. <see cref="RelationalQueryableExtensions.FromSql"/>
/// parameterizes the <c>Guid[]</c> (<c>= ANY(@p)</c>, injection-safe, NOT concatenation — CLAUDE.md §5),
/// carries NO query filter (<c>JobAd</c> has no soft-delete axis, #821 — so retracted/archived ads are
/// NOT absent from the map; what actually yields a null org.nr is the raw_payload purge recomputing
/// the generated column, #824/#841), and the
/// <c>EF.Property</c> shadow projection stays server-side. InMemory hides both the translation and the
/// generated column, so the Testcontainers integration tests are the oracle (same memory).
/// </para>
///
/// <para>
/// <b>ADR 0087 D8(c):</b> the returned org.nr is a SERVER-SIDE-ONLY value — a sole-prop org.nr can equal
/// a personnummer (CLAUDE.md §5). This reader neither logs nor surfaces it; callers use it to key a
/// <c>CompanyWatch</c> or compute a <c>Followable</c> flag and must not put it in a DTO/URL/log.
/// </para>
/// </summary>
internal sealed class JobAdEmployerReader(AppDbContext db) : IJobAdEmployerReader
{
    // The EF.Property key EF maps to the STORED generated organization_number column (JobAdConfiguration).
    // The column name itself is an Infrastructure detail that never leaks to Application.
    private const string OrganizationNumberColumn = "OrganizationNumber";

    private static readonly IReadOnlyDictionary<Guid, string?> Empty =
        new Dictionary<Guid, string?>();

    public async Task<IReadOnlyDictionary<Guid, string?>> GetOrganizationNumbersByJobAdIdsAsync(
        IReadOnlyList<Guid> jobAdIds, CancellationToken cancellationToken)
    {
        if (jobAdIds.Count == 0)
            return Empty;

        var ids = jobAdIds.Distinct().ToArray();

        var rows = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Select(j => new EmployerOrgNrRow(j.Id, EF.Property<string?>(j, OrganizationNumberColumn)))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id.Value, r => r.OrgNr);
    }

    // Materialisation shape: JobAdId (value-converted) projects cleanly; .Value is taken client-side.
    private sealed record EmployerOrgNrRow(JobAdId Id, string? OrgNr)
    {
        // REDACTED (#883). The compiler-generated ToString() prints every member, so a plain {X} MEL
        // placeholder would write OrgNr (a possible sole-prop personnummer, ADR 0087 D8(c); CLAUDE.md
        // §5) into a log. Keeps Id; pinned by OrgNrRecordLoggingGuardTests.
        public override string ToString() => $"EmployerOrgNrRow(Id={Id}, org.nr redacted)";
    }
}
