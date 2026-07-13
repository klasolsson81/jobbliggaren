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
    /// step that makes it merely visible, and this command is exactly where that trade is worth it.
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

        // The mandatory dry run, enforced structurally. You cannot reach the destructive branch
        // without stating what you saw in the non-destructive one.
        RuleFor(c => c.ConfirmedJobAdCount)
            .NotNull()
            .When(c => !c.DryRun)
            .WithMessage("Radering kräver att du först kör en testkörning och sedan bekräftar "
                + "antalet annonser den visade.")
            .GreaterThanOrEqualTo(0)
            .When(c => !c.DryRun && c.ConfirmedJobAdCount.HasValue)
            .WithMessage("Bekräftat antal kan inte vara negativt.");

        RuleFor(c => c.RequestId)
            .NotEmpty()
            .WithMessage("RequestId är obligatoriskt (audit-spårets aggregate-id).");
    }
}
