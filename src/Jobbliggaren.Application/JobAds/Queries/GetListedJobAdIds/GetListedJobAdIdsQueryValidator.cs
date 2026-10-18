using FluentValidation;
using Jobbliggaren.Application.JobAds.Listing;

namespace Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

public sealed class GetListedJobAdIdsQueryValidator : AbstractValidator<GetListedJobAdIdsQuery>
{
    public GetListedJobAdIdsQueryValidator()
    {
        RuleFor(q => q.Parts)
            .Equal(ListedJobAdSnapshotLimits.PartCount)
            .WithMessage("Antalet delar stämmer inte med webbplatskartan.");

        RuleFor(q => q.Part)
            .InclusiveBetween(1, ListedJobAdSnapshotLimits.PartCount)
            .WithMessage("Delen finns inte i webbplatskartan.");
    }
}
