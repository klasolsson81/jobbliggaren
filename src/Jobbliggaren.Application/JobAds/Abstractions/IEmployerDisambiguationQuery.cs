namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// ADR 0087 D6/D7 (#311 PR-2b C2) — the employer-disambiguation read-model projection. A SEPARATE
/// read concern from <see cref="IJobAdSearchQuery"/> (D6: the disambiguation list must NOT be folded
/// into the filter/facet port). Given a company-name term, returns the DISTINCT legal entities in the
/// ad corpus that match — one row per org.nr (the canonical follow key; the "Volvo×20" trap is why
/// org.nr is canonical, ADR 0087) with the employer name + ad count.
/// <para>
/// Lives in Infrastructure because the projection uses PostgreSQL <c>ILIKE</c> + <c>GROUP BY</c> over
/// the STORED <c>organization_number</c> shadow column — provider (Npgsql) LINQ that the architecture
/// test forbids in Application (parity <see cref="IJobAdSearchQuery"/>, ADR 0062). The shadow-column
/// name is an Infrastructure secret; it never leaks to Application.
/// </para>
/// <para>
/// <b>Returns RAW org.nr</b> (<see cref="EmployerAdGroup.OrganizationNumber"/>) — the personnummer
/// guard (ADR 0087 D8(c)) is applied in the Application handler, at the surfacing boundary, before
/// the value becomes a wire DTO (mirrors <c>ListCompanyWatchesQueryHandler</c>). Infrastructure does
/// no masking; the handler owns it.
/// </para>
/// </summary>
public interface IEmployerDisambiguationQuery
{
    /// <summary>
    /// Distinct employers whose name contains <paramref name="nameQuery"/> (case-insensitive),
    /// grouped by org.nr, ordered by ad count desc, capped at <paramref name="limit"/>. Ads with a
    /// NULL org.nr are excluded (partial-index predicate); the global soft-delete filter applies.
    /// </summary>
    ValueTask<IReadOnlyList<EmployerAdGroup>> SearchAsync(
        string nameQuery, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// ADR 0087 D6 (#311 PR-2b C2) — one disambiguation group: a distinct legal entity in the ad corpus.
/// An Application-INTERNAL projection intermediate carrying the RAW org.nr — deliberately NOT a
/// <c>*Dto</c> (it never crosses the wire; the handler masks it into
/// <c>EmployerDisambiguationDto</c> at the surfacing boundary per ADR 0087 D8(c)). The
/// <c>OrganizationNumberSurfacingGuardTests</c> partition scans <c>*Dto</c> types only — this raw
/// intermediate is intentionally outside that scope, mirroring how <c>ListCompanyWatchesQueryHandler</c>
/// holds a raw <c>OrganizationNumber</c> transiently before masking.
/// </summary>
public sealed record EmployerAdGroup(string OrganizationNumber, string CompanyName, int AdCount);
