using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionReference;

/// <summary>
/// GET /api/v1/me/company-watch-criteria/reference — the SCB reference tree the picker renders
/// (CTO Fork G2): SNI 2025 sections → divisions → leaves, and län → kommuner. Same dataset the
/// existence-validator reads (ONE authority), so the picker can never offer a code the write path
/// rejects. Static per deploy → the endpoint serves it with ETag + <c>Cache-Control: private</c>
/// (the taxonomy-endpoint mold; never public — Web Cache Deception).
/// </summary>
public sealed record GetCriterionReferenceQuery()
    : IQuery<CriterionReferenceDto>, IAuthenticatedRequest;

/// <summary>The full picker tree. Version stamps surfaced so a stale FE cache is diagnosable.</summary>
public sealed record CriterionReferenceDto(
    string SniVersion,
    string KommunVersion,
    IReadOnlyList<SniSectionDto> Sni,
    IReadOnlyList<LanDto> Lan);

public sealed record SniSectionDto(
    string Code, string Name, IReadOnlyList<SniDivisionDto> Divisions);

public sealed record SniDivisionDto(
    string Code, string Name, IReadOnlyList<SniLeafDto> Leaves);

public sealed record SniLeafDto(string Code, string Name);

public sealed record LanDto(string Code, string Name, IReadOnlyList<KommunDto> Kommuner);

public sealed record KommunDto(string Code, string Name);
