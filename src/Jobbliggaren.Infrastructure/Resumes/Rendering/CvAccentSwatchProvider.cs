using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// Exposes the curated accent hexes that live private in <see cref="CvPalette"/> (Fas 4b PR-8b 8b.3)
/// as the template catalog's Infrastructure egress. Drives <c>CvAccentColor.List</c> so every closed
/// accent gets a swatch; <see cref="CvPalette.Accent"/> is fail-loud on an unmapped member (a new
/// <see cref="CvAccentColor"/> added without a hex here throws exactly like the renderer would),
/// so the swatch set can never silently omit or invent an accent. Stateless singleton (parity
/// <c>CvRenderer</c>) — same-assembly access to <see cref="CvPalette"/>'s <c>internal</c> members.
/// </summary>
internal sealed class CvAccentSwatchProvider : ICvAccentSwatchProvider
{
    public IReadOnlyList<CvAccentSwatch> GetSwatches() =>
        [.. CvAccentColor.List.Select(a => new CvAccentSwatch(a.Name, CvPalette.Hex(CvPalette.Accent(a))))];
}
