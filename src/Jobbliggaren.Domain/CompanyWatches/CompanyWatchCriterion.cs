using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// #560 Fork A1 (senior-cto-advisor 2026-07-12) — a user's CRITERIA-based company watch: a
/// predicate (SNI industry codes ∧ SCB seat-municipality codes) evaluated as a query against the
/// local SCB company register, for DISCOVERING employers the user does not yet know of
/// (spontaneous applications).
///
/// <para>
/// <b>Strictly separate from <see cref="CompanyWatch"/> (the A1 seal, re-affirmed by ADR 0105
/// RF-1).</b> <see cref="CompanyWatch"/> follows ONE known employer by org.nr; this aggregate
/// carries no org.nr at all and is never expanded into per-company rows — the epic's binding
/// constraint ("the scan-set explodes"). The UX presents both under one "bevakningar" umbrella;
/// the DOMAIN keeps them apart (ubiquitous language without model unification).
/// </para>
///
/// <para>
/// <b>Identity key: <see cref="UserId"/> (Guid), NOT JobSeekerId</b> — mirrors
/// <see cref="CompanyWatch"/> (ADR 0087 D3: cohesion follows the consumer). The browse read
/// (PR-2) is UserId-scoped via <c>ICurrentUser</c>, so keying here by UserId avoids a
/// <c>JobSeeker.UserId</c> bridge hop on every read.
/// </para>
///
/// <para>
/// <b>The criterion is personal data ABOUT THE USER</b> (which industries and towns they are
/// job-hunting in — profiling-adjacent), not about the companies (legal entities, not PII —
/// DPIA Part C). Stored and browsed under Art. 6(1)(b) (the service the user actively asked
/// for); the proactive e-mail notification (deferred, RF-9=9C) is what would need separate
/// Art. 6(1)(a) consent. It is FK-less by UserId (ADR 0011 soft-reference) → it MUST be deleted
/// explicitly in the Art. 17 cascade (<c>AccountHardDeleter</c>), which the fail-closed
/// <c>AccountHardDeleteCascadeFitnessTests</c> partition enforces at build time (DPIA C-D1).
/// </para>
///
/// <para>
/// <b>C-D8 VERDICT (senior-cto-advisor Fork G1, 2026-07-16): user delete is HARD — there is no
/// soft-delete state.</b> <c>DeleteCompanyWatchCriterionCommandHandler</c> removes the row via
/// tracked <c>Remove</c> (the #782/ADR 0104 template) — the payload IS the user's personal data, no
/// sweeper exists, and a deleted criterion has no undo value, so Art. 5(1)(e) is satisfied by
/// construction. This settles the C-D8 open condition (security-auditor 2026-07-13) and makes the
/// <see cref="MaxPerUser"/> count question moot: there is no hidden row to count. The soft-delete
/// apparatus PR-3 carried under this verdict (a <c>SoftDelete()</c> method, a <c>DeletedAt</c>
/// property, an EF query filter and the physical <c>deleted_at</c> column, all with zero production
/// callers) was demolished wholesale in the follow-up migration this paragraph once promised. A
/// criterion is therefore either present or gone — the only two states this aggregate has ever
/// meaningfully had.
/// </para>
///
/// <para>
/// <b>No domain events (mirrors <see cref="CompanyWatch"/> / <c>UserJobAdMatch</c>):</b> the
/// Art. 17 cascade is handler-driven by UserId and the browse is a read — there is no reactive
/// consumer of a criterion-created/-deleted event in v1.
/// </para>
/// </summary>
public sealed class CompanyWatchCriterion : AggregateRoot<CompanyWatchCriterionId>
{
    public const int LabelMaxLength = 120;

    /// <summary>
    /// Per-user criterion cap. The CONSTANT lives here (Domain owns the rule, CLAUDE.md §5 — no
    /// magic numbers); the ENFORCEMENT lives in the Application create-handler (PR-3), because a
    /// cross-instance rule is one a single aggregate cannot see — the
    /// <c>RecentJobSearch.MaxPerSeeker</c> precedent. Why cap at all (SavedSearch does not): every
    /// criterion drives a browse — and, once RF-9 unfreezes, a notification scan — over ~1.17M
    /// register rows. The per-user cap is what keeps that cost bounded.
    /// </summary>
    public const int MaxPerUser = 20;

    // The MAPPED state: two Postgres text[] columns (Fork A1: text[], NOT a jsonb VO — the future
    // notification scan wants to invert the predicate, `@company_sni && sni_codes`, which is
    // GIN-indexable on text[] and impossible on jsonb). Npgsql auto-maps List<string> → text[];
    // IReadOnlyList<string> is NOT auto-mapped, so the backing FIELD is the concrete List and the
    // value object below is EF-ignored (architect bind 2026-07-13 Q1; RecentJobSearch precedent).
    private readonly List<string> _sniCodes = [];
    private readonly List<string> _municipalityCodes = [];

    public Guid UserId { get; private set; }

    /// <summary>
    /// The predicate, as a value object. COMPUTED from the two mapped backing lists and ignored by
    /// EF (<c>builder.Ignore</c> — load-bearing: without it EF tries to model the VO itself and the
    /// whole model fails). <see cref="CompanyWatchCriteriaSpec.FromTrusted"/> copies the lists, so
    /// the returned spec never aliases this aggregate's mutable state.
    /// </summary>
    public CompanyWatchCriteriaSpec Criteria =>
        CompanyWatchCriteriaSpec.FromTrusted(_sniCodes, _municipalityCodes);

    /// <summary>Optional user label ("IT i Göteborg"). Null = unlabelled — a criterion is a
    /// PREDICATE, not a named object, and the UI can derive a display label from the codes.
    /// Requiring a name would add friction to the picker while protecting no invariant.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core constructor
    private CompanyWatchCriterion() { }

    private CompanyWatchCriterion(
        CompanyWatchCriterionId id,
        Guid userId,
        CompanyWatchCriteriaSpec criteria,
        string? label,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        Label = label;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ApplyCriteria(criteria);
    }

    /// <summary>
    /// Creates an active criterion for <paramref name="userId"/>. The spec is an already-validated
    /// <see cref="CompanyWatchCriteriaSpec"/> (the caller builds it via
    /// <see cref="CompanyWatchCriteriaSpec.Create"/>), so the guards here cover the aggregate's own
    /// invariants: a real user, a present spec, a label within bounds.
    /// </summary>
    public static Result<CompanyWatchCriterion> Create(
        Guid userId,
        CompanyWatchCriteriaSpec criteria,
        string? label,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CompanyWatchCriterion>(DomainError.Validation(
                "CompanyWatchCriterion.UserIdRequired", "UserId krävs."));

        if (criteria is null)
            return Result.Failure<CompanyWatchCriterion>(DomainError.Validation(
                "CompanyWatchCriterion.CriteriaRequired", "Bevakningens kriterier krävs."));

        var labelResult = NormalizeLabel(label);
        if (labelResult.IsFailure)
            return Result.Failure<CompanyWatchCriterion>(labelResult.Error);

        var criterion = new CompanyWatchCriterion(
            CompanyWatchCriterionId.New(), userId, criteria, labelResult.Value, clock.UtcNow);
        return Result.Success(criterion);
    }

    /// <summary>
    /// Replaces the predicate. No active-only precondition: under hard delete (G1) a removed
    /// criterion has no row to load, so the handler 404s before an aggregate method is ever reached
    /// — the state <c>CompanyWatch.SetFilter</c>'s NotActive guard protects against does not exist
    /// here.
    /// </summary>
    public Result UpdateCriteria(CompanyWatchCriteriaSpec criteria, IDateTimeProvider clock)
    {
        if (criteria is null)
            return Result.Failure(DomainError.Validation(
                "CompanyWatchCriterion.CriteriaRequired", "Bevakningens kriterier krävs."));

        ApplyCriteria(criteria);
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Sets or clears the label (<c>null</c>/blank = clear).
    /// </summary>
    public Result Rename(string? label, IDateTimeProvider clock)
    {
        var labelResult = NormalizeLabel(label);
        if (labelResult.IsFailure)
            return Result.Failure(labelResult.Error);

        Label = labelResult.Value;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    // Mutates the mapped lists IN PLACE (Clear/AddRange). EF's change detection therefore has to
    // snapshot them DEEPLY, or this update would be invisible to it. It does: Npgsql's array type
    // mapping supplies a deep comparer, and the configuration pins an explicit one on top
    // (defense-in-depth — mutation-verified 2026-07-13: the persistence suite stays green without
    // it). The persistence oracle is CompanyWatchCriterionPersistenceTests, which proves the update
    // actually reaches Postgres.
    private void ApplyCriteria(CompanyWatchCriteriaSpec criteria)
    {
        _sniCodes.Clear();
        _sniCodes.AddRange(criteria.SniCodes);
        _municipalityCodes.Clear();
        _municipalityCodes.AddRange(criteria.MunicipalityCodes);
    }

    // Blank → null (an unlabelled criterion), not a validation failure: "no label" and "a label of
    // spaces" are the same user intent.
    private static Result<string?> NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return Result.Success<string?>(null);

        var trimmed = label.Trim();
        if (trimmed.Length > LabelMaxLength)
            return Result.Failure<string?>(DomainError.Validation(
                "CompanyWatchCriterion.LabelTooLong",
                $"Namn får vara max {LabelMaxLength} tecken."));

        return Result.Success<string?>(trimmed);
    }
}
