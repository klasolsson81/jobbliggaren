using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

// Fas 4b PR-9b (ADR 0100 §D3 read-path, DPIA #659 M-F2). Owner-scoped download of a stored
// original CV file (the exact uploaded PDF/DOCX bytes) by its ResumeFileId — the read half of the
// Form C binary store PR-9a wrote. Returns the decrypted original bytes + the server-derived
// content-type + the (already-redacted) filename, or null when the file does not exist for the
// caller.
//
// IRequiresFieldEncryptionKey: the handler decrypts the Form C envelope via IBinaryFieldOpener,
// which peeks the owner DEK FieldEncryptionKeyPrefetchBehavior warms — so the marker is mandatory
// (pinned by an architecture test; without it the opener fails closed at runtime).
// IAuthenticatedRequest gates the query; ownership is enforced fail-closed IN the handler
// (cross-user → null + a failed-access ops event, unknown id → null with NO event — no enumeration
// oracle). The returned bytes are the owner's own file and leave the backend only to the owner's
// browser (M-F2 headers: no-store, nosniff, attachment, fixed content-type).
public sealed record DownloadResumeFileQuery(Guid FileId)
    : IQuery<ResumeFileDownloadDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
