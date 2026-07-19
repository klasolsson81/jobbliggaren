using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// Computes the at-rest identity fingerprint for one review finding (#692, ADR 0093 §D2(e)):
/// a <b>keyed</b> HMAC-SHA256 over the canonical payload
/// <see cref="Jobbliggaren.Application.Resumes.Review.FindingTargetFingerprint.BuildCanonicalPayload"/>
/// mints (rubric version + criterion id + normalized evidence). The single compute entry point
/// every call site uses; the implementation holds the per-deployment pepper OUTSIDE the database
/// (security-auditor Fas 4b PR-4 Q4 ruling — an UNSALTED digest of short user-written CV quotes is
/// dictionary/brute-forceable if the ledger table leaks in isolation).
/// </summary>
/// <remarks>
/// <b>Why a port, not the old static method:</b> the canonicalization is business knowledge and
/// stays in the Application layer under its existing tests (<c>FindingTargetFingerprint</c>); only
/// the KEY is an Infrastructure secret. So the payload-build lives in Application and the keyed hash
/// is injected — Clean Architecture dependency rule, parity with #544's
/// <see cref="Common.Security.IProtectedIdentityTokenizer"/> (port in Application, HMAC impl in
/// Infrastructure). Output is 64 lowercase hex chars (HMAC-SHA256 = 32 bytes), so the persisted
/// <c>resume_finding_statuses.target_fingerprint</c> column contract is unchanged (no DDL —
/// <see cref="Jobbliggaren.Domain.Resumes.Resume"/>'s validator already accepts it).
/// <para>
/// Always SERVER-derived from the engine's current finding (ADR 0074 Invariant 2 — a client-submitted
/// fingerprint would be forgeable provenance). Deterministic under a fixed pepper, so the reconcile's
/// "same finding still present?" comparison holds.
/// </para>
/// </remarks>
public interface IFindingFingerprinter
{
    /// <summary>
    /// The keyed fingerprint for one (already redacted) criterion verdict. Deterministic given the
    /// pepper; identical to another verdict's fingerprint exactly when their canonical payloads match.
    /// </summary>
    string Compute(RubricVersion rubricVersion, CvCriterionVerdict verdict);
}
