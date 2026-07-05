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

    // ===============================================================
    // Promoted-Resume overload (TD-112 / #202) — render a canonical
    // ResumeContent (structured DateOnly periods, Resume language).
    // ===============================================================

    private static async Task<RenderedCv> RenderAsync(
        ResumeContent content, ResumeLanguage language, RenderProfile profile) =>
        await new CvRenderer().RenderAsync(content, language, profile, TestContext.Current.CancellationToken);

    private static ResumeContent ResumeContentFixture() =>
        new(
            new PersonalInfo("Anna Andersson", "anna@example.se", "070-1234567", "Stockholm"),
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 3, 1), new DateOnly(2024, 6, 1), "Ledde ett team om 8."),
                new Experience("Nuvarande AB", "Teknisk ledare",
                    new DateOnly(2024, 7, 1), null, "Pågående uppdrag."),
            ],
            educations: [new Education("KTH", "Civilingenjör", new DateOnly(2016, 8, 1), new DateOnly(2021, 1, 1))],
            skills: [new Skill("C#", 8), new Skill("PostgreSQL", 5)],
            summary: "Erfaren backend-utvecklare.");

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task RenderAsync_FromResumeContent_ShouldProduceAValidNonEmptyPdf(RenderProfile profile)
    {
        var rendered = await RenderAsync(ResumeContentFixture(), ResumeLanguage.Sv, profile);

        rendered.PdfBytes.ShouldNotBeEmpty();
        IsPdf(rendered.PdfBytes).ShouldBeTrue("Utdata ska vara en giltig PDF (%PDF-magi).");
        rendered.ContentType.ShouldBe("application/pdf");
        rendered.Profile.ShouldBe(profile);
        rendered.Language.ShouldBe(ResumeLanguage.Sv);
    }

    [Fact]
    public async Task RenderAsync_FromResumeContent_ShouldEchoTheResumeLanguage()
    {
        var rendered = await RenderAsync(ResumeContentFixture(), ResumeLanguage.En, RenderProfile.Ats);

        rendered.Language.ShouldBe(ResumeLanguage.En);
        IsPdf(rendered.PdfBytes).ShouldBeTrue();
    }

    [Fact]
    public async Task RenderAsync_FromResumeContent_ShouldNotThrowAndStillProduceAPdf_ForMinimalContent()
    {
        // The promoted Resume always has a full name (domain-validated) but may have no sections —
        // the renderer must render an honest, near-empty PDF, never throw, never synthesise (§5).
        var minimal = ResumeContent.Empty("Bo Bengtsson");

        var rendered = await RenderAsync(minimal, ResumeLanguage.Sv, RenderProfile.Visual);

        rendered.PdfBytes.ShouldNotBeEmpty();
        IsPdf(rendered.PdfBytes).ShouldBeTrue();
    }

    [Fact]
    public async Task RenderAsync_FromResumeContent_ShouldBeDeterministic_WhenCalledTwice()
    {
        var content = ResumeContentFixture();

        var first = await RenderAsync(content, ResumeLanguage.Sv, RenderProfile.Ats);
        var second = await RenderAsync(content, ResumeLanguage.Sv, RenderProfile.Ats);

        second.PdfBytes.Length.ShouldBe(first.PdfBytes.Length,
            "Samma innehåll ska rendera till samma storlek (deterministisk renderare).");
    }

    // ----- FormatPeriod (CTO D1 / Variant A — year-span, en-dash, localised ongoing) -----

    [Fact]
    public void FormatPeriod_ShouldRenderYearSpan_WhenStartAndEndDifferInYear()
    {
        var period = CvDocumentModel.FormatPeriod(
            new DateOnly(2021, 3, 15), new DateOnly(2024, 6, 30), "pågående");

        period.ShouldBe("2021–2024"); // en-dash between years
    }

    [Fact]
    public void FormatPeriod_ShouldCollapseToSingleYear_WhenStartAndEndShareYear()
    {
        var period = CvDocumentModel.FormatPeriod(
            new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), "pågående");

        period.ShouldBe("2021");
    }

    [Theory]
    [InlineData("pågående")]
    [InlineData("present")]
    public void FormatPeriod_ShouldUseLocalisedOngoingToken_WhenEndIsNull(string ongoingLabel)
    {
        var period = CvDocumentModel.FormatPeriod(new DateOnly(2021, 7, 1), null, ongoingLabel);

        period.ShouldBe($"2021–{ongoingLabel}");
    }

    [Fact]
    public void FormatPeriod_ShouldUseEnDash_NeverTheForbiddenEmDash()
    {
        var period = CvDocumentModel.FormatPeriod(
            new DateOnly(2018, 1, 1), new DateOnly(2020, 1, 1), "pågående");

        period.ShouldContain('–');        // en-dash present
        period.ShouldNotContain('—');     // em-dash (U+2014) is forbidden in UI copy (§5)
    }

    [Fact]
    public async Task RenderAsync_FromResumeContent_ShouldThrow_WhenContentIsNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await new CvRenderer().RenderAsync(
                (ResumeContent)null!, ResumeLanguage.Sv, RenderProfile.Ats,
                TestContext.Current.CancellationToken));
    }

    // ----- CvDocumentModel.From(ResumeContent) — Fas 4b superset language projection (#651) -----
    // Since the AppCopy superset (ADR 0095 D-C) the promoted content carries spoken languages, so
    // their NAMES feed the existing languages slot (proficiency + the other superset fields are not
    // rendered yet). This is a pure BCL projection (Phase A) — no PDF render, no I/O.

    [Fact]
    public void From_ResumeContentWithLanguages_ProjectsLanguageNamesIntoModel()
    {
        var content = ResumeContentFixture() with
        {
            Languages =
            [
                new SpokenLanguage("Svenska", LanguageProficiency.Native),
                new SpokenLanguage("Tyska", LanguageProficiency.NotStated),
            ],
        };

        var model = CvDocumentModel.From(content, "pågående");

        // Names only, in order; the proficiency level is not projected (later PR, ADR 0095 D-E).
        model.Languages.ShouldBe(["Svenska", "Tyska"]);
    }

    [Fact]
    public void From_ResumeContentWithoutLanguages_ProjectsEmptyLanguageList()
    {
        // The fixture leaves Languages empty (legacy/degraded content) → honest empty list, not a
        // synthesised placeholder (CLAUDE.md §5). Regression guard for the previously hard-coded [].
        var model = CvDocumentModel.From(ResumeContentFixture(), "pågående");

        model.Languages.ShouldBeEmpty();
    }
}
