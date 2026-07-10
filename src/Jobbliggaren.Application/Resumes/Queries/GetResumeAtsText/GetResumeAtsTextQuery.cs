using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumeAtsText;

/// <summary>
/// The ATS text view of a canonical Resume (Fas 4b PR-8.2, ADR 0093 §D5(e)/§D8; CTO-bind
/// PR-8 Q3): the shared-linearizer rendering of the master content — "what we generate
/// from your app copy", the same single point of truth the canonical review cites into
/// (the ATS-PDF composes structurally from the same canonical ResumeContent). Deliberately CANONICAL-ONLY: the parsed RawText view
/// ("what a parser reads from YOUR file") is a DIFFERENT claim over a different
/// substrate (D8 pre-promote-only) and was deferred by the CTO — the two claims are
/// never conflated (D5e).
///
/// <para><see cref="IRequiresFieldEncryptionKey"/>: the handler reads the decrypted
/// master content (Form B shadow) to linearize it.</para>
/// </summary>
public sealed record GetResumeAtsTextQuery(Guid ResumeId)
    : IQuery<ResumeAtsTextDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
