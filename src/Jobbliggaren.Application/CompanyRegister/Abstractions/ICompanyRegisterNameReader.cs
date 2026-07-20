namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #994 (ADR 0087 D2/D3, ADR 0091) — resolves the registered company NAME for a set of
/// legal-entity org.nrs from the local <c>company_register</c> replica, in ONE round-trip.
///
/// <para>
/// The company-watch list resolves <c>company_name</c> at READ from public <c>job_ads</c>
/// (ADR 0087 D3 — never a denormalised snapshot). A followed company with ZERO current ads
/// therefore has no name to resolve, so its row rendered the "Företagets namn är inte tillgängligt"
/// fallback (#994) even though the register HAS the name (it is shown in the search results). This
/// port is the SECOND public read-model the list falls back to — still a read-time projection, no
/// stored snapshot, so ADR 0087 D2/D3 hold: the name stays derived, now from the register when
/// <c>job_ads</c> carries none.
/// </para>
///
/// <para>
/// <b>Same firewall as the sibling register ports (ADR 0091/0043, DPIA C-D4/M-C5):</b> the
/// register is Infrastructure-internal — NEVER a <c>DbSet</c> on <c>IAppDbContext</c> — so a
/// handler can reach it only through a port and can never join it against personnummer-lookup
/// output. The keys handed in are the caller's OWN resolved plaintext org.nrs (the user's followed
/// employers), NOT rows selected out of the register, and the return carries only the public
/// legal-entity name. The register is legal-entities-only (ADR 0091: no personnummer, no sole
/// traders), so even a personnummer-shaped key would resolve to nothing — AND the caller excludes
/// such values before calling (a pnr-shaped watch is named by job_ads or resolves to null, so it is
/// filtered out of the org.nr set; see ListCompanyWatchesQueryHandler), parity the list's D8(c) mask.
/// </para>
/// </summary>
public interface ICompanyRegisterNameReader
{
    /// <summary>
    /// Returns a map <c>organization_number → company_name</c> for those of
    /// <paramref name="organizationNumbers"/> that exist in the register. An org.nr ABSENT from the
    /// map has no register row (never ingested, or a sole trader the register excludes) — the caller
    /// keeps its existing null/fallback.
    ///
    /// <para>
    /// Lifecycle status is deliberately NOT filtered: a followed company that has since
    /// deregistered keeps a resolvable name — the register retains deregistered rows EXACTLY so
    /// <c>company_watches</c> history stays resolvable (ADR 0091), and the name is public
    /// legal-entity data. This port resolves an ALREADY-followed identity, not a discoverability
    /// surface, which is why it does not carry the <c>status = 'Active'</c> gate the search/browse
    /// ports do (DPIA M-D6 governs what may be SURFACED as followable, not the name of a company
    /// the user already follows).
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetNamesByOrganizationNumbersAsync(
        IReadOnlyList<string> organizationNumbers, CancellationToken cancellationToken);
}
