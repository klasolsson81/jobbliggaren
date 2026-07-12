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

        var dto = new CvTemplateCatalogDto(
            [.. CvTemplate.List.Select(t => new TemplateOptionDto(t.Name, t.AtsSafe))],
            [.. CvAccentColor.List.Select(a => new AccentOptionDto(a.Name, hexByName[a.Name]))],
            [.. CvFontPair.List.Select(f => new FontPairOptionDto(f.Name))],
            [.. CvDensity.List.Select(d => new DensityOptionDto(d.Name))]);

        return ValueTask.FromResult(dto);
    }
}
