using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;

/// <summary>
/// Flat transport DTO for a CV-improvement pass (Fas 4 STEG 10, F4-10) — the Application
/// result type (<see cref="CvImprovementResult"/>) never crosses the Application boundary
/// (CLAUDE.md §2.3). Enums are projected to names; the evidence reuses the F4-9
/// <see cref="CitedEvidenceDto"/> tagged shape; the provenance is flattened to a tagged shape
/// so the client can render KB vs structural changes without the discriminated-union type. No
/// opaque total (Goodhart).
/// </summary>
public sealed record CvImprovementDto(
    string ClicheListVersion,
    string VerbMappingVersion,
    string RubricVersion,
    string Profile,
    IReadOnlyList<ProposedChangeDto> Changes);

/// <summary>One proposed change with its cited evidence + provenance, projected for transport.</summary>
public sealed record ProposedChangeDto(
    string TargetId,
    string Kind,
    string Category,
    string? CriterionId,
    CitedEvidenceDto Evidence,
    ProposedReplacementDto? Replacement,
    StructuralOperationDto? Operation,
    string Rationale,
    ChangeProvenanceDto Provenance);

/// <summary>A before→after text edit (null when the change is a pure structural removal).</summary>
public sealed record ProposedReplacementDto(string Before, string After);

/// <summary>A structural operation (null when the change is a pure text replacement).</summary>
public sealed record StructuralOperationDto(string Kind, string Target);

/// <summary>
/// Tagged transport form of <see cref="ChangeProvenance"/>: <c>Kind</c> is "KnowledgeBank" or
/// "StructuralTransform". For "KnowledgeBank" <c>Source</c>/<c>Version</c>/<c>Key</c> are set;
/// for "StructuralTransform" only <c>Transform</c> is set.
/// </summary>
public sealed record ChangeProvenanceDto(
    string Kind,
    string? Source,
    string? Version,
    string? Key,
    string? Transform);
