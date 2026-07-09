namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One actionable (Fail/Warn) review finding as seen by the engine at reconcile time
/// (Fas 4b PR-8, ADR 0093 §D5(b)) — the criterion id plus the server-derived
/// content-addressed fingerprint that identifies the finding instance. A deliberately
/// thin Domain-side shape: the Application-side reconciler maps engine verdicts to
/// snapshots so <see cref="Resume.ReconcileFindingStatuses"/> never depends on
/// Application review types (Clean Architecture — Domain depends on nothing).
/// </summary>
public sealed record ReviewFindingSnapshot(string CriterionId, string TargetFingerprint);
