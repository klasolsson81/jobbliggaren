using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

namespace Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>
/// The related section's rows (ADR 0182): an envelope with one member, so the answer can grow without breaking a client
/// the way a top-level array cannot. The rows are the catalogue's six-member <see cref="PublicJobAdSummaryDto"/>, the
/// same type and never a copy: what a public list row may carry is decided once (ADR 0176 D5).
/// <c>RelatedPublicJobAdsDtoContractTests</c> pins both.
/// </summary>
public sealed record RelatedPublicJobAdsDto(IReadOnlyList<PublicJobAdSummaryDto> Items);
