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
/// <b>Two methods, one predicate authority (CTO Fork G3, 2026-07-16).</b> This port once carried a
/// single method precisely because "a second public entry point is exactly the surface on which a
/// count predicate can silently drift from the page predicate". The magnitude count
/// (<see cref="CountMatchingCompaniesAsync"/>) is that second method — added HERE and not as its
/// own port so both methods share the implementation's single <c>FROM/WHERE</c> constant and
/// parameter-binding routine. Co-locating the predicate is the drift defense; a separate port would
/// re-create the very risk the one-method rule existed to prevent.
/// </para>
/// </summary>
public interface ICompanyWatchBrowseQuery
{
    /// <summary>
    /// Returns the page of ACTIVE register companies matching <paramref name="criteria"/>
    /// (<c>status = 'Active'</c> is unconditional — DPIA M-D6: a de-registered company is never
    /// surfaced), together with a SATURATING match count from a separate count query (CLAUDE.md §3.6).
    ///
    /// <para>
    /// <b><see cref="PagedResult{T}.TotalCount"/> is a PAGINATION QUANTITY, not a magnitude. Never
    /// render it as "N företag matchar."</b> It saturates at
    /// <see cref="CompanyBrowseCriteria.MaxServableRows"/> — see that constant for why the cap is a
    /// correctness requirement rather than a performance tweak, and
    /// <c>docs/reviews/2026-07-13-560-pr2-browse-perf-measurement.md</c> for the measured numbers. A
    /// surface that wants to say "108 244 företag matchar" (or honestly, "10 000+") needs its OWN
    /// count with its OWN product-chosen ceiling; it must not read this one.
    /// </para>
    ///
    /// <para>
    /// This also REVOKES the "count-only caller passes <c>PageSize: 1</c> and reads
    /// <c>TotalCount</c>" mechanism the architect bound in Q2 (senior-cto-advisor 2026-07-13). Under
    /// the cap that call would report <c>MaxPage × 1 = 100</c> for a criterion matching 108 244
    /// companies — a lie generator. Whether PR-3's picker preview gets a second port method or its own
    /// port is a PR-3 decision; it is deliberately not bound here.
    /// </para>
    /// </summary>
    ValueTask<PagedResult<CompanyBrowseResult>> BrowseAsync(
        CompanyBrowseCriteria criteria, CancellationToken cancellationToken);

    /// <summary>
    /// The MAGNITUDE count (CTO Fork G3, 2026-07-16): "roughly how many companies match this
    /// predicate" — the number a headline or the picker's live preview may honestly render, capped
    /// at <paramref name="ceiling"/> (a PRODUCT ceiling, Klas 2026-07-16: 10 000 — carried by
    /// <c>CriterionMatchMagnitudeDto.Ceiling</c>, never hardcoded at call sites). Returns
    /// <c>min(true count, ceiling)</c>; a return value equal to <paramref name="ceiling"/> means
    /// SATURATED and the copy must say "10 000+", never the bare number.
    ///
    /// <para>
    /// <b>This is a DIFFERENT question from <see cref="PagedResult{T}.TotalCount"/></b> — that one
    /// is a pagination quantity saturating at <see cref="CompanyBrowseCriteria.MaxServableRows"/>
    /// (a correctness cap: <c>TotalPages ≤ MaxPage</c> by construction) and must never be rendered
    /// as a magnitude. Two questions, two ceilings, one shared predicate (see the interface doc).
    /// </para>
    ///
    /// <para>
    /// Takes the Domain VO (not a criterion id) for the same reason <see cref="BrowseAsync"/> does:
    /// the picker's live preview counts an UNSAVED criterion.
    /// </para>
    /// </summary>
    ValueTask<int> CountMatchingCompaniesAsync(
        CompanyWatchCriteriaSpec criteria, int ceiling, CancellationToken cancellationToken);
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
    int PageSize)
{
    /// <summary>
    /// The bounds are enforced HERE, in the port's input, and not only in
    /// <c>BrowseCompaniesQueryValidator</c> (security-auditor Minor, 2026-07-13). A validator only
    /// guards the callers that go through the Mediator pipeline — and this interface's own doc
    /// anticipates one that will not: PR-3's picker preview of an UNSAVED criterion. That is exactly
    /// where a validator-only cap silently disappears, taking the unbounded-OFFSET DoS surface and the
    /// <c>TotalPages ≤ MaxPage</c> guarantee with it. An invariant that holds "as long as you came in
    /// the front door" is not an invariant.
    /// </summary>
    public CompanyWatchCriteriaSpec Criteria { get; } =
        Criteria ?? throw new ArgumentNullException(nameof(Criteria));

    public int Page { get; } = Page is >= 1 and <= MaxPage
        ? Page
        : throw new ArgumentOutOfRangeException(
            nameof(Page), Page, $"Page måste vara mellan 1 och {MaxPage}.");

    public int PageSize { get; } = PageSize is >= 1 and <= MaxPageSize
        ? PageSize
        : throw new ArgumentOutOfRangeException(
            nameof(PageSize), PageSize, $"PageSize måste vara mellan 1 och {MaxPageSize}.");

    /// <summary>House parity (<c>GetApplicationsQueryValidator</c>).</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Deep-offset ceiling — a DELIBERATE divergence from <c>GetApplicationsQueryValidator</c>, which
    /// caps <c>PageSize</c> but leaves <c>Page</c> unbounded. That hole is harmless there
    /// (<c>applications</c> is per-user and small). It is NOT harmless against a 1,17M-row register:
    /// an <c>OFFSET 5_000_000</c> still makes Postgres produce AND SORT every preceding row before
    /// discarding it. §5 already forbids "unpaginated list fetches"; an unbounded OFFSET is the same
    /// sin with a LIMIT on it.
    /// </summary>
    public const int MaxPage = 100;

    /// <summary>
    /// The most rows this surface can EVER serve — and therefore the ceiling the count query is capped
    /// at (<c>LIMIT MaxPage * pageSize</c>).
    ///
    /// <para>
    /// <b>This cap is a CORRECTNESS requirement, not a performance tweak</b> (senior-cto-advisor
    /// 2026-07-13). <see cref="PagedResult{T}.TotalPages"/> is <c>ceil(TotalCount / PageSize)</c>, and
    /// <see cref="MaxPage"/> makes any page beyond 100 a 400. With an UNCAPPED count and a bound-legal
    /// broad criterion (1000 SNI × 290 kommuner matches all 1 170 000 rows), the pager would advertise
    /// <c>58 500</c> pages of which <c>100</c> are fetchable — an authoritative number the system that
    /// emitted it does not back. That is the same failure shape as the vacuous <c>JobAd.DeletedAt</c>
    /// filter (#805-3): not slow, FALSE. Capping the count at <c>MaxPage × PageSize</c> makes
    /// <c>TotalPages ≤ MaxPage</c> true BY CONSTRUCTION — the pager cannot advertise a page the
    /// validator rejects.
    /// </para>
    ///
    /// <para>
    /// The cap is DERIVED, never a hand-picked number: the page cap and the count cap are the same
    /// knowledge piece ("how many rows will this surface ever serve"), so they are single-sourced. A
    /// standalone <c>10001</c> sitting next to an independent <c>MaxPage</c> is duplicated knowledge
    /// that drifts apart. Measured cost of the capped count: ~78 ms even in the worst case (vs 3 147 ms
    /// exact).
    /// </para>
    /// </summary>
    public static int MaxServableRows(int pageSize) => MaxPage * pageSize;
}

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
    IReadOnlyList<string> SniCodes)
{
    /// <summary>
    /// REDACTED (#883). The compiler-generated <c>ToString()</c> prints every member; this row carries
    /// the RAW <see cref="OrganizationNumber"/> (a possible sole-prop personnummer, ADR 0087 D8(c);
    /// CLAUDE.md §5) that must not leave the Application boundary un-masked — and a plain <c>{X}</c> MEL
    /// placeholder is exactly such an exit. Overriding makes "never logged" structural
    /// (<c>OrgNrRecordLoggingGuardTests</c>). <see cref="Name"/> is a legal-entity name (ADR 0091), kept
    /// for debugging.
    /// </summary>
    public override string ToString() => $"CompanyBrowseResult({Name}, org.nr redacted)";
}
