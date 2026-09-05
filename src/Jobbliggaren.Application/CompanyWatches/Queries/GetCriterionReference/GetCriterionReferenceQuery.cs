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

/// <summary>The full picker tree. One version stamp per dataset, so a stale FE cache is diagnosable
/// for each of them — <c>DemandVersion</c> is the dated list the alias extract was selected by, and
/// without it on the wire "which demand list produced this coverage?" cannot be answered from a
/// running host at all.</summary>
public sealed record CriterionReferenceDto(
    string SniVersion,
    string KommunVersion,
    string AliasVersion,
    string DemandVersion,
    IReadOnlyList<SniSectionDto> Sni,
    IReadOnlyList<LanDto> Lan);

/// <summary>
/// Search aliases for this node (#1115) — words a user may type that the node's own <c>Name</c>
/// does not contain, carried on every level because an everyday word can name a whole division as
/// readily as one leaf. Empty for most nodes. A LOOKUP AID: it widens what the filter shows, and
/// the selection the picker emits is still the node's own code (#560 bind 4).
/// </summary>
public sealed record SniSectionDto(
    string Code, string Name, IReadOnlyList<SniDivisionDto> Divisions, IReadOnlyList<string> Aliases);

/// <inheritdoc cref="SniSectionDto"/>
public sealed record SniDivisionDto(
    string Code, string Name, IReadOnlyList<SniLeafDto> Leaves, IReadOnlyList<string> Aliases);

/// <inheritdoc cref="SniSectionDto"/>
public sealed record SniLeafDto(string Code, string Name, IReadOnlyList<string> Aliases);

public sealed record LanDto(string Code, string Name, IReadOnlyList<KommunDto> Kommuner);

public sealed record KommunDto(string Code, string Name);
