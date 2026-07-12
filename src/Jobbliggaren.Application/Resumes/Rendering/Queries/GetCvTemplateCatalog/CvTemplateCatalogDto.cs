namespace Jobbliggaren.Application.Resumes.Rendering.Queries.GetCvTemplateCatalog;

/// <summary>
/// The closed, non-PII vocabulary of CV template options (Fas 4b PR-8b 8b.3, CTO-bind Q2) — the
/// single BE source of truth the mallbyggare's pickers consume. Each option carries only its stable
/// member NAME (the FE resolves the Swedish label via next-intl, never a label in this payload) plus
/// the two facts the FE must NOT re-derive: a template's <see cref="TemplateOptionDto.AtsSafe"/>
/// (from the Domain rule <c>CvTemplate.AtsSafe</c> — P5 "surfaces never disagree") and an accent's
/// <see cref="AccentOptionDto.Hex"/> swatch (from the WCAG-guarded <c>CvPalette</c>, so a swatch can
/// never show a colour the PDF does not, nor escape the contrast guard). Static reference data,
/// identical for every user; sourced at runtime from the live SmartEnums + palette (never a build-time
/// FE copy, which would reintroduce the drift the SSOT exists to kill).
/// </summary>
/// <remarks>
/// <see cref="FontPairs"/> is emitted even though the builder UI defers the TYPSNITT control (Klas
/// 2026-07-12 — the serif asset is a follow-up): the catalog is the BE vocabulary, not the UI, so an
/// unrendered group is invisible, and the font control later lands with zero BE-contract change.
/// </remarks>
public sealed record CvTemplateCatalogDto(
    IReadOnlyList<TemplateOptionDto> Templates,
    IReadOnlyList<AccentOptionDto> Accents,
    IReadOnlyList<FontPairOptionDto> FontPairs,
    IReadOnlyList<DensityOptionDto> Densities);

/// <summary>A template choice: its member name + the Domain-sourced ATS-safety verdict.</summary>
public sealed record TemplateOptionDto(string Name, bool AtsSafe);

/// <summary>An accent choice: its member name + the palette-sourced "#RRGGBB" swatch.</summary>
public sealed record AccentOptionDto(string Name, string Hex);

/// <summary>A font-pair choice: its member name (Swedish label resolved FE-side).</summary>
public sealed record FontPairOptionDto(string Name);

/// <summary>A density choice: its member name (Swedish label resolved FE-side).</summary>
public sealed record DensityOptionDto(string Name);
