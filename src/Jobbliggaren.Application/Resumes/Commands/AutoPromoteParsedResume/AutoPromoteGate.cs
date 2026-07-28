using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// The ONE auto-promote policy evaluator (#1060 CTO-bind D4-REBIND): "may this parse become a
/// canonical CV, and if not, why". Extracted from
/// <see cref="AutoPromoteParsedResumeCommandHandler"/> so a SECOND caller — the DEK-warm read
/// path <c>GetParsedResumeQueryHandler</c> — can answer the same question with the same
/// predicate, and the user learns the reason by opening the review she was already opening,
/// instead of re-uploading the file (#1060's third sub-requirement).
///
/// <para><b>Two name parameters, not one, and that is the point (architect's bind, CTO-ACCEPTED
/// 2026-07-25 D5-REBIND-2).</b> <paramref name="personName"/> is CV <i>content</i> — it becomes
/// <c>PersonalInfo.FullName</c> inside the DEK-encrypted shadow, and it is what the DQ6 guard
/// scans. <paramref name="label"/> is a plaintext <i>label channel</i> — it becomes
/// <c>Resume.Name</c>, an unencrypted column that surfaces in CV lists. Two knowledge pieces,
/// two data-protection classes, two failure meanings. Collapsing them into one
/// <c>resolvedName</c> would re-unify exactly what PR A split.</para>
///
/// <para><b>Pure: no I/O, no DbContext, no mutation of anything the caller owns.</b> Every input
/// is a value the caller already holds. That is what makes it callable from a query handler at
/// all — a pure function is only callable where its arguments are obtainable, and D4's original
/// "put it on the pending-summary query" bind failed precisely on that test (that handler is
/// deliberately not <c>IRequiresFieldEncryptionKey</c>, so the DEK-bearing half of the input is
/// out of its reach — a written PII-minimisation control, not an oversight).</para>
///
/// <para><b>Why it returns the built <see cref="Resume"/> and not merely a reason.</b> The
/// Tier-2 verdict IS <c>Resume.CreateFromParsed</c> — the one buildability authority (its
/// failure is the honest "content insufficient, user reviews", never re-encoded). Handing the
/// aggregate back makes this the ONLY <c>CreateFromParsed</c> call site on the auto-promote
/// path, so gate and handler cannot drift apart into two predicates that disagree; the
/// alternative (gate probes, handler rebuilds) is two evaluations of one question, which is
/// the defect D4-REBIND rejected alternative (4) for. The read side ignores the
/// <see cref="AutoPromoteGateVerdict.Promotable"/> arm's aggregate: it is never added to a
/// DbContext, so its domain events are never dispatched (dispatch runs off tracked entries at
/// SaveChanges), and it is garbage in the same request.</para>
///
/// <para><b>Order is load-bearing and unchanged from the handler it came from:</b> cheapest and
/// highest-PII-priority first (parse-flag → extraction failure → label channel → composed
/// content) and only then buildability. A personnummer anywhere reports
/// <see cref="AutoPromoteBlockReason.PersonnummerPresent"/>, never
/// <see cref="AutoPromoteBlockReason.IncompleteContent"/>, because
/// <c>Resume.ValidateName</c>/<c>ValidateContent</c> would refuse it as a buildability failure
/// and that would mis-report the verdict (CLAUDE.md §5).</para>
/// </summary>
internal static class AutoPromoteGate
{
    /// <summary>
    /// Evaluates every auto-promote gate against <paramref name="parsed"/>. Returns
    /// <see cref="AutoPromoteGateVerdict.Blocked"/> with the first reason that fires, else
    /// <see cref="AutoPromoteGateVerdict.Promotable"/> carrying the canonical CV the parse
    /// builds.
    /// </summary>
    /// <param name="parsed">The staging artifact. Read-only here; nothing is mutated.</param>
    /// <param name="personName">The account holder's display name — CV content, never the
    /// parsed contact name and never the form field (5a CTO-bind R5).</param>
    /// <param name="label">The resolved CV label (<see cref="ResumeLabelResolver"/>).</param>
    /// <param name="jobSeekerId">The owner. The caller has already scoped its load to this
    /// owner; this value only reaches <c>CreateFromParsed</c>'s own required-id check.</param>
    /// <param name="clock">Injected time (CLAUDE.md §5 — never <c>DateTime.UtcNow</c>).</param>
    public static AutoPromoteGateVerdict Evaluate(
        ParsedResume parsed,
        string personName,
        string label,
        JobSeekerId jobSeekerId,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(clock);

        // ── Tier 1: the two POLICY gates (CTO-bind §2; narrowed from three by #1060's D1
        // bind) — both read-only. Order: highest PII priority first, then extraction failure.
        if (parsed.Personnummer.Found)
            return new AutoPromoteGateVerdict.Blocked(AutoPromoteBlockReason.PersonnummerPresent);

        // #1060 D1.3: `Failed` ONLY, not `RequiresManualReview`. A `Degraded` parse found
        // something and is honest that the document was messy — under ADR 0112 that is the CV
        // the reviewer has most to say about, so blocking it gives the least product to the
        // user who needs it most. `Failed` still blocks: extraction produced nothing usable, so
        // the promote would build a CV that says less than the file did. The enum member's
        // docblock records the reversal of the 5a bind's R3 — read it before re-tightening.
        //
        // #1060 D1.2: the preamble gate that stood between these two is RETIRED. Text above the
        // first heading no longer blocks; it is projected onto the canonical CV at promote
        // (ADR 0109 amendment 2026-07-27). Nothing is minted here — AutoPromoteContentMapper
        // still never maps Preamble into Summary or a Section, which is the prohibition the
        // gate was standing in for, enforced where it belongs.
        if (parsed.Confidence.Overall == OverallConfidenceLevel.Failed)
            return new AutoPromoteGateVerdict.Blocked(AutoPromoteBlockReason.ParseNotConfident);

        // A personnummer in the LABEL is a personnummer presence, not "incomplete content" —
        // `Resume.ValidateName` would refuse it too, but as a buildability failure, which
        // would mis-report the reason (§5: never mis-report a verdict).
        if (PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(label)).Count > 0)
            return new AutoPromoteGateVerdict.Blocked(AutoPromoteBlockReason.PersonnummerPresent);

        // ── Tier 2: buildability, through the ONE existing promote pipeline.
        var dto = AutoPromoteContentMapper.ToContentDto(parsed.Content, personName);

        // DQ6 on the COMPOSED content (arch-tripwire-required for every CreateFromParsed
        // caller — the tripwire's walk is transitive within the Application module, so the
        // command handler stays correctly classified with both the guard call and the sink
        // call delegated here). The import scan covered the raw-text superset of everything
        // the parse structured, so the one genuinely new text here is the account display
        // name — a personnummer riding in it is caught HERE, and the disposition is the same
        // honest "pending, review" (it is a personnummer presence, whichever field carries it).
        var guard = ResumeContentPersonnummerGuard.Check(dto);
        if (guard.IsFailure)
            return new AutoPromoteGateVerdict.Blocked(AutoPromoteBlockReason.PersonnummerPresent);

        var content = ResumeContentMapper.ToDomain(dto);
        var created = Resume.CreateFromParsed(jobSeekerId, label, content, parsed.Id, clock);

        return created.IsFailure
            ? new AutoPromoteGateVerdict.Blocked(AutoPromoteBlockReason.IncompleteContent)
            : new AutoPromoteGateVerdict.Promotable(created.Value);
    }
}

/// <summary>
/// What <see cref="AutoPromoteGate.Evaluate"/> found. A CLOSED discriminated union (private
/// constructor + nested cases — nothing outside this file can add a case), mirroring
/// <see cref="AutoPromoteOutcome"/>'s precedent in the same folder for the same reason:
/// correctness hangs on the two arms being the only two.
/// </summary>
internal abstract record AutoPromoteGateVerdict
{
    private AutoPromoteGateVerdict() { }

    /// <summary>A gate fired. <paramref name="Reason"/> is the FIRST one in evaluation order —
    /// the honest single answer, not a set (a CV blocked on a personnummer is not additionally
    /// "incomplete"; it has not been asked yet).</summary>
    public sealed record Blocked(AutoPromoteBlockReason Reason) : AutoPromoteGateVerdict;

    /// <summary>No gate fired, and <paramref name="Resume"/> is the canonical CV the parse
    /// builds — already validated, not yet persisted and not yet linked to its parse's
    /// promote. The write path adopts it; the read path discards it.</summary>
    public sealed record Promotable(Resume Resume) : AutoPromoteGateVerdict;

    /// <summary>The block reason, or <c>null</c> when promotable. The read path's whole
    /// interest in this type: it wants the reason token, never the aggregate.</summary>
    public AutoPromoteBlockReason? BlockReason =>
        this is Blocked blocked ? blocked.Reason : null;
}
