using Jobbliggaren.Application.Resumes.Rendering.Queries.GetCvTemplateCatalog;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4b PR-8b 8b.3 (CTO-bind Q2) — the template-catalog handler runs over the REAL
/// <c>CvAccentSwatchProvider</c> (this assembly sees Infrastructure internals, like
/// <see cref="CvPaletteTests"/>). Drives every SmartEnum <c>.List</c> so a new member added without a
/// catalog entry fails here, and truth-syncs the two BE-authoritative facts to their single source:
/// a template's AtsSafe to <c>CvTemplate.AtsSafe</c> (P5 — no re-derivation) and an accent's hex to
/// <c>CvPalette</c> (no swatch drift). No DbContext — the handler is pure static reference composition.
/// </summary>
public class GetCvTemplateCatalogQueryHandlerTests
{
    private static readonly GetCvTemplateCatalogQueryHandler Sut = new(new CvAccentSwatchProvider());

    private static async Task<CvTemplateCatalogDto> LoadAsync() =>
        await Sut.Handle(new GetCvTemplateCatalogQuery(), TestContext.Current.CancellationToken);

    public static TheoryData<string> TemplateNames() => Names(CvTemplate.List.Select(t => t.Name));
    public static TheoryData<string> AccentNames() => Names(CvAccentColor.List.Select(a => a.Name));
    public static TheoryData<string> FontPairNames() => Names(CvFontPair.List.Select(f => f.Name));
    public static TheoryData<string> DensityNames() => Names(CvDensity.List.Select(d => d.Name));

    private static TheoryData<string> Names(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (var n in names) data.Add(n);
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateNames))]
    public async Task Catalog_HasEntryForEveryTemplate_WithDomainSourcedAtsSafe(string name)
    {
        var dto = await LoadAsync();

        var entry = dto.Templates.SingleOrDefault(t => t.Name == name);
        entry.ShouldNotBeNull($"Mallkatalogen saknar posten '{name}'.");
        // P5: the catalog's AtsSafe IS the Domain rule, never a re-derivation.
        entry.AtsSafe.ShouldBe(CvTemplate.FromName(name).AtsSafe);
    }

    [Theory]
    [MemberData(nameof(AccentNames))]
    public async Task Catalog_HasEntryForEveryAccent_WithPaletteSourcedHex(string name)
    {
        var dto = await LoadAsync();

        var entry = dto.Accents.SingleOrDefault(a => a.Name == name);
        entry.ShouldNotBeNull($"Accentkatalogen saknar posten '{name}'.");
        // No swatch drift: the hex IS the palette's, contrast-guarded there.
        entry.Hex.ShouldBe(CvPalette.Hex(CvPalette.Accent(CvAccentColor.FromName(name))));
        entry.Hex.ShouldStartWith("#");
        entry.Hex.Length.ShouldBe(7);
    }

    [Theory]
    [MemberData(nameof(FontPairNames))]
    public async Task Catalog_HasEntryForEveryFontPair(string name)
    {
        var dto = await LoadAsync();
        // Emitted even though the builder UI defers TYPSNITT (Klas 2026-07-12) — the catalog is the
        // BE vocabulary, so the font control later lands with zero BE-contract change.
        dto.FontPairs.ShouldContain(f => f.Name == name);
    }

    [Theory]
    [MemberData(nameof(DensityNames))]
    public async Task Catalog_HasEntryForEveryDensity(string name)
    {
        var dto = await LoadAsync();
        dto.Densities.ShouldContain(d => d.Name == name);
    }

    [Fact]
    public async Task Catalog_CountsMatchTheClosedSmartEnumSets_NoExtrasNoOmissions()
    {
        var dto = await LoadAsync();

        dto.Templates.Count.ShouldBe(CvTemplate.List.Count);
        dto.Accents.Count.ShouldBe(CvAccentColor.List.Count);
        dto.FontPairs.Count.ShouldBe(CvFontPair.List.Count);
        dto.Densities.Count.ShouldBe(CvDensity.List.Count);
    }
}
