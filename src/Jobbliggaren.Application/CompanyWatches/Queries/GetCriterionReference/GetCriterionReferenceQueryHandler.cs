using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionReference;

/// <summary>
/// Projects the two immutable catalogs to the picker tree. The DTO is rebuilt per request (the
/// catalogs are in-memory; the tree is ~1100 small records) — the FE-side cost is what matters,
/// and the endpoint's ETag turns repeat fetches into 304s (taxonomy mold).
/// </summary>
public sealed class GetCriterionReferenceQueryHandler(ICriterionReferenceProvider reference)
    : IQueryHandler<GetCriterionReferenceQuery, CriterionReferenceDto>
{
    public ValueTask<CriterionReferenceDto> Handle(
        GetCriterionReferenceQuery query, CancellationToken cancellationToken)
    {
        var sni = reference.Sni;
        var kommuner = reference.Kommuner;

        var leavesByDivision = sni.Leaves
            .GroupBy(static l => l.DivisionCode, StringComparer.Ordinal)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<SniLeafDto>)g
                    .Select(static l => new SniLeafDto(l.Code, l.Name))
                    .ToList(),
                StringComparer.Ordinal);

        var divisionsBySection = sni.Divisions
            .GroupBy(static d => d.SectionCode, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SniDivisionDto>)g
                    .Select(d => new SniDivisionDto(
                        d.Code, d.Name, leavesByDivision.GetValueOrDefault(d.Code, [])))
                    .ToList(),
                StringComparer.Ordinal);

        var sections = sni.Sections
            .Select(s => new SniSectionDto(
                s.Code, s.Name, divisionsBySection.GetValueOrDefault(s.Code, [])))
            .ToList();

        var kommunerByLan = kommuner.Kommuner
            .GroupBy(static k => k.LanCode, StringComparer.Ordinal)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<KommunDto>)g
                    .Select(static k => new KommunDto(k.Code, k.Name))
                    .ToList(),
                StringComparer.Ordinal);

        var lan = kommuner.Lan
            .Select(l => new LanDto(l.Code, l.Name, kommunerByLan.GetValueOrDefault(l.Code, [])))
            .ToList();

        return ValueTask.FromResult(new CriterionReferenceDto(
            sni.Version, kommuner.Version, sections, lan));
    }
}
