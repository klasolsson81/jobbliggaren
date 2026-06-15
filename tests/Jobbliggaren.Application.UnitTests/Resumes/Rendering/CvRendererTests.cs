using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Improvement.CvImprovementFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0071/0074) Phase B — the deterministic QuestPDF CV renderer. NO
/// AI/LLM: it renders the user's parsed CV verbatim (only section labels localised, never
/// translated/synthesised — CLAUDE.md §5). ATS-plain + visual come from the SAME
/// <see cref="CvDocumentModel"/> source (BUILD §8.3); both languages (sv/en) are supported.
///
/// The internal sealed <see cref="CvRenderer"/> is constructed directly (Infrastructure exposes
/// internals to this assembly, parity the engine tests). It is pure — takes a decrypted
/// <see cref="ParsedResume"/> and returns bytes; the DEK ownership is the read-handler's job
/// (proven against real Postgres in the Worker integration test). Byte output is pinned
/// deterministic via fixed document metadata; exact byte-equality is avoided (the PDF /ID can
/// vary), so determinism is asserted via stable output size + valid structure.
/// </summary>
public class CvRendererTests
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    private static async Task<RenderedCv> RenderAsync(ParsedResume resume, RenderProfile profile) =>
        await new CvRenderer().RenderAsync(resume, profile, TestContext.Current.CancellationToken);

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length >= 4 && bytes.Take(4).SequenceEqual(PdfMagic);

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task RenderAsync_ShouldProduceAValidNonEmptyPdf_ForARichSwedishCv(RenderProfile profile)
    {
        var rendered = await RenderAsync(Resume(), profile);

        rendered.PdfBytes.ShouldNotBeEmpty();
        IsPdf(rendered.PdfBytes).ShouldBeTrue("Utdata ska vara en giltig PDF (%PDF-magi).");
        rendered.ContentType.ShouldBe("application/pdf");
        rendered.Profile.ShouldBe(profile);
        rendered.Language.ShouldBe(ResumeLanguage.Sv);
    }

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task RenderAsync_ShouldEchoEnglishLanguage_ForAnEnglishCv(RenderProfile profile)
    {
        var resume = Resume(
            detectedLanguage: ResumeLanguage.En,
            profile: "Backend engineer with 8 years building payment platforms.",
            experience: [Experience(title: "Backend Engineer", organization: "Acme Inc", period: "01/2022 – 06/2024",
                rawText: "Led a team of 8 and increased conversion by 23% in 2024.")]);

        var rendered = await RenderAsync(resume, profile);

        rendered.Language.ShouldBe(ResumeLanguage.En);
        IsPdf(rendered.PdfBytes).ShouldBeTrue();
    }

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task RenderAsync_ShouldNotThrowAndStillProduceAPdf_ForADegradedEmptyCv(RenderProfile profile)
    {
        // A degraded parse: empty contact (all null), no profile/experience/education/skills.
        // The renderer must render an honest (near-empty) PDF, never throw, never synthesise a
        // placeholder name (CLAUDE.md §5).
        var degraded = Resume(
            contact: ParsedContact.Empty,
            profile: null,
            experience: [],
            education: [],
            skills: [],
            languages: []);

        var rendered = await RenderAsync(degraded, profile);

        rendered.PdfBytes.ShouldNotBeEmpty();
        IsPdf(rendered.PdfBytes).ShouldBeTrue();
    }

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task RenderAsync_ShouldBeDeterministic_WhenCalledTwiceOnTheSameInput(RenderProfile profile)
    {
        var resume = Resume();

        var first = await RenderAsync(resume, profile);
        var second = await RenderAsync(resume, profile);

        // Pinned metadata ⇒ stable output. Assert stable size (a robust determinism signal that
        // does not flake on the PDF /ID) plus both being valid PDFs.
        second.PdfBytes.Length.ShouldBe(first.PdfBytes.Length,
            "Samma CV ska rendera till samma storlek (deterministisk renderare).");
        IsPdf(first.PdfBytes).ShouldBeTrue();
        IsPdf(second.PdfBytes).ShouldBeTrue();
    }
}
