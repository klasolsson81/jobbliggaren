using FluentValidation;
using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

public sealed class ImportResumeCommandValidator : AbstractValidator<ImportResumeCommand>
{
    /// <summary>Hard input-size cap (DoS budget, ADR 0045). A CV is small; 10 MiB is
    /// generous and bounds extraction work before any PDF/DOCX library touches it.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    public ImportResumeCommandValidator()
    {
        RuleFor(c => c.FileName)
            .NotEmpty().WithMessage("Filnamn krävs.")
            .MaximumLength(400).WithMessage("Filnamn får vara max 400 tecken.");

        RuleFor(c => c.ContentType)
            .NotEmpty().WithMessage("Innehållstyp krävs.")
            .MaximumLength(255).WithMessage("Innehållstyp får vara max 255 tecken.");

        RuleFor(c => c.FileBytes)
            .Must(bytes => !bytes.IsEmpty)
            .WithMessage("Filen är tom.")
            .Must(bytes => bytes.Length <= MaxFileBytes)
            .WithMessage("Filen är för stor. Maxstorlek är 10 MB.");
    }
}
