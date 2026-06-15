using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;

/// <summary>
/// Explicit Application-boundary mapping <see cref="CvImprovementResult"/> →
/// <see cref="CvImprovementDto"/> (no AutoMapper across the boundary — CLAUDE.md §5). Projects
/// enums to names, reuses the F4-9 <see cref="CitedEvidenceDto"/> tagged evidence shape, and
/// flattens the closed provenance union to a tagged transport shape.
/// </summary>
internal static class CvImprovementDtoMapper
{
    public static CvImprovementDto ToDto(this CvImprovementResult result) =>
        new(
            ClicheListVersion: result.ClicheListVersion,
            VerbMappingVersion: result.VerbMappingVersion,
            RubricVersion: result.RubricVersion.ToString(),
            Profile: result.Profile.ToString(),
            Changes: result.Changes.Select(ToDto).ToList());

    private static ProposedChangeDto ToDto(ProposedChange change) =>
        new(
            TargetId: change.TargetId,
            Kind: change.Kind.ToString(),
            Category: change.Category.ToString(),
            CriterionId: change.CriterionId,
            Evidence: ToEvidenceDto(change.Evidence),
            Replacement: change.Replacement is null
                ? null
                : new ProposedReplacementDto(change.Replacement.Before, change.Replacement.After),
            Operation: change.Operation is null
                ? null
                : new StructuralOperationDto(change.Operation.Kind.ToString(), change.Operation.Target),
            Rationale: change.Rationale,
            Provenance: ToProvenanceDto(change.Provenance));

    private static CitedEvidenceDto ToEvidenceDto(CitedEvidence evidence) => evidence switch
    {
        TextSpanEvidence span => new CitedEvidenceDto(
            Kind: "TextSpan",
            Start: span.Span.Start,
            Length: span.Span.Length,
            Quote: span.Span.Quote,
            Note: span.Note,
            Observation: null),
        StructuralEvidence structural => new CitedEvidenceDto(
            Kind: "Structural",
            Start: null,
            Length: null,
            Quote: null,
            Note: null,
            Observation: structural.Observation),
        _ => throw new ArgumentOutOfRangeException(
            nameof(evidence), evidence.GetType().Name, "Unknown CitedEvidence kind."),
    };

    private static ChangeProvenanceDto ToProvenanceDto(ChangeProvenance provenance) => provenance switch
    {
        KnowledgeBankProvenance kb => new ChangeProvenanceDto(
            Kind: "KnowledgeBank",
            Source: kb.Source,
            Version: kb.Version,
            Key: kb.Key,
            Transform: null),
        StructuralTransformProvenance st => new ChangeProvenanceDto(
            Kind: "StructuralTransform",
            Source: null,
            Version: null,
            Key: null,
            Transform: st.Transform.ToString()),
        _ => throw new ArgumentOutOfRangeException(
            nameof(provenance), provenance.GetType().Name, "Unknown ChangeProvenance kind."),
    };
}
