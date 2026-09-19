using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;

public sealed class ConsumeLoginLinkCommandValidator : AbstractValidator<ConsumeLoginLinkCommand>
{
    public ConsumeLoginLinkCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(128);
    }
}
