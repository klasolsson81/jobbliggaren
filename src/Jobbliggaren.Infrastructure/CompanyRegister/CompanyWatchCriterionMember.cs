using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — one row of <c>company_watch_criterion_members</c>: "this saved criterion
/// matches this register company". The materialised half of the smart watch, written by
/// <see cref="CompanyWatchCriterionMaterialiser"/> and read only through
/// <c>ICompanyWatchBrowseQuery</c>.
///
/// <para>
/// <b>Infrastructure-internal, exactly like <see cref="ScbCompanyRegisterEntry"/> — and that is a
/// LAYER decision, not a convenience</b> (senior-cto-advisor, ADR 0139). A member table is the
/// REGISTER's extension: derived data of the register, not of the user's domain, changing when the
/// register changes and when the predicate changes. Its home is where the register's home is. Three
/// consequences follow, and the third is the load-bearing one:
/// <list type="number">
///   <item>DPIA C-D4 / M-C5 hold VERBATIM — no handler holding <c>IAppDbContext</c> can join the
///     register (or this table) against personnummer-lookup output, so no DPIA Part D amendment is
///     owed (security-auditor Blocker 1 never arises).</item>
///   <item>No org.nr crosses the Application boundary on the read path at all: the port answers with
///     ids and counts, never rows (the <c>ListActiveAdIdsAsync</c> pattern).</item>
///   <item><c>ScbCompanyRegisterLayerTests.IAppDbContext_exposes_only_Domain_types</c> would FAIL THE
///     BUILD if anyone put this table on the port. Had the table been Domain-typed instead, that same
///     guard would have PASSED while the property it documents became false — a green gate over a
///     dead invariant, the class this repo has already shipped twice (#805-3, #842).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>The org.nr here is plaintext and NOT tokenised, deliberately</b> (security-auditor Major 4
/// answer). <c>CompanyWatch.organization_number</c> is HMAC'd because it is USER-SUPPLIED; these
/// values are REGISTER-DERIVED and pnr-free at source — different provenance. HMAC would also break
/// the read path outright, since <c>job_ads.organization_number</c> is plaintext (ADR 0087 D8(a)).
/// Pnr-freedom is instead this table's OWN invariant, enforced at this job's write boundary by
/// <see cref="CompanyWatchCriterionMemberFilter"/>.
/// </para>
///
/// <para>
/// <b>It is still personal data</b> (security-auditor Minor 7): a DEK-free text column on a row
/// attributable to a person via <c>criterion_id -&gt; user_id</c> is <c>PlaintextPersonalData</c>
/// under <c>MappedPlaintextExposureRegistry</c>'s STEP 1 row test, with no opt-out. The accepted
/// restore-exposure list (ADR 0125 Case 2, #197, #1285) therefore grows by one entry — the
/// controller's decision, recorded in ADR 0139 under Klas beviljanden (3).
/// </para>
/// </summary>
internal sealed class CompanyWatchCriterionMember
{
    /// <summary>
    /// The most companies ONE criterion may materialise. Above it the criterion is refused by the
    /// breadth gate: no members are written, the state is <c>TooBroad</c>, and the surfaces render
    /// a refusal — the <c>CriterionMatchingAds.SetTooLarge</c> answer the detail page already gives,
    /// so this introduces no new vocabulary.
    ///
    /// <para>
    /// <b>The bound is DERIVED, and the derivation is the point</b> (#1681's own acceptance list: the
    /// bound must be derived, not chosen). <b>The numbers live in the dated reports, never here</b> -
    /// <c>docs/reviews/2026-09-06-1681-membership-measurement.md</c> and the later reading
    /// <c>docs/reviews/2026-09-08-1706-breadth-gate-remeasurement.md</c>. They are
    /// deliberately NOT restated here: two homes for one measured value drift apart at the next
    /// re-measurement (§5 <c>Comments:</c>), and an earlier version of this docblock was exactly that
    /// second home (code-reviewer, 2026-09-06).
    /// </para>
    ///
    /// <para>
    /// <b>What belongs here is the ARGUMENT, because the constant is meaningless without it.</b> The
    /// anchor that survives re-measurement is <b>the budget at the criterion cap</b>:
    /// <c>CompanyWatchCriterion.MaxPerUser</c> is 20, so whatever one criterion costs, the surface
    /// that composes them pays twenty times.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>A second anchor — the twin handler's cost class — was computed in 2026-09-06's report and
    /// is RETIRED as a ratio</b> (ADR 0139 Amendment 2026-09-08): it divides by a sub-millisecond
    /// denominator that is not stable enough to carry it, and the buffer series may not stand in.
    /// Its status is settled in that amendment and nowhere else; do not re-argue it here.
    /// </para>
    ///
    /// <para>
    /// <b>The gate carries three loads, and the third is why it cannot be relaxed on a whim.</b> It is
    /// the product's honest refusal; it is the read-cost bound above; and it is the <b>Art. 5(1)(c)
    /// minimisation argument</b> — security-auditor is explicit that unbounded derived storage is not
    /// "limited to what is necessary".
    /// </para>
    ///
    /// <para>
    /// <b>Re-derive it; do not nudge it.</b> As a COST term the bound is a function of the composed
    /// read at <c>MaxPerUser</c> against <c>/oversikt</c>'s budget, so if <c>job_ads</c> grows, if
    /// <c>MaxPerUser</c> moves, or if ADR 0045's budget changes, re-run the protocol; a hand-adjusted
    /// constant silently stops satisfying the anchor it drifted past.
    /// <b>But a cost trigger is not the only kind, and the distinction is what #1706 turned on:</b>
    /// whether the bound refuses ordinary use is an ACCEPTANCE CRITERION, not a term in that
    /// function — which is exactly why it can fire while no cost term has moved. It did: none of the
    /// cost triggers had fired, and a re-derivation was owed all the same because the product
    /// distribution the bound was shown usable against turned out to be mis-weighted. A list naming
    /// only the cost triggers reads as "nothing is due" at the moment something is.
    /// </para>
    /// </summary>
    public const int MaxPerCriterion = 1000;

    /// <summary>
    /// The owning criterion. FK with <c>ON DELETE CASCADE</c> — see
    /// <c>CompanyWatchCriterionMemberConfiguration</c>.
    ///
    /// <para>
    /// Typed as the Domain <see cref="CompanyWatchCriterionId"/> rather than a bare <c>Guid</c>
    /// because the principal's key IS that type, and EF matches a relationship on the CLR type of the
    /// key: a <c>Guid</c> here would not map to <c>CompanyWatchCriterion.Id</c> at all, so the FK —
    /// and with it the cascade Major 5(a) requires — would silently not be created. Using a Domain
    /// value object as a PROPERTY type does not make this entity a Domain type; the firewall guard
    /// classifies by the ENTITY's assembly, and this one is Infrastructure's.
    /// </para>
    /// </summary>
    public required CompanyWatchCriterionId CriterionId { get; init; }

    /// <summary>The matched company's 10-digit legal-entity org.nr, plaintext, no hyphen — parity
    /// <c>company_register.organization_number</c> and <c>job_ads.organization_number</c>, which is
    /// what the read path joins against.</summary>
    public required string OrganizationNumber { get; init; }

    /// <summary>
    /// REDACTED (#883). This type is a class rather than a record precisely so no member-printing
    /// <c>ToString()</c> is generated — a plain <c>{X}</c> MEL placeholder over a row carrying a raw
    /// org.nr is exactly the exit CLAUDE.md §5 forbids. The override makes that structural rather than
    /// dependent on the type staying a class, and keeps <c>OrgNrRecordLoggingGuardTests</c> honest.
    /// </summary>
    public override string ToString() => "CompanyWatchCriterionMember(org.nr redacted)";
}
