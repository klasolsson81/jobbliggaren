using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetApplicationById;

// IRequiresFieldEncryptionKey: aggregatet bär krypterade kolumner
// (cover_letter/notes.content/follow_ups.note) — FieldEncryptionKeyPrefetchBehavior
// värmer ägar-DEK före materialisering (ADR 0049 Mekanik-not 3/4).
public sealed record GetApplicationByIdQuery(Guid Id)
    : IQuery<ApplicationDetailDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
