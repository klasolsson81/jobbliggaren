using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — the per-criterion materialisation STATE row
/// (<c>company_watch_criterion_materialisations</c>): what the last run concluded about this
/// criterion, and when.
///
/// <para>
/// <b>This table is what makes the degradation HONEST</b> (security-auditor Major 3: "a missing or
/// stale materialisation degrades honestly, never to a silent number"). Without it, "no member rows"
/// would be one symbol for three different facts — <i>never materialised</i>, <i>refused as too
/// broad</i>, and <i>materialised, genuinely zero matches</i> — and a reader would have to pick one.
/// Picking "zero" is the dishonest nought this repo has already shipped once and had to unpick
/// (<c>getRecentSearches</c> returning 0 rather than null, #1656). So the three facts get three
/// representations:
/// <list type="bullet">
///   <item><b>No row at all</b> = never materialised. The read side says it does not know; it must
///     not say zero. This is also the state a criterion is in between its creation and the next job
///     run, so it is the common case rather than an exotic one.</item>
///   <item><b><see cref="MaterialisationState.TooBroad"/></b> = refused by the breadth gate. The read
///     side renders a refusal — deterministic, and identical to what the detail page already answers
///     via <c>CriterionMatchingAds.SetTooLarge</c>.</item>
///   <item><b><see cref="MaterialisationState.Materialised"/></b> with
///     <see cref="MemberCount"/> = 0 = genuinely no matching company. An honest zero, and the ONLY
///     state in which a zero may be rendered.</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="MaterialisedAt"/> carries the AGE axis: a reader that wants to know how old the answer
/// is can ask, instead of assuming freshness. It is deliberately NOT the staleness GUARD — that is
/// <see cref="Application.CompanyWatches.Abstractions.CriteriaFingerprint"/>, for the reason its own docblock gives (a timestamp cannot tell
/// a predicate edit from a rename, and both bump the aggregate's <c>UpdatedAt</c>).
/// </para>
///
/// <para>
/// Infrastructure-internal for the same reason <see cref="CompanyWatchCriterionMember"/> is, and it is
/// the register's extension by the same argument: the state is a fact about resolving a predicate
/// against the register.
/// </para>
/// </summary>
internal sealed class CompanyWatchCriterionMaterialisation
{
    /// <summary>The criterion this state describes (PK and FK, <c>ON DELETE CASCADE</c>). One row per
    /// criterion at most — the state is the criterion's, not the run's. Typed as the Domain
    /// <see cref="CompanyWatchCriterionId"/> for the reason
    /// <see cref="CompanyWatchCriterionMember.CriterionId"/> gives: EF matches a relationship on the
    /// key's CLR type, so a bare <c>Guid</c> would leave the FK — and its cascade — uncreated.</summary>
    public required CompanyWatchCriterionId CriterionId { get; init; }

    /// <summary>What the last run concluded. Stored BY NAME (reorder-safe; parity
    /// <c>company_register.status</c> and <c>TaxonomyConcept.Kind</c>).</summary>
    public required MaterialisationState State { get; init; }

    /// <summary>
    /// How many members the last run wrote. Always 0 under
    /// <see cref="MaterialisationState.TooBroad"/> — a refused criterion has no members, and storing
    /// the true size of a set we refused to store would re-introduce the unbounded knowledge the gate
    /// exists to prevent (Art. 5(1)(c)), while also being a number no surface may render.
    /// </summary>
    public required int MemberCount { get; init; }

    /// <summary>
    /// Candidate org.nr this criterion's run dropped at the write boundary because they were
    /// personnummer-shaped (security-auditor Major 4). Expected 0 — <c>ScbLegalEntityFilter</c> keeps
    /// the register legal-entities-only at ingest (ADR 0091) — and stored anyway, because a guard
    /// whose firings are not counted cannot be told from a guard that never ran. That distinction is
    /// the whole reason this filter is applied here rather than inherited (#454).
    /// </summary>
    public required int ExcludedPersonnummerShaped { get; init; }

    /// <summary>When the last run wrote this row (from <c>IDateTimeProvider</c>) — the AGE axis, not
    /// the staleness guard (see the class docblock).</summary>
    public required DateTimeOffset MaterialisedAt { get; init; }

    /// <summary>
    /// #1681 part 2 — the digest of the PREDICATE this row's member set was computed from, so a read
    /// can refuse to answer with numbers belonging to a predicate the user has since edited. The
    /// argument, and why it is a fingerprint rather than a timestamp comparison or a copy of the
    /// codes, lives in one place: <see cref="Application.CompanyWatches.Abstractions.CriteriaFingerprint"/>.
    ///
    /// <para>
    /// Written on EVERY path, including <see cref="MaterialisationState.TooBroad"/>. A refused
    /// criterion has no members, but it still has a predicate — and the refusal itself must stop
    /// applying once that predicate changes, or a user who narrowed a too-broad watch would keep
    /// being told it is too broad until the next daily run.
    /// </para>
    /// </summary>
    public required string CriteriaFingerprint { get; init; }
}

/// <summary>
/// #1681 — the closed set of conclusions a materialisation run can reach about one criterion. There
/// is deliberately no <c>Pending</c> / <c>Unknown</c> member: "not yet materialised" is the ABSENCE of
/// a row, so a criterion cannot be in that state and carry a stale member set at the same time. A
/// third symbol would make that contradiction representable.
/// </summary>
internal enum MaterialisationState
{
    /// <summary>The company set fitted under the breadth gate and was written in full.</summary>
    Materialised = 0,

    /// <summary>The company set exceeded <see cref="CompanyWatchCriterionMember.MaxPerCriterion"/>.
    /// No members are stored; the surfaces render a refusal.</summary>
    TooBroad = 1,
}
