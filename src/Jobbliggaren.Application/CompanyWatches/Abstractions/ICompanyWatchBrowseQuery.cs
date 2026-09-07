using Jobbliggaren.Application.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;

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
/// <b>The COMPANY half takes the Domain VO, not a criterion id.</b> The handler loads the user's
/// <c>CompanyWatchCriterion</c> (owner-scoped) and passes its <see cref="CompanyWatchCriteriaSpec"/>
/// here. Binding those methods to a persisted id would weld them to saved criteria and force a second
/// port the day PR-3's picker wants a live "412 företag matchar" preview of an UNSAVED criterion —
/// which this shape serves unchanged. <c>PreviewCriterionMatchMagnitudeQueryHandler</c> is that
/// caller today, so this is a live requirement rather than a preserved option.
/// </para>
///
/// <para>
/// <b>The AD half takes a criterion id instead, and the port therefore carries TWO key types — which
/// is correct, not a smell</b> (#1681 part 2, ADR 0139; senior-cto-advisor 2026-09-06). The two halves
/// answer different questions. The company half asks <i>"what does this PREDICATE match, live"</i>,
/// which is meaningful for an unsaved criterion. The ad half asks <i>"what did we MATERIALISE for this
/// SAVED criterion"</i> — a question that has no answer at all for a predicate nobody saved, because
/// the whole point of ADR 0139 is that the predicate is resolved to a company set OUT of the request
/// path. A spec-keyed materialised read would have to resolve the predicate live (defeating the ADR)
/// or invent one. So the key type is not a stylistic difference between neighbouring methods; it is
/// what each method is about.
/// </para>
///
/// <para>
/// <b>Every ad-side method also takes a <see cref="CriteriaFingerprint"/>, and that is what makes the
/// staleness guard unforgettable</b> (#1681 part 2). The question these methods answer is not "what
/// did we materialise for criterion X" but "what did we materialise for criterion X <i>as it is
/// now</i>" — because a user who edits a criterion's codes leaves a member set behind that is exact
/// for a predicate they no longer have. Passing the predicate's fingerprint IN means a caller cannot
/// ask the question without saying which predicate it is asking about; a mismatch answers
/// <see cref="CriterionMaterialisationState.NotMaterialised"/>. Had the port looked the fingerprint up
/// itself, the guard would have been a rule every future caller has to remember, which is the shape
/// this port's own paging-bounds docblock rejects one paragraph below.
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
    /// <summary>
    /// #1559, re-keyed to the materialised member set by #1681 part 2 (ADR 0139) — the ACTIVE public
    /// job ads posted by the companies this criterion matches, newest first, as their <c>job_ads</c>
    /// ids. The caller loads and projects the ads themselves through <c>IAppDbContext</c> (which DOES
    /// carry <c>JobAds</c>); this port only answers the half that needs the materialised set.
    ///
    /// <para>
    /// <b>It reads <c>members ⋈ job_ads</c>, never the register</b> (ADR 0139). The criterion's company
    /// set was resolved out of the request path by the materialisation job, so what is left here is a
    /// bounded index join against a pre-computed set. The register scan is GONE from this path and that
    /// is measured rather than asserted: the plan carries no <c>company_register</c> node at all
    /// (<c>docs/reviews/2026-09-06-1681-part2-read-form-measurement.md</c>, Result 3, and pinned by
    /// <c>CompanyWatchBrowseQueryPlanTests</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Ids, not ad rows — and that is the firewall doing its job.</b> Projecting the ad columns
    /// here would put the job-ad read shape in the register's Infrastructure file, a second home
    /// beside the Application-side projection every other ad surface uses. The join is the only thing
    /// that cannot be done anywhere else, so the join is the only thing this method does.
    /// </para>
    ///
    /// <para>
    /// <b>The order is TOTAL and is part of the contract</b>: <c>published_at DESC, id</c>, and the
    /// caller re-sequences its loaded rows by THIS array rather than re-deriving the order. A non-total
    /// order plus OFFSET can drop or duplicate rows across pages (the same reason
    /// <see cref="BrowseAsync"/> appends the PK), and a caller ordering differently would paginate
    /// against one order while reading another.
    /// </para>
    ///
    /// <para>
    /// <see cref="PagedResult{T}.TotalCount"/> is a PAGINATION QUANTITY here too, capped at
    /// <see cref="CompanyBrowseCriteria.MaxServableRows"/> — never render it as "N annonser". The
    /// honest headline number is <see cref="CountActiveAdsAsync"/>, with its own ceiling.
    /// </para>
    /// </summary>
    ValueTask<MaterialisedAdPage> BrowseAdIdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint,
        int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// #1559, re-keyed by #1681 part 2 — the MAGNITUDE of the criterion's ACTIVE ad set: "how many
    /// active ads do the companies this criterion matches have right now", capped at
    /// <paramref name="ceiling"/>. A <see cref="MaterialisedAdCount.Count"/> equal to
    /// <paramref name="ceiling"/> means SATURATED and the copy must say so, never the bare number
    /// (#859).
    ///
    /// <para>
    /// <b>The ceiling still earns its place after materialisation, and that was measured rather than
    /// assumed.</b> The breadth gate bounds COMPANIES, not ads, so a bound-legal criterion can still
    /// carry several times this ceiling in active ads and saturation stays a reachable state rather
    /// than dead copy. The measured figure lives in ONE place —
    /// <see cref="GetCriterionAdMagnitude.CriterionAdMagnitudeDto.Ceiling"/>, the constant it is
    /// evidence for — because a measured number with two homes drifts at the next re-measurement.
    /// </para>
    ///
    /// <para>
    /// It does NOT share the company magnitude's ceiling — that one is a product answer to "how many
    /// COMPANIES", and binding the ad question to it would make one constant carry two meanings.
    /// </para>
    /// </summary>
    ValueTask<MaterialisedAdCount> CountActiveAdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint, int ceiling,
        CancellationToken cancellationToken);

    /// <summary>
    /// #1656 (b), re-keyed by #1681 part 2 — the criterion's ACTIVE ad ids as a WHOLE ORDERED SET
    /// (same order as <see cref="BrowseAdIdsAsync"/>, same materialised set), or a REFUSAL when that
    /// set is larger than <paramref name="maxSetSize"/>.
    ///
    /// <para>
    /// <b>It REFUSES; it never truncates, and that distinction is the whole method</b>
    /// (senior-cto-advisor 2026-09-05, ADR 0120 clause 5). The caller grades this set to answer "how
    /// many of these ads match ME". A count over a TRUNCATED input is a FLOOR wearing a magnitude's
    /// clothes: grade a prefix of a larger set, get 300, and "300" is false while "300+" reads as
    /// approximately 300. That is why the bound cannot sit on the OUTPUT the way
    /// <see cref="CountActiveAdsAsync"/>'s ceiling does — a saturating count is honest because
    /// "&gt;= ceiling" is TRUE, and a truncated count has no such true reading.
    /// </para>
    ///
    /// <para>
    /// So the refusal is structural rather than policed: the statement selects
    /// <c>LIMIT maxSetSize + 1</c> and refuses the moment the extra row exists. <b>No code path can
    /// return a prefix</b>, which is what makes the hazard unrepresentable instead of a rule somebody
    /// has to remember. A refusal means "too broad to answer", NEVER "nothing matched" — an empty set
    /// is an empty list, and the two are different members of
    /// <see cref="MaterialisedAdIds"/>.
    /// </para>
    ///
    /// <para>
    /// Ids and nothing else, for the reason <see cref="BrowseAdIdsAsync"/> gives. No org.nr crosses
    /// the Application boundary on this path (ADR 0087 D8(c) has nothing to mask — an ad id is an
    /// opaque Guid over public Platsbanken data).
    /// </para>
    ///
    /// <para>
    /// Unpaged BY DESIGN, and that is not the "unpaginated list fetch" §5 forbids: the set is bounded
    /// by <paramref name="maxSetSize"/> at the database, and a PAGE could not answer the question at
    /// all — a count over one page is about the page, not the watch (ADR 0120).
    /// </para>
    /// </summary>
    ValueTask<MaterialisedAdIds> ListActiveAdIdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint, int maxSetSize,
        CancellationToken cancellationToken);
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
    /// (<c>applications</c> is per-user and small). It is NOT harmless against the register:
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
    /// <see cref="MaxPage"/> makes any page beyond 100 a 400. With an UNCAPPED count, a bound-legal
    /// broad criterion matches far more rows than this surface can serve, so the pager would advertise
    /// many times the <c>100</c> pages that are fetchable — an authoritative number the system that
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

/// <summary>
/// #1681 part 2 (ADR 0139) — what a materialised read found out about the criterion itself, BEFORE
/// any number is computed. Three states, and the read side must not collapse any two of them.
///
/// <para>
/// <b>This mirrors the three facts part 1 made representable</b>
/// (<c>CompanyWatchCriterionMaterialisation</c>'s docblock owns the argument and it is not restated
/// here): a criterion with no state row has never been materialised, a
/// <c>TooBroad</c> one was refused by the breadth gate, and a <c>Materialised</c> one has a real
/// answer — including a real ZERO. What this enum adds is that the distinction now crosses the
/// Application boundary, because a surface that cannot tell ignorance from refusal from zero will
/// render one of them as the other.
/// </para>
///
/// <para>
/// <b>There is deliberately no fourth member for "refused because the AD set was too large".</b> That
/// refusal is not a property of the criterion's materialisation — it belongs to the individual
/// question's own bound (<c>ListActiveAdIdsAsync</c>'s <c>maxSetSize</c>) and is carried by
/// <see cref="MaterialisedAdIds.Refused"/>. Folding the two into one symbol would make a criterion
/// that WAS materialised indistinguishable from one that was not.
/// </para>
/// </summary>
public enum CriterionMaterialisationState
{
    /// <summary>The company set fitted under the breadth gate and was written in full. The only state
    /// in which a number — zero included — may be rendered.</summary>
    Materialised = 0,

    /// <summary>The company set exceeded the breadth gate, so nothing was materialised and no number
    /// exists. The surfaces render "för bred". Never a zero.</summary>
    TooBroad = 1,

    /// <summary>
    /// No materialisation has run for this criterion yet, so the answer is UNKNOWN. Never a zero, and
    /// never "för bred" either — the two say different things and offer the user different actions.
    ///
    /// <para>
    /// <b>This is the common state, not an exotic one</b>: it is where every criterion sits between
    /// its creation and the next materialisation run.
    /// </para>
    /// </summary>
    NotMaterialised = 2,
}

/// <summary>
/// #1681 part 2 — the criterion's ACTIVE ad magnitude, or the reason there is no number.
/// <see cref="Count"/> is non-null exactly when <see cref="State"/> is
/// <see cref="CriterionMaterialisationState.Materialised"/>; the constructor rejects every other
/// combination rather than leaving it to reviewers, the same way <c>MyMatchingAdCountDto</c> does.
/// </summary>
public sealed record MaterialisedAdCount(
    CriterionMaterialisationState State, int? Count, bool Saturated)
{
    public int? Count { get; } =
        (State == CriterionMaterialisationState.Materialised) == (Count is not null)
            ? Count
            : throw new ArgumentException(
                "Ett tal finns exakt när materialiseringen är gjord: ett tal utan Materialised vore "
                + "en siffra vi inte har täckning för, och Materialised utan tal vore en mätning vi "
                + "kastade bort.",
                nameof(Count));

    /// <summary>A real magnitude; <c>0</c> is a real answer.</summary>
    public static MaterialisedAdCount Counted(int count, bool saturated) =>
        new(CriterionMaterialisationState.Materialised, count, saturated);

    /// <summary>Refused by the breadth gate — "för bred", never a zero.</summary>
    public static MaterialisedAdCount TooBroad { get; } =
        new(CriterionMaterialisationState.TooBroad, null, false);

    /// <summary>Not materialised yet — unknown, and that is not a zero either.</summary>
    public static MaterialisedAdCount NotMaterialised { get; } =
        new(CriterionMaterialisationState.NotMaterialised, null, false);
}

/// <summary>
/// #1681 part 2 — the criterion's whole ordered ACTIVE ad-id set, or the reason there is none.
///
/// <para>
/// <b><see cref="Refused"/> and a <see cref="State"/> of
/// <see cref="CriterionMaterialisationState.TooBroad"/> are two different facts that happen to render
/// the same sentence.</b> The first says the criterion's AD set is larger than this question's bound;
/// the second says the criterion's COMPANY set was too large to materialise at all. Both are "för
/// bred" to a user, and keeping them apart here is what stops the read side inferring one from the
/// other — an empty <see cref="Ids"/> under <c>Materialised</c> is neither: it is an honest zero.
/// </para>
/// </summary>
public sealed record MaterialisedAdIds(
    CriterionMaterialisationState State, IReadOnlyList<JobAdId>? Ids, bool Refused)
{
    public IReadOnlyList<JobAdId>? Ids { get; } =
        (State == CriterionMaterialisationState.Materialised && !Refused) == (Ids is not null)
            ? Ids
            : throw new ArgumentException(
                "En id-mängd finns exakt när materialiseringen är gjord OCH mängden ryms: ett "
                + "prefix av en vägrad mängd är ett golv utgivet för en exakt siffra.",
                nameof(Ids));

    /// <summary>The whole set, in the port's published order. An empty list is a real answer.</summary>
    public static MaterialisedAdIds Resolved(IReadOnlyList<JobAdId> ids) =>
        new(CriterionMaterialisationState.Materialised, ids, Refused: false);

    /// <summary>The set exceeded this question's own bound — refused, never truncated.</summary>
    public static MaterialisedAdIds TooManyAds { get; } =
        new(CriterionMaterialisationState.Materialised, null, Refused: true);

    /// <summary>The criterion was refused by the breadth gate; there is no member set to read.</summary>
    public static MaterialisedAdIds TooBroad { get; } =
        new(CriterionMaterialisationState.TooBroad, null, Refused: false);

    /// <summary>Not materialised yet — unknown.</summary>
    public static MaterialisedAdIds NotMaterialised { get; } =
        new(CriterionMaterialisationState.NotMaterialised, null, Refused: false);
}

/// <summary>
/// #1681 part 2 — one page of the criterion's ordered ACTIVE ad ids, or the reason there is no page.
/// <see cref="Page"/> is non-null exactly under
/// <see cref="CriterionMaterialisationState.Materialised"/>.
///
/// <para>
/// An EMPTY page under <c>Materialised</c> means the criterion matches no active ad right now, which
/// is a real answer and renders as the ordinary empty state. The other two states must NOT render
/// that empty state: "we have not counted this yet" and "this watch is too broad" are not "nothing
/// found", and showing an empty ad list for either is the false zero one level up from the count.
/// </para>
/// </summary>
public sealed record MaterialisedAdPage(
    CriterionMaterialisationState State, PagedResult<JobAdId>? Page)
{
    public PagedResult<JobAdId>? Page { get; } =
        (State == CriterionMaterialisationState.Materialised) == (Page is not null)
            ? Page
            : throw new ArgumentException(
                "En sida finns exakt när materialiseringen är gjord.", nameof(Page));

    public static MaterialisedAdPage Resolved(PagedResult<JobAdId> page) =>
        new(CriterionMaterialisationState.Materialised, page);

    public static MaterialisedAdPage TooBroad { get; } =
        new(CriterionMaterialisationState.TooBroad, null);

    public static MaterialisedAdPage NotMaterialised { get; } =
        new(CriterionMaterialisationState.NotMaterialised, null);
}
