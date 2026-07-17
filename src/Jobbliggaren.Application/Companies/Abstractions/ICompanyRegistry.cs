using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.Companies.Abstractions;

/// <summary>
/// #454 (ADR 0088) — the national company-registry lookup port: org.nr → company identity, EVEN for
/// a company with zero ads in our feed (the local <c>job_ads</c> projection cannot name a 0-ad
/// company — that gap is the whole reason this port exists). SCB (Statistics Sweden, an EU public
/// authority) is the ONLY bound upstream source (ADR 0088 D1; a non-EU commercial register is a
/// rejected Art. 44 transfer). Port surface is BCL + Domain VO only (parity
/// <see cref="Matching.Abstractions.IMatchScorer"/>): implementations live in Infrastructure and
/// translate wire format — Application never sees an unsanitised payload.
///
/// <para>
/// <b>v1 providers (ADR 0088 D3 — Klas phasing 2026-07-02):</b> a deterministic Fake (dev/test) and
/// a Null provider (prod, always <see cref="CompanyRegistryStatus.Unavailable"/>) ship now; the real
/// SCB adapter targets SCB's NEW API (September 2026, API-key auth) as a follow-up and its first
/// real transmission is HARD-GATED on DPIA #456 + SCB terms review. A read-through Redis cache
/// decorates the inner provider (org.nr→name only, D8(a) public-data plaintext class).
/// </para>
///
/// <para>
/// <b>Personnummer guard (ADR 0087 D8(c), CLAUDE.md §5 highest-priority):</b> the caller
/// (<c>LookupCompanyQueryHandler</c>) REFUSES a personnummer-shaped org.nr BEFORE this port is
/// invoked — a potential personnummer is never transmitted to any registry, never cached, never
/// surfaced (ADR 0088 D4, security-bound; pinned by a transmission-fail-closed test). Implementations
/// may therefore assume a legal-entity-shaped input, but MUST NOT log the org.nr regardless (the
/// org.nr surfacing-guard log-scan covers them).
/// </para>
/// </summary>
public interface ICompanyRegistry
{
    /// <summary>
    /// Looks up <paramref name="organizationNumber"/> in the national register. Never throws for
    /// upstream unavailability — a downed/off source returns
    /// <see cref="CompanyRegistryStatus.Unavailable"/> (the never-500 civic-degradation channel,
    /// parity <c>RedisLandingStatsCache</c>), distinct from <see cref="CompanyRegistryStatus.NotFound"/>
    /// (a valid org.nr absent from the register — a legitimate answer, not an error).
    /// </summary>
    ValueTask<CompanyRegistryLookup> LookupAsync(
        OrganizationNumber organizationNumber, CancellationToken cancellationToken);
}

/// <summary>Outcome discriminator for <see cref="ICompanyRegistry.LookupAsync"/>.</summary>
public enum CompanyRegistryStatus
{
    /// <summary>The org.nr resolved to a registered entity — <see cref="CompanyRegistryLookup.Entry"/> is set.</summary>
    Found,

    /// <summary>Valid org.nr, but no such entity in the register (never treated as a validation error).</summary>
    NotFound,

    /// <summary>The registry source is down, rate-broken or not activated (Null provider). Never cached.</summary>
    Unavailable,
}

/// <summary>
/// Lookup result envelope. <see cref="Entry"/> is non-null exactly when
/// <see cref="Status"/> is <see cref="CompanyRegistryStatus.Found"/>.
/// </summary>
public sealed record CompanyRegistryLookup(CompanyRegistryStatus Status, CompanyRegistryEntry? Entry)
{
    public static CompanyRegistryLookup NotFound { get; } = new(CompanyRegistryStatus.NotFound, null);
    public static CompanyRegistryLookup Unavailable { get; } = new(CompanyRegistryStatus.Unavailable, null);
    public static CompanyRegistryLookup Found(CompanyRegistryEntry entry) => new(CompanyRegistryStatus.Found, entry);
}

/// <summary>
/// One registry hit. DELIBERATELY minimal in v1 (ADR 0088 D2 — data minimisation + the smallest
/// Fake-vs-real divergence surface + cacheability: the read-through cache stores ONLY org.nr→name
/// per the #454 issue binding, so any field beyond the name could not survive a cache hit): org.nr
/// + registered name. NO address, NO SNI code, NO size class, NO registration status — future
/// fields (incl. deregistered-status) are additive and bound to the SCB-activation PR where the
/// real API semantics are known. The raw org.nr stays INSIDE the Application boundary: the handler
/// masks it per D8(c) before it reaches any DTO (parity <c>IEmployerDisambiguationQuery</c>
/// returning raw for the handler to mask). Never logged.
/// </summary>
public sealed record CompanyRegistryEntry(
    string OrganizationNumber,
    string Name)
{
    /// <summary>
    /// REDACTED (#883). The compiler-generated <c>ToString()</c> prints every member, so a plain
    /// <c>{X}</c> MEL placeholder would write <see cref="OrganizationNumber"/> into a log — a sole
    /// proprietor's org.nr IS a personnummer (ADR 0087 D8(c); CLAUDE.md §5). "Never logged" is stated
    /// above; this override makes it structural rather than guard-dependent
    /// (<c>OrgNrRecordLoggingGuardTests</c>). <see cref="Name"/> is kept for debugging.
    /// </summary>
    public override string ToString() => $"CompanyRegistryEntry({Name}, org.nr redacted)";
}
