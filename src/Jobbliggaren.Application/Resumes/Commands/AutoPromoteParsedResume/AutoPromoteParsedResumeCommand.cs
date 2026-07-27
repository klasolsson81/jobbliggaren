using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// Auto-promotes a clean <c>PendingReview</c> <c>ParsedResume</c> verbatim into a canonical
/// <c>Resume</c> — the CV-pivot's "spara direkt" (PR 5a, CTO-bind 2026-07-17; Approach A
/// bound 2026-07-16). Unlike <c>PromoteParsedResumeCommand</c> (whose content is the USER'S
/// gap-filled, human-curated payload) this command derives the content FROM THE PARSE via a
/// bound verbatim projection — the machine promotes only what the file already said, never
/// synthesises (ADR 0071/CLAUDE.md §5), and only when the parse is clean. **What "clean" means
/// NARROWED on 2026-07-27 (#1060), and this sentence is the Art. 22 record of the automated
/// decision, so it has to track it:** no personnummer (fail-closed until the 5b consent path),
/// extraction did not FAIL outright (`ParseConfidence.Overall != Failed` — a `Degraded` parse
/// now promotes; see <c>AutoPromoteBlockReason.ParseNotConfident</c>), and buildable against the
/// canonical <c>ValidateContent</c>. An unclassified preamble no longer blocks: it is carried
/// onto the CV verbatim under its own name (ADR 0109 amendment 2026-07-27), so the user still
/// classifies it and the machine still asserts nothing about what it is.
/// Anything not clean is NOT an error — the artifact stays pending
/// and the caller routes the user to the review flow (<see cref="AutoPromoteOutcome"/>).
///
/// <para><see cref="NameOverride"/> is the optional upload-form value and it is the CV's
/// LABEL only (<c>Resume.Name</c>), never the person's name (#1060). The form sends it only
/// when the user actually typed one, so absent means "no human named this" and the handler
/// generates a non-PII default (<c>ResumeLabelResolver</c>). The person's name inside the
/// content is ALWAYS <c>JobSeeker.DisplayName</c> — never this field, and never the parsed
/// file's contact name (5a CTO-bind R5).</para>
///
/// <para><see cref="IRequiresFieldEncryptionKey"/> is mandatory twice over: the handler
/// reads the parse's encrypted content shadow (Form B decrypt on load) and writes the new
/// Master as encrypted CV-PII (ADR 0074 Invariant 3).</para>
///
/// <para>Deliberately NOT <c>IAuditableCommand</c>: a <c>LeftPending</c> outcome is a
/// <c>Result.Success</c> (R1), so the blanket behavior would audit it — but
/// <c>AuditLogEntry.Create</c> throws on an empty aggregate id, and a promote row for a
/// promote that did not happen would be misreporting (§5). The handler writes the
/// <see cref="AuditEventType"/> row itself, on the <c>Promoted</c> branch only, in the same
/// transaction (GDPR Art. 22 — the automated decision is distinguishable from the
/// human-curated <c>Resume.PromotedFromParsed</c> in the audit log).</para>
/// </summary>
public sealed record AutoPromoteParsedResumeCommand(
    Guid ParsedResumeId,
    string? NameOverride = null)
    : ICommand<Result<AutoPromoteOutcome>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey
{
    /// <summary>Distinct from the user-promote's <c>Resume.PromotedFromParsed</c> so the
    /// audit log can always tell machine-verbatim from human-curated provenance.</summary>
    public const string AuditEventType = "Resume.AutoPromotedFromParsed";
}
