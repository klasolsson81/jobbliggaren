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
            return LeftPending(blocked.Reason, parsed.Id);

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

    // #1060 D4-REBIND(6): the measurement instrument for D3. Which gate actually stops real
    // uploads is a question the product has never been able to answer from production — #1060
    // itself was diagnosed by reading a dev-DB row by hand — and D3's per-entry decomposition
    // is explicitly waiting on a population measurement. One structured line per LeftPending
    // makes the distribution readable in Seq without a query over CV-PII.
    //
    // NON-PII by construction, and both properties are deliberate: the reason is a closed enum
    // token (never free text, never a field VALUE — the whole point of the enum, see
    // AutoPromoteBlockReason's docblock) and the id is the staging artifact's surrogate key,
    // the same identifier IFailedAccessLogger already logs on this aggregate. No file name, no
    // display name, no parsed content — this handler never logs decrypted content
    // (ADR 0074 Invariant 3, CLAUDE.md §5).
    private Result<AutoPromoteOutcome> LeftPending(
        AutoPromoteBlockReason reason, ParsedResumeId parsedResumeId)
    {
        LogLeftPending(logger, reason, parsedResumeId.Value);
        return Result.Success<AutoPromoteOutcome>(new AutoPromoteOutcome.LeftPending(reason));
    }

    // Information, not Warning: a LeftPending is an expected product state the user resolves,
    // not a fault (the same reason it rides Result.Success). MEL property names come from the
    // placeholder TOKENS, so these read as `BlockReason` / `ParsedResumeId` in Seq.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Auto-promote left the parsed CV pending: {BlockReason} (parsed resume {ParsedResumeId})")]
    private static partial void LogLeftPending(
        ILogger logger, AutoPromoteBlockReason blockReason, Guid parsedResumeId);
}
