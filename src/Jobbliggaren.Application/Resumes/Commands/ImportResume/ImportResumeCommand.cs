using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

/// <summary>
/// Imports an uploaded CV (PDF/DOCX) and parses it deterministically into a
/// <c>ParsedResume</c> staging artifact (F4-8, ADR 0074 — NO AI/LLM).
///
/// <para><see cref="IRequiresFieldEncryptionKey"/> is mandatory: the handler persists
/// the parsed content + raw text as encrypted CV-PII (Invariant 3), so the owner DEK
/// must be warmed by <c>FieldEncryptionKeyPrefetchBehavior</c> before the SaveChanges
/// interceptor encrypts the shadows.</para>
///
/// <para><see cref="PersonnummerAcknowledged"/> (CV-pivot 5b, DPIA #659 Beslut 2(c)) is the
/// consent signal for storing a personnummer-flagged ORIGINAL FILE: the FE re-POSTs the same
/// file with the flag after the consent dialog. The flag is INERT when the server scan finds
/// no personnummer (the consent stamps are driven by the server's own scan, never the client
/// flag — a blindly-set flag on a clean upload writes no consent evidence), and it never
/// affects parsing, promotion, or the content gates (security-bind B3: consent stores the
/// file only).</para>
/// </summary>
public sealed record ImportResumeCommand(
    string FileName,
    string ContentType,
    ReadOnlyMemory<byte> FileBytes,
    bool PersonnummerAcknowledged = false)
    : ICommand<Result<ImportResumeResponse>>, IAuthenticatedRequest, IRequiresFieldEncryptionKey,
      IAuditableCommand<Result<ImportResumeResponse>>
{
    /// <summary>The distinct Art. 7(1) audit event for a consented flagged-file capture —
    /// written IN-HANDLER on that branch only (the blanket behavior's one event slot is
    /// <see cref="EventType"/>; 5b CTO-bind M-D). Aggregate = the ResumeFile, not the parse.</summary>
    public const string PnrConsentAuditEventType = "ResumeFile.PnrStorageConsented";

    public string EventType => "Resume.Imported";

    public string AggregateType => "ParsedResume";

    public Guid ExtractAggregateId(Result<ImportResumeResponse> response) =>
        response.IsSuccess ? response.Value.ParsedResumeId : Guid.Empty;
}
