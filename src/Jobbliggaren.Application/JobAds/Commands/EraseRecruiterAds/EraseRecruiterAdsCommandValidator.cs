using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// Preconditions for the one command in the product that destroys content for every user.
/// </summary>
public sealed class EraseRecruiterAdsCommandValidator : AbstractValidator<EraseRecruiterAdsCommand>
{
    /// <summary>
    /// The substring channel matches ANY occurrence, so a short identifier is a corpus-wide
    /// destruction primitive: <c>"a"</c> would match essentially every one of the 93 469 ads. The
    /// dry run would show it — but a floor that makes the mistake unrepresentable beats a review
    /// step that merely makes it visible, and this is the one command where that trade is worth it.
    /// Four characters is below any real email, phone or surname and above the danger zone.
    /// </summary>
    public const int MinIdentifierLength = 4;

    public EraseRecruiterAdsCommandValidator()
    {
        RuleFor(c => c.Identifier)
            .NotEmpty()
            .WithMessage("Identifierare är obligatorisk.")
            .MinimumLength(MinIdentifierLength)
            .WithMessage($"Identifieraren måste vara minst {MinIdentifierLength} tecken. "
                + "En kortare sökning matchar i praktiken hela annonsbeståndet.")
            .MaximumLength(320)
            .WithMessage("Identifieraren får vara max 320 tecken.");

        // ── THE MANDATORY DRY RUN ─────────────────────────────────────────────────────────────
        //
        // ⚠ This rule was VACUOUS when first written, and the way it was vacuous is the whole
        // subject of this issue. It read:
        //
        //     RuleFor(c => c.ConfirmedJobAdIds)
        //         .NotNull().When(c => !c.DryRun)
        //         .Must(...).When(c => !c.DryRun && c.ConfirmedJobAdIds is not null);
        //
        // FluentValidation's `.When()` defaults to ApplyConditionTo.AllValidators — it applies to
        // EVERY validator defined in the RuleFor chain, not just the one it follows. So the second
        // When() silently re-scoped the FIRST one, and NotNull() could only run when the value was
        // NOT null. It could never fire. The mandatory dry run — the single control standing
        // between an operator and irreversible corpus-wide destruction — was a control that looked
        // like it worked and never ran. A green test suite agreed with it.
        //
        // That is #842's defect class, reproduced inside #842's own fix. It is fixed by giving
        // each condition its OWN RuleFor, which cannot be re-scoped by a later chain link.
        RuleFor(c => c.ConfirmedJobAdIds)
            .NotNull()
            .When(c => !c.DryRun)
            .WithMessage("Radering kräver att du först kör en testkörning och sedan bekräftar "
                + "vilka annonser den visade. Skicka id:na för de annonser du har granskat.");

        RuleFor(c => c.ConfirmedJobAdIds!)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(c => !c.DryRun && c.ConfirmedJobAdIds is not null)
            .WithMessage("Bekräftade annons-id får inte innehålla dubbletter.");

        RuleFor(c => c.RequestId)
            .NotEmpty()
            .WithMessage("RequestId är obligatoriskt (audit-spårets aggregate-id).");
    }
}
