namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #560 (ADR 0091) — one sanitized legal-entity row yielded by
/// <see cref="IScbCompanyRegisterSource"/>. This is the anti-corruption boundary for the SCB
/// population channel (parity <c>JobAdImportItem</c> for the JobTech ingest, ADR 0032): the raw
/// SCB wire format (the <c>hamtaforetag</c> JSON envelope) is Infrastructure-internal and never
/// crosses into Application — the client translates a wire row into this neutral record.
///
/// <para>
/// <b>Legal-entities-only (the register's GDPR foundation, ADR 0091 / CLAUDE.md §5):</b> a record
/// is only ever produced for a 10-digit legal-entity org.nr. Sole traders (enskild firma, whose
/// org.nr equals the owner's personnummer) are excluded at the SCB query (Juridisk form ≠ 10) AND,
/// defense-in-depth, the orchestrator drops any row whose <see cref="OrganizationNumber"/> is
/// personnummer-shaped before it is persisted. This record therefore carries NO personnummer, and
/// the org.nr is never logged (the org.nr surfacing-guard log-scan covers the client + orchestrator).
/// </para>
/// </summary>
/// <param name="OrganizationNumber">The 10-digit legal-entity org.nr (SCB <c>OrgNr</c>, no hyphen,
/// no 16-prefix — SCB's row output gives the 10-digit form directly for legal entities).</param>
/// <param name="Name">The registered company name (SCB <c>Företagsnamn</c>).</param>
/// <param name="SeatMunicipalityCode">The 4-digit registered-seat municipality code (SCB
/// <c>Säteskommun</c>). The workplace municipality (<c>Kommun</c>) needs the AE layout, deferred to
/// v2.</param>
/// <param name="SeatMunicipalityName">Human-readable seat municipality name, when the wire row
/// carries it; null otherwise.</param>
/// <param name="SniCodes">Up to five 5-digit SNI-2025 industry codes (SCB <c>Bransch1..5</c>),
/// primary first. May be empty.</param>
/// <param name="HasAdvertisingBlock">True when the entity has a reklamspärr (SCB <c>Reklam</c> code
/// 21/22/23 — has opted out of marketing). Ingested per the signed SCB terms (god
/// marknadsföringssed) to future-proof any list-export feature.</param>
/// <param name="RawStatusCode">The raw SCB <c>Företagsstatus</c> code ("0" never active, "1" active,
/// "9" not active) — preserved verbatim so the orchestrator can both derive the coarse
/// Active/Deregistered status and keep full fidelity.</param>
public sealed record ScbCompanyRecord(
    string OrganizationNumber,
    string Name,
    string SeatMunicipalityCode,
    string? SeatMunicipalityName,
    IReadOnlyList<string> SniCodes,
    bool HasAdvertisingBlock,
    string RawStatusCode)
{
    /// <summary>
    /// REDACTED (#883). A record's compiler-generated <c>ToString()</c> prints every public member,
    /// so a plain <c>{X}</c> MEL placeholder would write <see cref="OrganizationNumber"/> into the log
    /// — and a sole proprietor's org.nr IS a personnummer, in plaintext (ADR 0087 D8(c); CLAUDE.md §5,
    /// highest priority). Overriding makes the leak structurally impossible rather than guard-dependent
    /// (parity <c>JobAdFacets</c> / <c>JobAdImportItem</c>, pinned by <c>OrgNrRecordLoggingGuardTests</c>).
    /// <see cref="Name"/> is a legal-entity name (ADR 0091 excludes sole traders), safe to keep for
    /// debugging.
    /// </summary>
    public override string ToString() => $"ScbCompanyRecord({Name}, org.nr redacted)";
}
