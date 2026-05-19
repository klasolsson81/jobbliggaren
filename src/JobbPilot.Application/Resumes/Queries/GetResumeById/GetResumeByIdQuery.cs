using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Security;
using Mediator;

namespace JobbPilot.Application.Resumes.Queries.GetResumeById;

// IRequiresFieldEncryptionKey: ToDetailDto läser dekrypterad
// ResumeVersion.Content (krypterad text-shadow content_enc, #1c) —
// FieldEncryptionKeyPrefetchBehavior värmer ägar-DEK före materialisering
// (ADR 0049 Mekanik-not 4/6).
public sealed record GetResumeByIdQuery(Guid Id)
    : IQuery<ResumeDetailDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
