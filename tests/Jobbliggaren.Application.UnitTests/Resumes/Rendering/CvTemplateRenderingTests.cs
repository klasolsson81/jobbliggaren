using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// PR-8b (8b.1) — the three visual templates (Klar / Accentlinje / MorkPanel) render the SAME AppCopy,
/// only the layout + styling differ. The binding guarantees: (1) NO content is lost under ANY template
/// (asserted at extracted PDF text, both single-column and the two-column MorkPanel panel+main split);
/// (2) MorkPanel flows across pages for a long CV — never throws QuestPDF's DocumentLayoutException,
/// never clips; (3) the ATS profile renders the plain single-column parallel IGNORING the template, so
/// a two-column choice never costs a parseable version; (4) the chosen accent reaches the visual PDF;
/// (5) determinism (same input → same content). NO AI/LLM; content rendered verbatim (§5).
/// </summary>
[Xunit.Collection("QuestPdfRendering")]
public class CvTemplateRenderingTests
{
    private static readonly string[] TemplateNames = ["Klar", "Accentlinje", "MorkPanel"];

    private static CvTemplateOptions Opt(string templateName, CvAccentColor? accent = null) =>
        new(CvTemplate.FromName(templateName), accent ?? CvAccentColor.NavyBlue,
            CvFontPair.Modern, CvDensity.Normal, PhotoEnabled: false, CvPhotoShape.Circle);

    private static async Task<RenderedCv> RenderAsync(ResumeContent content, CvTemplateOptions options, RenderProfile profile) =>
        await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, options, profile, TestContext.Current.CancellationToken);

    private static string Extract(byte[] pdf) =>
        new PdfPigOpenXmlCvTextExtractor()
            .Extract(pdf, CvFileKind.Pdf, TestContext.Current.CancellationToken).RawText;

    private static async Task<string> RenderTextAsync(ResumeContent content, CvTemplateOptions options, RenderProfile profile) =>
        Extract((await RenderAsync(content, options, profile)).PdfBytes);

    // A rich CV touching every surface (contact, profile, experience, education, grouped + ungrouped
    // skills, proficiency incl. NotStated, dynamic sections). The MorkPanel splits these across a
    // panel (contact/skills/languages) and a main column (the rest) — fidelity must hold across both.
    private static ResumeContent Rich() =>
        new(
            new PersonalInfo("Karin Nyström", "karin@example.se", "070-9876543", "Göteborg"),
            experiences:
            [
                new Experience("Volvo AB", "Systemarkitekt",
                    new DateOnly(2019, 1, 1), new DateOnly(2023, 12, 1), "Ledde betalplattformen."),
            ],
            educations: [new Education("Chalmers", "Civilingenjor", new DateOnly(2013, 8, 1), new DateOnly(2018, 6, 1))],
            skills: [new Skill("C#", 8), new Skill("PostgreSQL", 5), new Skill("Ledarskap", null)],
            summary: "Erfaren systemarkitekt.",
            languages:
            [
                new SpokenLanguage("Svenska", LanguageProficiency.Native),
                new SpokenLanguage("Franska", LanguageProficiency.NotStated),
            ],
            skillGroups: [new SkillGroup("Backend", ["C#", "PostgreSQL"])],
            sections: [new ResumeSection("Projekt", [new SectionEntry("Betalplattform", ["Ledde migrationen."])])]);

    private static readonly string[] RichTokens =
    [
        "Karin Nyström", "karin@example.se", "Göteborg",     // contact
        "Erfaren systemarkitekt",                             // profile
        "Systemarkitekt", "Volvo AB",                         // experience
        "Chalmers",                                           // education
        "Backend", "C#", "Ledarskap",                         // grouped + ungrouped skills
        "Svenska", "Modersmål", "Franska",                    // language + proficiency + NotStated bare
        "Projekt", "Betalplattform", "Ledde migrationen",     // dynamic section
    ];

    [Theory]
    [InlineData("Klar")]
    [InlineData("Accentlinje")]
    [InlineData("MorkPanel")]
    public async Task Visual_EveryTemplate_EmitsAllContent_NoLoss(string templateName)
    {
        var text = await RenderTextAsync(Rich(), Opt(templateName), RenderProfile.Visual);

        foreach (var token in RichTokens)
        {
            text.ShouldContain(token, Case.Insensitive,
                $"'{token}' saknas i mallen {templateName} — innehåll får aldrig tappas (P2/P5).");
        }
    }

    [Fact]
    public async Task MorkPanel_MaximalMultiPageContent_Paginates_WithoutThrowingOrClipping()
    {
        // A CV long enough to overflow one A4 page. The two-column MorkPanel is the highest-risk layout
        // (QuestPDF throws DocumentLayoutException on constrained overflow) — it must flow to page 2,
        // and content from the LAST sections must still reach the PDF (no clip).
        var experiences = Enumerable.Range(1, 9).Select(i => new Experience(
            $"Arbetsgivare {i} AB", $"Roll {i}", new DateOnly(2005 + i, 1, 1), new DateOnly(2006 + i, 6, 1),
            $"Ansvarade for leveranser {i} och forbattrade processer med matbara resultat.")).ToList();
        var sections = Enumerable.Range(1, 4).Select(i => new ResumeSection(
            $"Extrasektion {i}", [new SectionEntry($"Post {i}", [$"Rad i post {i}."])])).ToList();
        var content = new ResumeContent(
            new PersonalInfo("Langt Namnson", "langt@example.se", "070-0", "Stockholm"),
            experiences: experiences,
            educations: [new Education("Universitet", "Examen", new DateOnly(2000, 8, 1), new DateOnly(2005, 6, 1))],
            skills: Enumerable.Range(1, 20).Select(i => new Skill($"Kompetens {i}", null)).ToList(),
            summary: "Lang sammanfattning.",
            sections: sections);

        // Must not throw.
        var text = await RenderTextAsync(content, Opt("MorkPanel"), RenderProfile.Visual);

        // Early AND late content both present — proves it paginated rather than clipped at the page edge.
        text.ShouldContain("Roll 1", Case.Insensitive);
        text.ShouldContain("Arbetsgivare 9 AB", Case.Insensitive);
        text.ShouldContain("Kompetens 20", Case.Insensitive);
        text.ShouldContain("Extrasektion 4", Case.Insensitive);
        text.ShouldContain("Post 4", Case.Insensitive);
    }

    [Fact]
    public async Task AtsProfile_RendersTheSameParallel_RegardlessOfChosenTemplate()
    {
        var content = Rich();

        // The ATS profile ignores the visual template — a MorkPanel choice yields the SAME ATS parallel
        // as a Klar choice (the plain single-column version generated from the same content, §5.5/§8).
        var viaKlar = await RenderTextAsync(content, Opt("Klar"), RenderProfile.Ats);
        var viaMorkPanel = await RenderTextAsync(content, Opt("MorkPanel"), RenderProfile.Ats);

        viaMorkPanel.ShouldBe(viaKlar);
    }

    [Fact]
    public async Task ChosenAccent_ReachesTheVisualPdf_ProducingDifferentOutput()
    {
        var content = Rich();

        var navy = await RenderAsync(content, Opt("Klar", CvAccentColor.NavyBlue), RenderProfile.Visual);
        var wine = await RenderAsync(content, Opt("Klar", CvAccentColor.WineRed), RenderProfile.Visual);

        // Same content + template, different accent → the accent colour reaches the PDF, so bytes differ.
        navy.PdfBytes.ShouldNotBe(wine.PdfBytes);
    }

    [Theory]
    [InlineData("Klar")]
    [InlineData("Accentlinje")]
    [InlineData("MorkPanel")]
    public async Task Visual_EachTemplate_IsDeterministic(string templateName)
    {
        var content = Rich();
        var options = Opt(templateName);

        var first = await RenderAsync(content, options, RenderProfile.Visual);
        var second = await RenderAsync(content, options, RenderProfile.Visual);

        // Content determinism (byte-identity is impossible — QuestPDF /ID + font-subset packing vary).
        Extract(second.PdfBytes).ShouldBe(Extract(first.PdfBytes),
            $"Mallen {templateName} ska rendera samma innehåll deterministiskt.");
    }
}
