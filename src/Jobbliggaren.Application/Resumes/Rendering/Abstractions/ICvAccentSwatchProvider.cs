namespace Jobbliggaren.Application.Resumes.Rendering.Abstractions;

/// <summary>
/// The template catalog's ONLY piece that physically lives in Infrastructure (Fas 4b PR-8b 8b.3):
/// the curated accent hex colours, which are private inside <c>CvPalette</c> (Infrastructure). This
/// port exposes exactly that Infra-owned egress (name → hex); the template/font/density names and
/// the per-template ATS-safety flag are all Domain-derivable and are composed in the catalog handler
/// — they never cross a port. ISP/SRP: a thin seam over the one thing that is not Domain-derivable,
/// parity <see cref="IFrameProvider"/> (Application port + Application record + Infrastructure impl
/// reading Infra-resident data). Deliberately decoupled from the catalog DTO (its own record) so the
/// port changes for palette reasons and the catalog for catalog reasons (CCP).
/// </summary>
public interface ICvAccentSwatchProvider
{
    /// <summary>The curated accent swatches (one per <c>CvAccentColor</c> member), name → hex.</summary>
    IReadOnlyList<CvAccentSwatch> GetSwatches();
}

/// <summary>A curated accent's display swatch — its closed-set name and its "#RRGGBB" hex.</summary>
public sealed record CvAccentSwatch(string Name, string Hex);
