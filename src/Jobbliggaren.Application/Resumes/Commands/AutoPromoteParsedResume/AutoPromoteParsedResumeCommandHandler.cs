using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// The "spara direkt" mechanism (CV-pivot PR 5a, CTO-bind 2026-07-17). Flow: resolve owner
/// (id + display name in one projection) → owner-scoped tracked load (IDOR fail-closed,
/// parity <c>PromoteParsedResumeCommandHandler</c>) → resolve the two names →
/// <see cref="AutoPromoteGate"/> (every gate, in order, ending in the ONE buildability
/// authority) → <c>ParsedResume.Promote</c> (aggregate owns the gate) → add →
/// reconciler-seed → audit → <c>Promoted</c>.
///
/// <para><b>The gates left this handler in PR C (#1060 D4-REBIND) and nothing about them
/// changed.</b> They moved because the read path needs the same verdict and must not re-encode
/// it: <c>GetParsedResumeQueryHandler</c> now calls the same
/// <see cref="AutoPromoteGate.Evaluate"/> so the CV hub can say WHY a file stayed pending
/// without a second upload. The gate hands back the built <c>Resume</c> on the promotable arm,
/// so this handler has no <c>CreateFromParsed</c> call of its own and the two paths cannot
/// drift into two predicates.</para>
///
/// <para><b>Every non-promote exit precedes every mutation.</b> A <c>LeftPending</c> returns
/// before <c>Promote</c>/<c>Add</c> touch anything, so the unconditional
/// <c>UnitOfWorkBehavior</c> save is a no-op and the artifact stays <c>PendingReview</c>,
/// fully visible to the review flow — the same structural atomicity the import handler
/// documents. A Tier-2 buildability failure is deliberately CONVERTED from the aggregate's
/// validation error to <c>LeftPending(IncompleteContent)</c>: on this path the user never
/// submitted anything to 400 — the same aggregate verdict that is a client error on
/// user-promote is a routing fact here (same gate, two call contexts, two dispositions).</para>
///
/// <para>The audit row (<see cref="AutoPromoteParsedResumeCommand.AuditEventType"/>, GDPR
/// Art. 22) is written in-handler on the <c>Promoted</c> branch only — see the command's
/// docblock for why the blanket behavior cannot carry it — with the same providers
/// <c>AuditBehavior</c> uses, in the same transaction as the promote. The handler never
/// reads or logs decrypted content; the warmed owner DEK decrypts the parse shadow on load
/// and encrypts the new Master on write (ADR 0074 Invariant 3).</para>
/// </summary>
public sealed partial class AutoPromoteParsedResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger,
    IResumeReviewReconciler reconciler,
    ICorrelationIdProvider correlationIdProvider,
    IRequestContextProvider requestContextProvider,
    ILogger<AutoPromoteParsedResumeCommandHandler> logger)
    : ICommandHandler<AutoPromoteParsedResumeCommand, Result<AutoPromoteOutcome>>
{
    public async ValueTask<Result<AutoPromoteOutcome>> Handle(
        AutoPromoteParsedResumeCommand command, CancellationToken cancellationToken)
    {
        // AuthorizationBehavior has already thrown if !currentUser.IsAuthenticated.
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        // One projection resolves both the owner scope and the bound name source
        // (JobSeeker.DisplayName — CTO R5); no second round-trip.
        var owner = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => new { js.Id, js.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner is null)
            return Result.Failure<AutoPromoteOutcome>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Owner-scoped TRACKED load (Promote mutates the artifact). The parsed_resumes
        // global query filter (DeletedAt == null) already hides Promoted/Discarded rows —
        // a finalized artifact reads as NotFound, the fail-closed answer.
        var parsedResumeId = new ParsedResumeId(command.ParsedResumeId);
        var parsed = await db.ParsedResumes
            .FirstOrDefaultAsync(
                r => r.Id == parsedResumeId && r.JobSeekerId == owner.Id, cancellationToken);

        if (parsed is null)
        {
            // IDOR fail-closed: identical NotFound whether the id is unknown or foreign
            // (no enumeration oracle); log only the cross-user case.
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value,
                    "AutoPromoteParsedResume");
            }
            return Result.Failure<AutoPromoteOutcome>(
                DomainError.NotFound("ParsedResume", parsedResumeId.Value));
        }

        // ── The two names are DIFFERENT concepts and are resolved separately (#1060).
        //
        // Until now one string fed both, so a user who named the CV "Backend-CV 2026" got
        // that printed where her name belongs, and a user who accepted the suggested account
        // name got every import labelled identically in the hub. They are also in different
        // data-protection classes: `Resume.Name` is a PLAINTEXT column that surfaces in CV
        // lists (its classification rests on it being a LABEL — see Resume.ValidateName's
        // remarks), while PersonalInfo.FullName lives in the DEK-encrypted content shadow.
        // Defaulting the plaintext column to the account holder's personal name made personal
        // data the standard content of exactly that column.
        //
        // Person name: ALWAYS the account holder's display name. Never the form field, and
        // never the parsed contact name (5a CTO-bind R5, preserved).
        var personName = owner.DisplayName;

        // Label: the form field when the user typed one, else a generated non-PII default. The
        // file name is deliberately NOT a candidate — ADR 0096 D-B refused it on Resume, and a
        // filename label would also falsify the documented rule that a filename never reaches
        // the canonical Resume (PersonnummerScanOutcome), which B4's Warn-not-Fail rests on.
        var label = ResumeLabelResolver.Resolve(command.NameOverride, clock);

        // ── Every gate, in one place, shared verbatim with the read path (#1060 D4-REBIND).
        // The promotable arm carries the built Resume: this handler deliberately has no
        // CreateFromParsed of its own, so "would it build?" and "build it" are one evaluation.
        var verdict = AutoPromoteGate.Evaluate(parsed, personName, label, owner.Id, clock);
        if (verdict is AutoPromoteGateVerdict.Blocked blocked)
            return LeftPending(blocked.Reason, blocked.DomainErrorCode, parsed.Id);

        var resume = ((AutoPromoteGateVerdict.Promotable)verdict).Resume;

        // ── Mutations begin. The aggregate owns the promote gate (PendingReview + no
        // flagged personnummer); the personnummer half was re-verified by the policy gate
        // above, and PendingReview is guaranteed structurally by the query filter (a
        // Promoted/Discarded row is soft-deleted and reads as NotFound) — so a failure
        // here is a genuine (e.g. concurrent) inconsistency and propagates as a real
        // Failure, not a LeftPending.
        var promotion = parsed.Promote(clock);
        if (promotion.IsFailure)
            return Result.Failure<AutoPromoteOutcome>(promotion.Error);

        db.Resumes.Add(resume);

        // Seed the DEK-free finding-status ledger in the SAME transaction (ADR 0093
        // §D5(b) — the arch tripwire requires every CreateFromParsed caller to
        // reconcile). The reconciler completes or THROWS (CTO bind 2026-07-17): a throw
        // propagates past this handler, the unconditional UnitOfWork save never runs,
        // and resume + promote + audit roll back TOGETHER — which is what resolves the
        // 5a security escalation (a promoted CV can never persist without its Art. 22
        // audit row), so the audit-add-after-reconcile ordering below is safe as-is.
        await reconciler.ReconcileAsync(resume, null, cancellationToken);

        // Art. 22 audit — Promoted branch ONLY (a LeftPending created nothing to audit;
        // a row for it would misreport, §5). Same providers and same transaction as the
        // blanket AuditBehavior; distinct event type keeps machine-verbatim provenance
        // distinguishable from the human-curated Resume.PromotedFromParsed.
        db.AuditLogEntries.Add(AuditLogEntry.Create(
            occurredAt: clock.UtcNow,
            correlationId: correlationIdProvider.Current,
            userId: currentUser.UserId,
            eventType: AutoPromoteParsedResumeCommand.AuditEventType,
            aggregateType: "Resume",
            aggregateId: resume.Id.Value,
            ipAddress: requestContextProvider.IpAddress,
            userAgent: requestContextProvider.UserAgent));

        return Result.Success<AutoPromoteOutcome>(
            new AutoPromoteOutcome.Promoted(resume.Id.Value));
    }

    // #1060 D4-REBIND(6). NON-PII by construction: the reason is a closed enum token (never free
    // text, never a field VALUE — see AutoPromoteBlockReason's docblock) and the id is the
    // staging artifact's surrogate key. No file name, no display name, no parsed content; this
    // handler never logs decrypted content (ADR 0074 Invariant 3, CLAUDE.md §5).
    //
    // THE SAME HOLDS OF `blockDetail`, and it is worth stating on its own terms rather than
    // waved through under "the reason is a token" (#1060 D3(β) PR 2). It is
    // DomainError.CODE — never DomainError.Message, which carries the Swedish user-facing text
    // — and every value it can hold is a DomainError code literal. What makes THAT exhaustive —
    // the half the minimisation argument rests on — is that CreateFromParsed's whole error set is
    // compile-time literals with no interpolation.
    //
    // Adjudicator, so this is checkable rather than asserted — and it has to be THIS shape:
    //   grep -rA1 -nE 'DomainError\.[A-Za-z]+\(' src/Jobbliggaren.Domain/Resumes/
    // then READ the code argument on every hit. TWO deliberate non-enumerations, because an
    // enumeration is what this whole paragraph exists to retire, and each was measured free
    // before it was taken. RECURSIVE over the directory, not a list of the files on the path
    // today: extract a further buildability file and a path list still returns nothing but
    // literals while the interpolated code sits in the file it never looked at — silent, and in
    // the reassuring direction. β-2 IS that extraction, so the hazard is demonstrated rather than
    // hypothetical. And `[A-Za-z]+` rather than the four factory names, because DomainError.cs
    // lives OUTSIDE this scope, so a fifth factory would arrive invisibly the same way; the open
    // form is byte-identical to the alternation today, so widening costs nothing. The residual
    // cost is a handful of off-path hits (ResumeFile, ParsedResume) the reader drops by path.
    //
    // Two under-reaches make the obvious form useless,
    // both measured rather than feared. (1) The house wraps after the open paren, so matching
    // `DomainError.Validation(` alone shows the code on 6 of 39 hits in Resume.cs and 0 of 13 in
    // ResumeEntryBuildability — the file that declares every per-entry code is entirely blind to
    // it, and a planted `$"Resume.Experience{nameof(...)}Required"` produced byte-identical
    // output. (2) Matching `Validation(` alone misses DomainError.NotFound(entity, id), whose
    // whole job is to INTERPOLATE its code, so an arm added through it would never appear at all.
    // A bare `$"` sweep is not the fix either: Resume.cs has two, and both are DomainException
    // MESSAGES carrying literal codes, outside the Result path this parameter draws from.
    //
    // SEPARATELY, and for the ROUTING question rather than the PII one, each code is of one of
    // exactly two kinds: a PER-ENTRY constraint on a work-experience or education entry (e.g.
    // `Resume.ExperienceCompanyRequired`), or a WHOLE-DOCUMENT one — every other code
    // CreateFromParsed can return, which is whole-document by definition. That classification is
    // a complement, so it cannot leak; the files are dated context (today the per-entry ones live
    // in ResumeEntryBuildability and the rest in Resume.ValidateContent / ValidateName /
    // CreateFromParsed's own preconditions). The revision that PREDATED #1060 D3(β-2) enumerated
    // declaring files, and β-2 falsified it by moving thirteen codes, making its own worked
    // example name a file that no longer declares it — an enumeration survives a new ARM inside
    // an existing file but not a new file being EXTRACTED, which is precisely the move that broke
    // it. A code names a
    // CONSTRAINT that was not met; it never carries the field's value, its length, or any
    // fragment of CV text. It is null on every arm but buildability
    // (AutoPromoteGateVerdict.Blocked's docblock; test-pinned).
    //
    // TWO codes touch the personnummer surface, and they are kept out by DIFFERENT things — an
    // earlier revision of this paragraph named one code, named it wrongly, and gave it the other
    // one's reason, which three reviewers measured independently:
    //   - `Resume.NamePersonnummerMustBeRemoved` (Resume.ValidateName) IS in CreateFromParsed's
    //     error set, so it is the one that could actually ride this parameter. What keeps it out
    //     is gate ORDER: AutoPromoteGate scans the resolved label with the SAME predicate and
    //     returns PersonnummerPresent before buildability is ever asked. That is the load-bearing
    //     argument and it was the missing one. It is PINNED, not merely asserted here:
    //     AutoPromoteGateTests' "pnr in label" arm expects PersonnummerPresent with a null code,
    //     so a reordering that let ValidateName answer first turns that test red.
    //   - `Resume.PersonnummerMustBeRemoved` is not a Resume.cs code at all —
    //     ResumeContentPersonnummerGuard (Application) owns it, its arm returns
    //     PersonnummerInAccountName, and that arm passes DomainErrorCode: null. It is not in the
    //     error set this parameter draws from, so it is excluded by construction rather than by
    //     order.
    // Either way the disclosure would be nil: the token printed beside the code on those arms
    // already carries the same presence boolean.
    //
    // Two PURPOSES, and Art. 5(1)(c) wants purposes rather than precedent — the earlier version
    // of this comment justified the id by noting IFailedAccessLogger already logs it, which is
    // true and is not a reason (CTO-bind D3.4, overruling security-auditor's minimisation
    // finding on exactly this ground).
    //   (1) SUPPORT, per artifact: answering "why is this user's CV stuck" needs the id. The
    //       alternative — re-running the gate against the row — requires DEK access to CV-PII,
    //       so logging a surrogate key is the LESS invasive route to the same answer. #1060
    //       itself was diagnosed by reading a dev-DB row by hand, which is the cost of not
    //       having this line. `blockDetail` serves the same purpose one level down: a support
    //       reader sees WHICH constraint refused the CV instead of a token that covers a missing
    //       employer and an over-long summary equally.
    //   (2) DISTRIBUTION, across artifacts: which gate actually stops real uploads is a question
    //       production has never been able to answer.
    //
    // AND IT CARRIES NO CLAIM ABOUT D3. The previous revision of purpose (2) ended "…and D3's
    // per-entry decomposition is explicitly waiting on that measurement", which was false twice
    // over: D3(β)'s instrument is the layout corpus and never production (#1060 R2 withdrew
    // production measurement; R3 names the corpus), and the log it pointed at did not carry the
    // sub-reason at all — the very gap this line now closes. Corrected in the PR that makes the
    // sentence above true, per CTO-bind B.4: a truth-change is owned by the PR that makes it true.
    private Result<AutoPromoteOutcome> LeftPending(
        AutoPromoteBlockReason reason, string? domainErrorCode, ParsedResumeId parsedResumeId)
    {
        LogLeftPending(logger, reason, parsedResumeId.Value, domainErrorCode);
        return Result.Success<AutoPromoteOutcome>(new AutoPromoteOutcome.LeftPending(reason));
    }

    // Information, not Warning: a LeftPending is an expected product state the user resolves,
    // not a fault (the same reason it rides Result.Success). MEL property names come from the
    // placeholder TOKENS, so these read as `BlockReason` / `ParsedResumeId` / `BlockDetail` in
    // Seq — never as the prose beside them, which is why `StructuredPropertyNameContractTests`
    // exists and why the corpus looks `BlockDetail` up by exactly that spelling.
    //
    // `BlockDetail` renders as `(null)` on the four arms that carry no code. That is the honest
    // reading — "this block was not a Domain refusal" — and it keeps one line shape for one
    // event, rather than two overloads that would make the Seq rows heterogeneous.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Auto-promote left the parsed CV pending: {BlockReason} (parsed resume {ParsedResumeId}, domain code {BlockDetail})")]
    private static partial void LogLeftPending(
        ILogger logger, AutoPromoteBlockReason blockReason, Guid parsedResumeId,
        string? blockDetail);
}
