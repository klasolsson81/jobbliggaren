using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJuniorItJobAdCount;

/// <summary>
/// ADR 0188 — sums <see cref="IJuniorItJobAdQuery.CountByMunicipalityAsync"/> at one instant, the row for ads without a
/// kommun included, which is <c>/junior</c>'s unfiltered total (<c>GetPublicJuniorItJobAdsQueryHandler</c>). It logs
/// nothing (security-auditor m3).
/// </summary>
public sealed class GetJuniorItJobAdCountQueryHandler(IJuniorItJobAdQuery juniorIt, IDateTimeProvider clock)
    : IQueryHandler<GetJuniorItJobAdCountQuery, JuniorItJobAdCountDto>
{
    public async ValueTask<JuniorItJobAdCountDto> Handle(
        GetJuniorItJobAdCountQuery query, CancellationToken cancellationToken)
    {
        var counts = await juniorIt.CountByMunicipalityAsync(clock.UtcNow, cancellationToken);
        return new JuniorItJobAdCountDto(counts.Sum(c => c.Count));
    }
}
