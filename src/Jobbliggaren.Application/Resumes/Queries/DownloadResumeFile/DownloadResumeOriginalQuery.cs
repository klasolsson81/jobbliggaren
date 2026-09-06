using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

// Fas 4b PR-9b (ADR 0100 §D3 read-path, DPIA #659 M-F2). Owner-scoped read of the stored ORIGINAL
// CV file (the exact uploaded PDF/DOCX bytes) for one CANONICAL Resume, keyed by its ResumeId —
// the id the CV hub's cards already carry. Returns the decrypted original bytes + the
// server-derived content-type + the (already-redacted) filename, or null when no original exists
// for the caller.
//
// The link is Resume.SourceParsedResumeId (ADR 0100 §D5) → ResumeFile.ParsedResumeId. It is
// resolved off the RESUME, never by reading the ParsedResumes table: the promoted arm of
// ParsedResumeRetentionJob sweeps the staging row but deliberately never the original
// ("graduation", DPIA M-F3), so a canonical resume keeps its file long after the parse row is
// gone. Resolving via ParsedResumes would 404 exactly the promoted CVs this endpoint exists for.
//
// An original is legitimately absent for a template-built resume (no SourceParsedResumeId at all)
// and for imports predating PR-9a. Null is the honest answer for both and the surface renders an
// empty state. A declined personnummer consent is NOT one of the causes here: ParsedResume.Promote
// refuses a parse with a flagged personnummer whatever the user consented to, so every resume that
// has a SourceParsedResumeId at all came from a clean parse, whose original was always captured.
//
// IRequiresFieldEncryptionKey: the handler decrypts the Form C envelope via IBinaryFieldOpener,
// which peeks the owner DEK FieldEncryptionKeyPrefetchBehavior warms — so the marker is mandatory
// (pinned by an architecture test; without it the opener fails closed at runtime).
// IAuthenticatedRequest gates the query; ownership is enforced fail-closed IN the handler
// (cross-user → null + a failed-access ops event, unknown id → null with NO event — no enumeration
// oracle). The returned bytes are the owner's own file and leave the backend only to the owner's
// browser (M-F2 headers: no-store, nosniff, attachment, fixed content-type).
public sealed record DownloadResumeOriginalQuery(Guid ResumeId)
    : IQuery<ResumeFileDownloadDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
