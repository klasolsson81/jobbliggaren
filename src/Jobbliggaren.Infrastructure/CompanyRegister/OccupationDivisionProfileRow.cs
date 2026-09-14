namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — one row of the occupation × SNI-division profile: how many of an occupation group's ads
/// sit with an employer in one huvudgrupp. Infrastructure-internal and NOT a <c>DbSet</c> on
/// <c>IAppDbContext</c>, for the same reason <c>company_register</c> itself is not one (ADR 0139's
/// layer half, pinned by <c>ScbCompanyRegisterLayerTests</c>): the table is derived from the
/// register and its home is where the register's home is. The EF configuration exists for the
/// migration schema; the table is reached only by raw SQL in <see cref="OccupationDivisionProfileStore"/>.
///
/// <para>
/// A corpus statistic, not personal data: no user id, no criterion id, no org.nr. None of ADR 0139's
/// person-attributable obligations (FK cascade, erasure oracle, the residual-exposure list) arise —
/// confirmed by security-auditor on PR 2 of #1682, not assumed.
/// </para>
/// <para>
/// The row IS the (group, division) pair, so the composite key is the natural one and a duplicate is
/// unrepresentable (the <c>CompanyWatchCriterionMember</c> shape). Unlike that table, the plan a
/// reader gets here is stable by construction: <c>n_distinct</c> on the leading key column is the
/// number of occupation groups with ads (~395 over ~5 100 rows), a property of the taxonomy rather
/// than of how many users exist — the drift #1706 found on the member table cannot happen here.
/// </para>
/// </summary>
internal sealed class OccupationDivisionProfileRow
{
    /// <summary>
    /// <see cref="DivisionCode"/> for the ads whose employer is not in the register (no org.nr on
    /// the ad, or an org.nr the register does not hold). Two characters, non-digit: it cannot
    /// collide with a real huvudgrupp code and cannot survive a careless <c>LEFT(code, 2)</c>
    /// unnoticed. It never crosses the Application boundary — the query port maps it to
    /// <c>OccupationDivisionProfile.NotInRegisterAdCount</c>.
    /// </summary>
    public const string NotInRegisterCode = "--";

    /// <summary>
    /// <see cref="DivisionCode"/> for the ads whose employer IS in the register but carries no SNI
    /// code at all (an empty <c>sni_codes</c> array). Kept apart from <see cref="NotInRegisterCode"/>
    /// so two facts never share one symbol; measured at zero rows on 2026-09-14, and counted on the
    /// run row so a bucket that starts filling is visible instead of silent.
    /// </summary>
    public const string NoSniCode = "-?";

    public required string OccupationGroupConceptId { get; init; }

    /// <summary>A two-digit SNI 2025 huvudgrupp code, or one of the two sentinels above.</summary>
    public required string DivisionCode { get; init; }

    public required int AdCount { get; init; }
}
