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

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// The "spara direkt" mechanism (CV-pivot PR 5a, CTO-bind 2026-07-17). Flow: resolve owner
/// (id + display name in one projection) → owner-scoped tracked load (IDOR fail-closed,
/// parity <c>PromoteParsedResumeCommandHandler</c>) → the TWO policy gates, cheapest and
/// highest-PII-priority first (personnummer → extraction failure) → project the
/// parse verbatim to the transport shape → the shared personnummer guard (DQ6; the one text
/// this composition adds over the import-scanned raw superset is the account display name)
/// → <c>ResumeContentMapper.ToDomain</c> → <c>Resume.CreateFromParsed</c> (the ONE
/// buildability authority — its failure is the honest "content insufficient, user reviews",
/// never re-encoded here) → <c>ParsedResume.Promote</c> (aggregate owns the gate) → add →
/// reconciler-seed → audit → <c>Promoted</c>.
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
public sealed class AutoPromoteParsedResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger,
    IResumeReviewReconciler reconciler,
    ICorrelationIdProvider correlationIdProvider,
    IRequestContextProvider requestContextProvider)
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

        // ── Tier 1: the two POLICY gates (CTO-bind §2; narrowed from three by #1060's D1
        // bind) — both read-only, both before any mutation. Order: highest PII priority
        // first, then extraction failure.
        if (parsed.Personnummer.Found)
            return LeftPending(AutoPromoteBlockReason.PersonnummerPresent);

        // #1060 D1.3: `Failed` ONLY, not `RequiresManualReview`. A `Degraded` parse found
        // something and is honest that the document was messy — under ADR 0112 that is the CV
        // the reviewer has most to say about, so blocking it gives the least product to the
        // user who needs it most. `Failed` still blocks: extraction produced nothing usable, so
        // the promote would build a CV that says less than the file did. The enum member's
        // docblock records the reversal of the 5a bind's R3 — read it before re-tightening.
        //
        // #1060 D1.2: the preamble gate that stood between these two is RETIRED. Text above the
        // first heading no longer blocks; it rides the source parse and is shown back on the
        // promoted CV's review surface (ADR 0109 amendment 2026-07-27). Nothing is minted here —
        // AutoPromoteContentMapper still never maps Preamble into Summary or a Section, which is
        // the prohibition the gate was standing in for, enforced where it belongs.
        if (parsed.Confidence.Overall == OverallConfidenceLevel.Failed)
            return LeftPending(AutoPromoteBlockReason.ParseNotConfident);

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

        // A personnummer in the LABEL is a personnummer presence, not "incomplete content" —
        // `Resume.ValidateName` would refuse it too, but as a buildability failure, which
        // would mis-report the reason (§5: never mis-report a verdict).
        if (PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(label)).Count > 0)
            return LeftPending(AutoPromoteBlockReason.PersonnummerPresent);

        // ── Tier 2: buildability, through the ONE existing promote pipeline.
        var dto = AutoPromoteContentMapper.ToContentDto(parsed.Content, personName);

        // DQ6 on the COMPOSED content (arch-tripwire-required for every CreateFromParsed
        // caller). The import scan covered the raw-text superset of everything the parse
        // structured, so the one genuinely new text here is the account display name — a
        // personnummer riding in it is caught HERE, and the disposition is the same honest
        // "pending, review" (it is a personnummer presence, whichever field carries it).
        var guard = ResumeContentPersonnummerGuard.Check(dto);
        if (guard.IsFailure)
            return LeftPending(AutoPromoteBlockReason.PersonnummerPresent);

        var content = ResumeContentMapper.ToDomain(dto);
        var created = Resume.CreateFromParsed(owner.Id, label, content, parsed.Id, clock);
        if (created.IsFailure)
            return LeftPending(AutoPromoteBlockReason.IncompleteContent);

        // ── Mutations begin. The aggregate owns the promote gate (PendingReview + no
        // flagged personnummer); the personnummer half was re-verified by the policy gate
        // above, and PendingReview is guaranteed structurally by the query filter (a
        // Promoted/Discarded row is soft-deleted and reads as NotFound) — so a failure
        // here is a genuine (e.g. concurrent) inconsistency and propagates as a real
        // Failure, not a LeftPending.
        var promotion = parsed.Promote(clock);
        if (promotion.IsFailure)
            return Result.Failure<AutoPromoteOutcome>(promotion.Error);

        var resume = created.Value;
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

    private static Result<AutoPromoteOutcome> LeftPending(AutoPromoteBlockReason reason) =>
        Result.Success<AutoPromoteOutcome>(new AutoPromoteOutcome.LeftPending(reason));
}
