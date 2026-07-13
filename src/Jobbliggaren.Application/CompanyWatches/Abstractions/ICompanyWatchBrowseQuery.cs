using Jobbliggaren.Application.Common;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// #560 (senior-cto-advisor Fork A1/B1, 2026-07-12) — the criteria browse read-path: "which ACTIVE
/// companies in the local SCB register match this criterion?". The port exists to keep the register
/// OFF <c>IAppDbContext</c> (DPIA C-D4 / M-C5 firewall): <c>company_register</c> is an
/// Infrastructure-internal read-model replica, so no handler holding the Application DbContext port
/// can ever join it against personnummer-lookup output. The firewall is a build gate, not a
/// convention — <c>ScbCompanyRegisterLayerTests.IAppDbContext_exposes_only_Domain_types</c> is
/// fail-closed.
///
/// <para>
/// <b>Why the implementation is raw SQL and not LINQ</b> (dotnet-architect Q5, PR-1 → PR-2 handover):
/// the predicate's SNI half is a Postgres ARRAY-OVERLAP (<c>sni_codes &amp;&amp; @sni</c>), and that is
/// the ONLY shape <c>ix_company_register_sni_codes_gin</c> can serve. Npgsql does NOT reliably
/// translate LINQ to <c>&amp;&amp;</c>: the natural-looking
/// <c>.Where(c =&gt; c.SniCodes.Any(s =&gt; userSni.Contains(s)))</c> compiles to an <c>unnest</c>
/// subquery that CANNOT use the GIN index — which would make PR-1's index pure cosmetics while every
/// test still passed. That is the vacuous-guarantee class this codebase has already shipped twice
/// (#805-3, #842), so the shape is pinned by an EXPLAIN test asserting the GIN index by NAME.
/// </para>
///
/// <para>
/// <b>Takes the Domain VO, not a criterion id.</b> The handler loads the user's
/// <c>CompanyWatchCriterion</c> (owner-scoped) and passes its <see cref="CompanyWatchCriteriaSpec"/>
/// here. Binding the port to a persisted id would weld it to saved criteria and force a second port
/// the day PR-3's picker wants a live "412 företag matchar" preview of an UNSAVED criterion — which
/// this shape serves unchanged.
/// </para>
///
/// <para>
/// <b>One method, deliberately.</b> No public <c>CountAsync</c> beside <see cref="BrowseAsync"/>:
/// <see cref="PagedResult{T}"/> already carries <c>TotalCount</c>, and a second public entry point is
/// exactly the surface on which a count predicate can silently drift from the page predicate. A
/// count-only caller passes <c>PageSize: 1</c> and reads <c>TotalCount</c>.
/// </para>
/// </summary>
public interface ICompanyWatchBrowseQuery
{
    /// <summary>
    /// Returns the page of ACTIVE register companies matching <paramref name="criteria"/>
    /// (<c>status = 'Active'</c> is unconditional — DPIA M-D6: a de-registered company is never
    /// surfaced), together with the total match count from a separate count query (CLAUDE.md §3.6).
    /// </summary>
    ValueTask<PagedResult<CompanyBrowseResult>> BrowseAsync(
        CompanyBrowseCriteria criteria, CancellationToken cancellationToken);
}

/// <summary>
/// Browse input: the Domain predicate + transport paging. Paging is deliberately NOT a Domain concept
/// (it never enters <see cref="CompanyWatchCriteriaSpec"/>), and the predicate is deliberately NOT
/// two loose string lists — a <c>BrowseAsync(spec, int, int, ct)</c> signature is the primitive
/// obsession / argument-swap surface §5 forbids.
/// </summary>
public sealed record CompanyBrowseCriteria(
    CompanyWatchCriteriaSpec Criteria,
    int Page,
    int PageSize);

/// <summary>
/// One matched register company as the port returns it — the RAW row, deliberately NOT a
/// <c>*Dto</c>.
///
/// <para>
/// <b><see cref="OrganizationNumber"/> is raw and must not leave the Application boundary in this
/// form.</b> The house rule (see <c>ICompanyRegistry</c>) is that the raw org.nr stays INSIDE
/// Application and the HANDLER masks it before it reaches any DTO: an org.nr can be a sole trader's
/// personnummer, and the personnummer guard is §5's highest priority. The register is
/// legal-entities-only by ADR 0091, but that is an INGEST-time invariant in a different subsystem —
/// resting a personnummer exposure on it is exactly what the repo declined to do for
/// <c>CompanyLookupDto</c> (#454). <c>CompanyBrowseDto</c> therefore nulls + flags, and this record
/// is the un-masked shape it is mapped FROM.
/// </para>
///
/// <para>
/// <b>No advertising-block member</b> (DPIA C-D3): the reklamspärr flag stays internal and is never
/// surfaced. It is also not a FILTER — a company with a reklamspärr IS returned (a jobseeker's
/// spontaneous application is not direct marketing; the E1 reading is ratified and scoped to exactly
/// that). The SQL does not even SELECT the column: what is never fetched cannot leak.
/// </para>
/// </summary>
public sealed record CompanyBrowseResult(
    string OrganizationNumber,
    string Name,
    // SCB 4-digit kommun code — a STRING with a load-bearing leading zero ("0180" = Stockholm).
    // Never parse it to int: 180 matches nothing in sate_kommun_code.
    string SeatMunicipalityCode,
    string? SeatMunicipalityName,
    IReadOnlyList<string> SniCodes);
