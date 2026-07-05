using FluentValidation;
using Jobbliggaren.Application.Resumes.Improvement.FrameApply;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;

/// <summary>
/// Input-shape validation for the EFTER-preview query (Fas 4b PR-7, #656; architect
/// review Minor 1). The SAME shared shape rules as the apply command — a free-echo Text
/// slot is semantically unconstrained, but its transport shape (length, control chars)
/// is bounded identically on both surfaces, closing the preview-side DoS asymmetry.
/// </summary>
public sealed class PreviewCvImprovementQueryValidator : AbstractValidator<PreviewCvImprovementQuery>
{
    public PreviewCvImprovementQueryValidator()
    {
        RuleFor(q => q.ResumeId)
            .NotEmpty().WithMessage("ResumeId krävs.");

        RuleFor(q => q.CriterionId).MustBeCriterionIdShape();
        RuleFor(q => q.FrameId).MustBeFrameIdShape();
        RuleFor(q => q.SlotInputs).MustBeSlotInputsShape();
    }
}
