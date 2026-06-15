namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// An unconfirmed SSYK occupation-group proposal carried on a parsed CV (F4-3
/// call-site, ADR 0040 Beslut 4: the engine PROPOSES, the user CONFIRMS). A Domain
/// mirror of the Application-layer <c>OccupationCandidate</c> — the aggregate owns
/// its own state and never references an Application type. Non-PII (a taxonomy id +
/// labels): persisted as plain jsonb. Confirmation/persistence-as-confirmed happens
/// later (never in F4-8).
/// </summary>
public sealed record ProposedOccupation(
    string ConceptId,
    string Label,
    string MatchedOn);
