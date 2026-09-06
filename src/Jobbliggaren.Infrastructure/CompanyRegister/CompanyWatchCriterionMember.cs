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
    /// "för bred" — the <c>CriterionMatchingAds.SetTooLarge</c> answer the detail page already gives,
    /// so this introduces no new vocabulary.
    ///
    /// <para>
    /// <b>The bound is DERIVED, and the derivation is the point</b> (#1681's own acceptance list: the
    /// bound must be derived, not chosen). Measured 2026-09-06 against a fixture reproducing dev's
    /// exact shape (<c>company_register</c> 1 066 938 rows / 743 654 Active, <c>job_ads</c> 106 071 /
    /// 41 597 Active over 9 130 distinct org.nr), p95 over 40 runs, never a warm singleton
    /// (<c>docs/reviews/2026-09-04-1559-perf-test-writer.md:43</c> forbids a verdict on singletons).
    /// Full protocol and every number: <c>docs/reviews/2026-09-06-1681-membership-measurement.md</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Anchor 1 — the twin handler's cost class.</b> <c>ListCompanyWatchesQueryHandler</c>'s
    /// bounded <c>= ANY(org.nr array)</c> GROUP BY over <c>job_ads</c> measures <b>0,24 ms p95</b> for
    /// 20 followed companies. The same statement shape over a member set measures <b>1,49 ms</b> at
    /// 1 000 members (6,2x the baseline — same order of magnitude, and the same plan: Bitmap Index
    /// Scan on the org.nr index) and <b>4,23 ms</b> at 5 000 (17,6x — out of the class). The cost is
    /// LINEAR in the member count, so the class boundary is a real boundary and not a cliff we
    /// happened to land in front of.
    /// </para>
    ///
    /// <para>
    /// <b>Anchor 2 — the budget at the criterion cap.</b> <c>CompanyWatchCriterion.MaxPerUser</c> is
    /// 20, so a per-criterion read costs 20x on the surface that composes them: 20 x 1,49 ms =
    /// <b>29,8 ms p95</b>, 9,9 % of <c>/oversikt</c>'s 300 ms p95 budget (ADR 0045 class (a)) — a
    /// block-sized share. At 5 000 members it would be 84,6 ms (28 % of the whole budget for one
    /// block); at 10 000, 431 ms — over budget on its own. Two independent anchors, one answer.
    /// </para>
    ///
    /// <para>
    /// <b>What the gate is worth, measured on BOTH sides.</b> Read: ungated (743 654 members) the same
    /// statement is <b>2 068 ms p95</b>, so the gate is worth ~1 388x there. Job: the widest
    /// bound-legal criterion's register selection under <c>LIMIT MaxPerCriterion + 1</c> costs
    /// <b>11-30 ms</b> against the real 1 066 938-row register, versus the <b>6 556 ms</b> the ungated
    /// live ad count cost — the early stop is what makes the refusal cheap. Storage: per-user derived
    /// rows fall from 743 654 x 20 = 14,9M to 1 000 x 20 = <b>20 000</b>, a 744x reduction. That last
    /// number is not a perf note: security-auditor is explicit that unbounded derived storage is not
    /// "limited to what is necessary" (Art. 5(1)(c)), so <b>the gate IS the minimisation argument</b>,
    /// and this constant is that argument's operative value.
    /// </para>
    ///
    /// <para>
    /// <b>The product half, measured on the REAL register</b> (read-only, dev, 2026-09-06) rather than
    /// assumed — a bound that refuses ordinary use would be a bug wearing a rationale. Of the 101 180
    /// distinct (kommun, SNI) cells, <b>83 (0,08 %)</b> hold more than 1 000 Active companies; the
    /// median cell holds 2 and the p95 cell 39. The ordinary criterion therefore materialises with
    /// three orders of magnitude to spare. What refuses is genuinely broad: a whole industry
    /// nationwide exceeds the bound for 203 of 830 SNI codes (24 %), and a whole municipality for 144
    /// of 291 (49 %, the big cities). Those are precisely the criteria whose ad count was unaffordable
    /// to compute live, so the refusal lands where the cost was.
    /// </para>
    ///
    /// <para>
    /// <b>Re-derive it; do not nudge it.</b> The bound is a function of three measured quantities —
    /// the twin handler's cost class, <c>MaxPerUser</c>, and <c>/oversikt</c>'s budget. If
    /// <c>job_ads</c> grows, if <c>MaxPerUser</c> moves, or if ADR 0045's budget changes, re-run the
    /// protocol; a hand-adjusted constant silently stops satisfying whichever anchor it drifted past.
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
