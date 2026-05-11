using FluentValidation;

namespace JobbPilot.Application.Admin.Queries.GetAuditLogEntries;

public sealed class GetAuditLogEntriesQueryValidator : AbstractValidator<GetAuditLogEntriesQuery>
{
    public GetAuditLogEntriesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);

        // From <= To om båda satta. Tomt date-fält tillåtet (öppen range).
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.From <= q.To)
            .WithName("From")
            .WithMessage("Från-datum får inte vara efter till-datum.");

        // Begränsa fri-text-fält så att ingen kan trigga DOS via lång LIKE-fråga.
        RuleFor(q => q.EventType)
            .MaximumLength(100)
            .When(q => q.EventType is not null);

        RuleFor(q => q.AggregateType)
            .MaximumLength(100)
            .When(q => q.AggregateType is not null);
    }
}
