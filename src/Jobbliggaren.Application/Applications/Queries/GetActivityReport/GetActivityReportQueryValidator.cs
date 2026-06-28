using FluentValidation;

namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// Defense-in-depth pre-handler validation (issue #316). Year/Month are
/// both-or-neither (a half-specified pair is a client bug, not a default);
/// when both are present, Month is 1–12 and Year is a sane bound so a malformed
/// <c>?year=0&amp;month=99</c> returns a clean 400, not a handler-time anomaly.
/// </summary>
public sealed class GetActivityReportQueryValidator : AbstractValidator<GetActivityReportQuery>
{
    public GetActivityReportQueryValidator()
    {
        RuleFor(q => q)
            .Must(q => q.Year.HasValue == q.Month.HasValue)
            .WithMessage("Ange både år och månad, eller ingetdera (standard = föregående månad).");

        RuleFor(q => q.Month!.Value)
            .InclusiveBetween(1, 12)
            .When(q => q.Month.HasValue)
            .WithMessage("Månad måste vara mellan 1 och 12.");

        RuleFor(q => q.Year!.Value)
            .InclusiveBetween(2000, 2100)
            .When(q => q.Year.HasValue)
            .WithMessage("År måste vara mellan 2000 och 2100.");
    }
}
