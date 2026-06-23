namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// An unconfirmed SSYK occupation-group proposal carried on a parsed CV (F4-3
/// call-site, ADR 0040 Beslut 4: the engine PROPOSES, the user CONFIRMS). A Domain
/// mirror of the Application-layer <c>OccupationCandidate</c> — the aggregate owns
/// its own state and never references an Application type. Non-PII (a taxonomy id +
/// labels): persisted as plain jsonb. Confirmation/persistence-as-confirmed happens
/// later (never in F4-8).
/// <para>
/// <see cref="ApproximateYears"/> (ADR 0079-amendment, exp-per-occ PR-2) is the
/// CV-derived ~years of experience attributed to this occupation group at import — a
/// PROPOSE-ONLY, non-PII integer projection (the raw <c>Period</c> strings stay
/// DEK-encrypted; only the count + concept-id leave the import pipeline). <c>null</c>
/// = "not stated" (no contributing experience entry — e.g. an education-sourced group
/// — or no parseable period). Trailing nullable so the additive jsonb field reads back
/// as null on pre-amendment rows and the ~30 existing call-sites stay unchanged.
/// </para>
/// </summary>
public sealed record ProposedOccupation(
    string ConceptId,
    string Label,
    string MatchedOn,
    int? ApproximateYears = null);
