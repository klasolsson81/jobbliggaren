using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Common;

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
/// path, so gate and handler cannot drift apart into two implementations of one predicate; the
/// alternative (gate probes, handler rebuilds) is two evaluations of one question, which is
/// the defect D4-REBIND rejected alternative (4) for. The read side ignores the
/// <see cref="AutoPromoteGateVerdict.Promotable"/> arm's aggregate: it is never added to a
/// DbContext and is unreachable once <c>Evaluate</c> returns, so nothing can observe the three
/// domain events <c>CreateFromParsed</c> raises on it. Two facts carry that, and the earlier
/// wording named neither: (1) <b>no domain-event dispatcher exists in <c>src/</c></b> —
/// <c>DomainEvents</c> is raise-only and every EF configuration <c>Ignore</c>s it; (2)
/// <c>UnitOfWorkBehavior</c> is constrained to <c>ICommand&lt;TResponse&gt;</c>, so
/// <c>SaveChangesAsync</c> never runs on a query request at all. Fact (2) keeps holding for any
/// future dispatcher, however it is built.</para>
///
/// <para><b>Order is load-bearing and unchanged from the handler it came from:</b> cheapest and
/// highest-PII-priority first (parse-flag → extraction failure → label channel → composed
/// content) and only then buildability. A personnummer anywhere reports
/// <see cref="AutoPromoteBlockReason.PersonnummerPresent"/>, never
/// <see cref="AutoPromoteBlockReason.IncompleteContent"/>, because
/// <c>Resume.ValidateName</c>/<c>ValidateContent</c> would refuse it as a buildability failure
/// and that would mis-report the verdict (CLAUDE.md §5).</para>
///
/// <para><b>The two call sites run one predicate over one artifact — and they differ on exactly
/// one input, deliberately (#1060 PR C, CTO-bind D1).</b> <paramref name="label"/> is not a
/// property of the artifact; it is a property of a future SUBMISSION, and only the write path
/// has it. The read path therefore passes the generated default, which means a personnummer the
/// user typed into the CV-name field is <b>not assessed</b> there — never cleared. That
/// asymmetry is accepted, documented and pinned by a test
/// (<c>GetParsedResumeEndpointTests</c>'s label-asymmetry case); do not "fix" it by persisting
/// the typed label, which would store user text known to carry a personnummer. What closes the
/// loop instead is the upload form, which refuses that name at the field.</para>
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
        //
        // Every blocking arm passes DomainErrorCode explicitly, and the argument is NAMED at
        // each of the five — the three arms in this tier (the two policy gates plus the label
        // scan, which is not itself a policy gate under the D1 bind) plus the Tier-2 DQ6 arm
        // (null) and the buildability arm (the code). The parameter has no default on purpose (#1060 D3(β) PR 2): with one,
        // dropping it compiles and the arm silently starts claiming "no domain evaluation ran"
        // — the same class of surviving mutation LayoutChainRunner.Crashed's fourth argument was
        // measured to produce. Without one it is a build error.
        if (parsed.Personnummer.Found)
        {
            // No Domain evaluation ran. This reads the aggregate's OWN scan outcome, a value
            // ParsedResume already holds; nothing returned a Result, so there is no code to carry.
            return new AutoPromoteGateVerdict.Blocked(
                AutoPromoteBlockReason.PersonnummerPresent, DomainErrorCode: null);
        }

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
        {
            // No Domain evaluation ran either: this compares a confidence LEVEL the parse
            // already carries. Same shape as the arm above, different subject.
            return new AutoPromoteGateVerdict.Blocked(
                AutoPromoteBlockReason.ParseNotConfident, DomainErrorCode: null);
        }

        // A personnummer in the LABEL is a personnummer presence, not "incomplete content" —
        // `Resume.ValidateName` would refuse it too, but as a buildability failure, which
        // would mis-report the reason (§5: never mis-report a verdict).
        if (PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(label)).Count > 0)
        {
            // Null for a STRONGER reason than the two above, and it is the same reason this rung
            // exists at all: a Domain code IS obtainable here — `Resume.ValidateName` returns
            // `Resume.NamePersonnummerMustBeRemoved` for this very label — but the gate refuses
            // BEFORE asking it, precisely so the verdict is not reported as a buildability
            // failure. Carrying that code would name a refusal that never happened, which is the
            // mis-report this ordering was written to prevent.
            return new AutoPromoteGateVerdict.Blocked(
                AutoPromoteBlockReason.PersonnummerPresent, DomainErrorCode: null);
        }

        // ── Tier 2: buildability, through the ONE existing promote pipeline.
        var dto = AutoPromoteContentMapper.ToContentDto(parsed.Content, personName);

        // DQ6 on the COMPOSED content (arch-tripwire-required for every CreateFromParsed
        // caller — the tripwire's walk is transitive within the Application module, so the
        // command handler stays correctly classified with both the guard call and the sink
        // call delegated here). The import scan covered the raw-text superset of everything
        // the parse structured, so the one genuinely new text here is the account display
        // name — a personnummer riding in it is caught HERE, and the disposition is the same
        // honest "pending, review" (it is a personnummer presence, whichever field carries it).
        //
        // WHY this arm can fire at all: JobSeeker.Register/UpdateDisplayName validate only
        // non-empty and length, so a personnummer goes into that plaintext column unrefused —
        // the invariant Resume.ValidateName carries one aggregate over, for a stated reason
        // that applies verbatim there. Tracked as #1117 (P1). Until it lands, this guard is
        // the only control standing on that channel.
        var guard = ResumeContentPersonnummerGuard.Check(dto);
        if (guard.IsFailure)
        {
            // A DISTINCT token (#1060 PR C, CTO-bind D2). Every other text in `dto` is a
            // projection of the parse, and the import scan already covered that raw superset —
            // so if this guard fires and the parse's own scan did not, the number is in the
            // account display name, which is the one text this composition adds. Reporting it
            // as PersonnummerPresent sent the user to search a clean file: a mis-reported
            // verdict (CLAUDE.md §5) and a loop with no exit, because the fix is under
            // Inställningar and nothing said so.
            // Null even though `guard.Error.Code` exists (`Resume.PersonnummerMustBeRemoved`) —
            // and the reason is the asymmetry the whole field exists for. THIS token is already
            // 1:1 with that code: reaching this rung IS that refusal, so carrying it would add a
            // second name for one fact. `IncompleteContent`, below, collapses `CreateFromParsed`'s
            // WHOLE error set onto one token, and that is the collapse a reader cannot undo.
            return new AutoPromoteGateVerdict.Blocked(
                AutoPromoteBlockReason.PersonnummerInAccountName, DomainErrorCode: null);
        }

        var content = ResumeContentMapper.ToDomain(dto);
        var created = Resume.CreateFromParsed(jobSeekerId, label, content, parsed.Id, clock);

        // The ONE arm that carries a code: `created.Error.Code`, verbatim and unexamined. This is
        // not a second evaluation of "why did this fail" — it is the FIRST evaluation's output,
        // stopped from being discarded. No predicate is re-encoded here, so nothing can drift.
        return created.IsFailure
            ? new AutoPromoteGateVerdict.Blocked(
                AutoPromoteBlockReason.IncompleteContent, created.Error.Code)
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
    /// "incomplete"; it has not been asked yet).
    ///
    /// <para><b><paramref name="DomainErrorCode"/> is populated on the
    /// <see cref="AutoPromoteBlockReason.IncompleteContent"/> arm ONLY</b>, and every other arm
    /// passes <c>null</c> as a named argument with its own reason written beside it. That is a
    /// contract, not a convention — <c>AutoPromoteGateTests</c> pins it, because
    /// <c>Blocked(PersonnummerPresent, "something")</c> is otherwise representable and nothing
    /// would say it is wrong. (A third DU case <c>Unbuildable(DomainError)</c> would make it
    /// unrepresentable and was rejected: this type's own docblock says correctness hangs on the
    /// two arms being the only two, and a third forces both call sites and every test to handle
    /// it for no user-visible gain.)</para>
    ///
    /// <para><b>Why it exists.</b> <see cref="AutoPromoteBlockReason.IncompleteContent"/> is one
    /// token over every code <c>Resume.CreateFromParsed</c> can return — thirty-two declared by
    /// <c>ValidateContent</c> alone, plus <c>JobSeekerIdRequired</c> and <c>ValidateName</c>'s three.
    /// The two mechanisms behind them
    /// have different fixes in different homes — a per-entry failure
    /// (<c>ExperienceCompanyRequired</c>) is routable, while a whole-document one
    /// (<c>SummaryTooLong</c>) was bound to the lexicon asset instead. Until now no consumer could
    /// tell them apart: <c>Resume.CreateFromParsed</c> returned the code and this gate discarded
    /// it whole.</para>
    ///
    /// <para><b>The code, never the whole <c>DomainError</c>.</b> A <c>DomainError</c> also carries
    /// the Swedish user-facing message; logging that would put a second home for UI copy into Seq.
    /// The code is a closed constraint identity — no field value, no user text.</para>
    ///
    /// <para><b>It is named for what it carries, not for the arm a reader hopes produced it.</b>
    /// <c>CreateFromParsed</c> can also refuse on <c>Resume.JobSeekerIdRequired</c> and on
    /// <c>ValidateName</c>'s three codes, so calling this <c>ValidateContentCode</c> would be a
    /// claim about the caller's own preconditions rather than about this value.</para>
    ///
    /// <para><b>The boundary is a COMPILER GUARANTEE, not a promise.</b>
    /// <see cref="AutoPromoteGateVerdict"/> is <c>internal</c> and
    /// <c>Jobbliggaren.Application.csproj</c> grants <c>InternalsVisibleTo</c> to
    /// <c>Jobbliggaren.Application.UnitTests</c> and <c>Jobbliggaren.Api.IntegrationTests</c> only
    /// — <b>not</b> to <c>Jobbliggaren.Api</c>. So the detail cannot reach
    /// <c>ResumesEndpoints</c>, where <c>pending.Reason.ToString()</c> goes straight onto the
    /// wire; leaking it there is a build error rather than a review miss. Putting the detail on
    /// <see cref="AutoPromoteOutcome.LeftPending"/> instead was rejected for exactly that reason:
    /// it IS the wire type, and a Domain constraint code arriving on it would reach a zod schema
    /// and an FE with no copy for it — a mis-reported verdict shown to a user, plus a hardcoded
    /// UI string (CLAUDE.md §5, both halves).</para></summary>
    public sealed record Blocked(AutoPromoteBlockReason Reason, string? DomainErrorCode)
        : AutoPromoteGateVerdict;

    /// <summary>No gate fired, and <paramref name="Resume"/> is the canonical CV the parse
    /// builds — already validated, not yet persisted and not yet linked to its parse's
    /// promote. The write path adopts it; the read path discards it.</summary>
    public sealed record Promotable(Resume Resume) : AutoPromoteGateVerdict;

    /// <summary>The block reason, or <c>null</c> when promotable. The read path's whole
    /// interest in this type: it wants the reason token, never the aggregate.</summary>
    public AutoPromoteBlockReason? BlockReason =>
        this is Blocked blocked ? blocked.Reason : null;
}
