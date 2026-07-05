namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — one row of the local <c>company_register</c>: a denormalized replica of a
/// Swedish legal entity from SCB's company register. Modeled EXACTLY like <c>TaxonomyConcept</c>
/// (ADR 0043): an Infrastructure-internal read-model POCO, NOT a DDD aggregate and NOT on
/// <c>IAppDbContext</c> — accessed only via the concrete <c>AppDbContext.Set&lt;T&gt;()</c> inside
/// Infrastructure. ADR 0087 D2 already rejected a persisted <c>Company</c> aggregate ("a second
/// employer source-of-truth protecting zero invariant"); this replica protects no invariant either
/// — SCB is the authority, we mirror its extract (senior-cto-advisor 2026-07-04, Fork 1).
///
/// <para>
/// <b>Legal-entities-only (ADR 0091, GDPR):</b> a row is only ever persisted for a 10-digit
/// legal-entity org.nr. Sole traders (enskild firma, org.nr == personnummer) are filtered at the SCB
/// query (Juridisk form ≠ 10) and, defense-in-depth, dropped by the orchestrator's
/// <c>IsPersonnummerShaped</c> guard before they reach this table. The register therefore holds no
/// personnummer — HMAC/pepper (that is #544's company-watch domain) is N/A here.
/// </para>
///
/// <para>
/// Written exclusively via the batched raw-SQL <c>ON CONFLICT</c> upsert in
/// <c>ScbCompanyRegisterStore</c> (bypasses the EF change-tracker for the ~1M-row bulk path); the EF
/// mapping exists for schema generation (the migration) and read materialization
/// (<c>db.Set&lt;T&gt;()</c>).
/// </para>
/// </summary>
internal sealed class ScbCompanyRegisterEntry
{
    /// <summary>The 10-digit legal-entity org.nr (PK). Plaintext, no hyphen — parity
    /// <c>job_ads.organization_number</c> / <c>company_watches.organization_number</c> (ADR 0087
    /// D8: a DEK-encrypted column would break equality/IN matching; public legal-entity data).</summary>
    public required string OrganizationNumber { get; init; }

    /// <summary>The registered company name (SCB <c>Företagsnamn</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The 4-digit registered-seat municipality code (SCB <c>Säteskommun</c>).</summary>
    public required string SeatMunicipalityCode { get; init; }

    /// <summary>Human-readable seat municipality name, when known.</summary>
    public string? SeatMunicipalityName { get; init; }

    /// <summary>Up to five 5-digit SNI-2025 industry codes (SCB <c>Bransch1..5</c>), primary first.
    /// Postgres <c>text[]</c>. No GIN index in v1 — no query reads it until smart-bevakning
    /// (ADR 0091 / Fork 5).</summary>
    public required List<string> SniCodes { get; init; }

    /// <summary>True when the entity has a reklamspärr (SCB <c>Reklam</c> 21/22/23). Ingested per the
    /// signed SCB terms (god marknadsföringssed).</summary>
    public required bool HasAdvertisingBlock { get; init; }

    /// <summary>The raw SCB <c>Företagsstatus</c> code ("0"/"1"/"9"), preserved for fidelity.</summary>
    public string? ScbStatusRaw { get; init; }

    /// <summary>Coarse derived lifecycle status (see <see cref="CompanyRegisterStatus"/>).</summary>
    public required CompanyRegisterStatus Status { get; init; }

    /// <summary>When this row was last touched by a sync run (the vanish-sweep predicate). Managed by
    /// <c>ScbCompanyRegisterStore</c> on write (the raw-SQL param) and populated by EF on read — the
    /// orchestrator does not set it when building an entry to upsert.</summary>
    public DateTimeOffset SyncedAt { get; init; }

    /// <summary>When this row was first inserted. Managed by the store on write (kept unchanged on
    /// update) and populated by EF on read.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
