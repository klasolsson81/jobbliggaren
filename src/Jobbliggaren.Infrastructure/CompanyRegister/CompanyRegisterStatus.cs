namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — the coarse, actionable lifecycle status of a
/// <see cref="ScbCompanyRegisterEntry"/>, derived from SCB's <c>Företagsstatus</c> at ingest.
/// Deliberately two-valued: the register (and its future smart-bevakning consumers) only act on
/// "is this company live or not". The full SCB status fidelity (never-active vs de-registered vs
/// the raw code) is preserved verbatim in <see cref="ScbCompanyRegisterEntry.ScbStatusRaw"/>, so
/// richer states can be projected later without a schema change (senior-cto-advisor 2026-07-04,
/// Fork 4 — raw + derived, mirroring the JobAd raw_payload + shadow-column pattern).
///
/// <para>Stored by NAME (<c>HasConversion&lt;string&gt;</c>) so enum reordering never corrupts
/// persisted rows — parity <c>TaxonomyConcept.Kind</c>.</para>
/// </summary>
internal enum CompanyRegisterStatus
{
    /// <summary>SCB <c>Företagsstatus = 1</c> — active per the register's criteria.</summary>
    Active,

    /// <summary>
    /// SCB <c>Företagsstatus = 0</c> (never active) or <c>9</c> (no longer active), OR a row that
    /// vanished from a fresh full extract (the floor-gated vanish-sweep). Never hard-deleted —
    /// company_watches point at the org.nr and history must stay resolvable (ADR 0091).
    /// </summary>
    Deregistered,
}
