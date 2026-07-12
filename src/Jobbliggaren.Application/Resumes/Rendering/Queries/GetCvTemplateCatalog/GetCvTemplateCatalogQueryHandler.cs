using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.GetCvTemplateCatalog;

/// <summary>
/// Composes the CV template catalog (Fas 4b PR-8b 8b.3, CTO-bind Q2) from the SINGLE authoritative
/// sources: the closed Domain SmartEnums drive which options exist and a template's
/// <c>CvTemplate.AtsSafe</c> (P5 — the FE never re-derives the ATS rule), and the
/// <see cref="ICvAccentSwatchProvider"/> supplies the palette-owned accent hexes. No DbContext, no
/// owner context, no DEK — pure static reference data, so the handler is DB-free and unit-testable
/// over the real provider. The <c>byName</c> lookup is belt-and-braces fail-loud: the swatch provider
/// drives <c>CvAccentColor.List</c> too, so a divergence (an accent without a hex) throws here rather
/// than shipping a colourless swatch.
/// </summary>
public sealed class GetCvTemplateCatalogQueryHandler(ICvAccentSwatchProvider swatches)
    : IQueryHandler<GetCvTemplateCatalogQuery, CvTemplateCatalogDto>
{
    public ValueTask<CvTemplateCatalogDto> Handle(
        GetCvTemplateCatalogQuery query, CancellationToken cancellationToken)
    {
        var hexByName = swatches.GetSwatches().ToDictionary(s => s.Name, s => s.Hex);

        // Ordered by the SmartEnum Value, which is the DOMAIN's declared order — not by Name,
        // which is what SmartEnum.List yields (alphabetical). The distinction is load-bearing for
        // CvDensity: its values encode a monotone scale (Airy=1 → Normal=2 → Compact=3), and a UI
        // that renders the options as a scale (a segmented control) presents "Airy | Compact |
        // Normal" as a nonsensical ordering when sorted by name. The other three carry an intended
        // order too (Klar is the default template; NavyBlue the default accent), so all four are
        // ordered the same way rather than special-casing density.
        var dto = new CvTemplateCatalogDto(
            [.. CvTemplate.List.OrderBy(t => t.Value).Select(t => new TemplateOptionDto(t.Name, t.AtsSafe))],
            [.. CvAccentColor.List.OrderBy(a => a.Value).Select(a => new AccentOptionDto(a.Name, hexByName[a.Name]))],
            [.. CvFontPair.List.OrderBy(f => f.Value).Select(f => new FontPairOptionDto(f.Name))],
            [.. CvDensity.List.OrderBy(d => d.Value).Select(d => new DensityOptionDto(d.Name))]);

        return ValueTask.FromResult(dto);
    }
}
