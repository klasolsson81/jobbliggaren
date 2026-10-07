using FluentValidation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

/// <summary>
/// Per page over the last 7, 30 or 90 days (#1979): how many users rated it, how their latest rating
/// is spread over 1–5, the mean of those ratings, and how many submissions arrived. A submission
/// without a rating counts as a submission and never as a zero.
/// </summary>
public sealed record GetFeedbackSummaryQuery(int Days) : IQuery<FeedbackSummaryDto>, IAdminRequest
{
    public static readonly IReadOnlySet<int> AllowedDays = new HashSet<int> { 7, 30, 90 };
}

public sealed record FeedbackPageSummary(
    string PageKey,
    int Submissions,
    int Raters,
    int Rated1,
    int Rated2,
    int Rated3,
    int Rated4,
    int Rated5,
    decimal? Mean);

public sealed record FeedbackSummaryDto(int Days, IReadOnlyList<FeedbackPageSummary> Pages);

/// <summary>
/// The statistics read (senior-cto-advisor M5): counting the LATEST rating per user and page needs
/// <c>DISTINCT ON</c>, a provider feature, so it sits behind this port.
/// </summary>
public interface IFeedbackRatingSummaryReader
{
    Task<IReadOnlyList<FeedbackPageSummary>> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public sealed class GetFeedbackSummaryQueryValidator : AbstractValidator<GetFeedbackSummaryQuery>
{
    public GetFeedbackSummaryQueryValidator()
    {
        RuleFor(q => q.Days)
            .Must(GetFeedbackSummaryQuery.AllowedDays.Contains)
            .WithMessage("Välj 7, 30 eller 90 dagar.");
    }
}

public sealed class GetFeedbackSummaryQueryHandler(IFeedbackRatingSummaryReader reader, IDateTimeProvider clock)
    : IQueryHandler<GetFeedbackSummaryQuery, FeedbackSummaryDto>
{
    public async ValueTask<FeedbackSummaryDto> Handle(GetFeedbackSummaryQuery query, CancellationToken cancellationToken)
    {
        var pages = await reader.ReadAsync(clock.UtcNow.AddDays(-query.Days), cancellationToken);
        return new FeedbackSummaryDto(query.Days, pages);
    }
}
