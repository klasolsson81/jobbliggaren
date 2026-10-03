namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>Concept identity paired with the exact display evidence kept by the scorer.</summary>
public sealed record class MatchConceptEvidence(string ConceptId, string Display);

/// <summary>One assessed dimension's partition; empty on NotAssessed and Vacuous.</summary>
public sealed record class MatchConceptPartition(
    IReadOnlyList<MatchConceptEvidence> Matched,
    IReadOnlyList<MatchConceptEvidence> Missing);

/// <summary>Read evidence beside the frozen score, never a grading or persistence input.</summary>
public sealed record class FullMatchConceptEvidence(
    MatchConceptPartition SkillOverlap,
    MatchConceptPartition MustHaveCoverage,
    MatchConceptPartition NiceToHaveCoverage);
