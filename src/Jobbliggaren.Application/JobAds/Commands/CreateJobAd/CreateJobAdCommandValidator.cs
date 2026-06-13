using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Commands.CreateJobAd;

public sealed class CreateJobAdCommandValidator : AbstractValidator<CreateJobAdCommand>
{
    public CreateJobAdCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(300);
        RuleFor(c => c.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).NotEmpty();
        RuleFor(c => c.Url)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("URL måste vara en giltig absolut URL.");
        RuleFor(c => c.Source).NotEmpty();
    }
}
