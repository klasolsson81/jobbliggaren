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
/// synthesises (ADR 0071/CLAUDE.md §5), and only when the parse is clean: no personnummer
/// (fail-closed until the 5b consent path), no unclassified preamble (ADR 0109 — only the
/// user classifies), parser-confident, and buildable against the canonical
/// <c>ValidateContent</c>. Anything not clean is NOT an error — the artifact stays pending
/// and the caller routes the user to the review flow (<see cref="AutoPromoteOutcome"/>).
///
/// <para><see cref="NameOverride"/> is the optional 5c form value (prefilled account name,
/// user-editable). Absent, the handler resolves the account holder's
/// <c>JobSeeker.DisplayName</c> — the bound name source (Klas 2026-07-16): the CV's name
/// field is ALWAYS the account holder's name, never the parsed file's contact name.</para>
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
