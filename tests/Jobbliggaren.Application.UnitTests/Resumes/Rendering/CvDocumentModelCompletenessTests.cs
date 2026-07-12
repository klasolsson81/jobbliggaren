using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// PR-8b (8b.0) — the CV read-model and composer must carry the FULL AppCopy superset before the
/// template work (8b.1) builds on them: grouped skills, spoken-language proficiency, and dynamic
/// profession-driven sections. This closes the pre-existing content-loss gap the source documented
/// as deferred ("not rendered yet — a later PR, ADR 0095 D-E"). Two guard altitudes (the CTO bind's
/// stress-test finding): a MODEL-level completeness floor (every field projects) plus the binding
/// RENDERED-fidelity invariant (every field actually reaches the PDF text — loss can happen in a
/// composer branch, not only the projection). NO AI/LLM: content is rendered verbatim (§5).
/// </summary>
public class CvDocumentModelCompletenessTests
{
    // A maximal promoted CV exercising every superset surface at once:
    //  • grouped skills (C#/PostgreSQL, Kubernetes) PLUS an UNGROUPED skill (Ledarskap) — the
    //    remainder that a naive "render only groups" would drop;
    //  • proficiency across the spectrum incl. NotStated (Franska — an honest bare name);
    //  • two dynamic sections with entries + body lines.
    private static ResumeContent Maximal() =>
        new(
            new PersonalInfo("Karin Nyström", "karin@example.se", "070-9876543", "Göteborg"),
            experiences:
            [
                new Experience("Volvo AB", "Systemarkitekt",
                    new DateOnly(2019, 1, 1), new DateOnly(2023, 12, 1), "Ansvarade for plattformen."),
            ],
            educations:
            [
                new Education("Chalmers", "Civilingenjor Datateknik",
                    new DateOnly(2013, 8, 1), new DateOnly(2018, 6, 1)),
            ],
            skills:
            [
                new Skill("C#", 8),
                new Skill("PostgreSQL", 5),
                new Skill("Kubernetes", 3),
                new Skill("Ledarskap", null),
            ],
            summary: "Erfaren systemarkitekt.",
            languages:
            [
                new SpokenLanguage("Svenska", LanguageProficiency.Native),
                new SpokenLanguage("Engelska", LanguageProficiency.Fluent),
                new SpokenLanguage("Franska", LanguageProficiency.NotStated),
            ],
            skillGroups:
            [
                new SkillGroup("Backend och .NET", ["C#", "PostgreSQL"]),
                new SkillGroup("Infrastruktur", ["Kubernetes"]),
            ],
            sections:
            [
                new ResumeSection("Projekt och arbetsprov",
                    [new SectionEntry("Betalplattform", ["Ledde mikrotjanst-migrationen.", "Inforde CICD."])]),
                new ResumeSection("Certifikat",
                    [new SectionEntry("AWS Certified", ["Utfardat 2022."])]),
            ]);

    private static CvDocumentModel From(ResumeContent content) =>
        CvDocumentModel.From(
            content, "pagaende", p => CvRenderStrings.ProficiencyLabel(p, ResumeLanguage.Sv));

    // ---------------------------------------------------------------
    // MODEL floor — every AppCopy field reaches the projection.
    // ---------------------------------------------------------------

    [Fact]
    public void From_ProjectsEverySkill_KeepingTheFlatListAuthoritative()
    {
        var model = From(Maximal());

        // The flat authoritative store (ADR 0095 D-A) is preserved verbatim, in order.
        model.Skills.ShouldBe(["C#", "PostgreSQL", "Kubernetes", "Ledarskap"]);
    }

    [Fact]
    public void From_ProjectsSkillGroups_WithMembersAllPresentInTheFlatList()
    {
        var model = From(Maximal());

        model.SkillGroups.Select(g => g.Name).ShouldBe(["Backend och .NET", "Infrastruktur"]);
        model.SkillGroups.SelectMany(g => g.Members).ShouldBe(["C#", "PostgreSQL", "Kubernetes"]);

        // Overlay invariant: no phantom member — every grouped name exists in the flat store.
        foreach (var member in model.SkillGroups.SelectMany(g => g.Members))
        {
            model.Skills.ShouldContain(member);
        }
    }

    [Fact]
    public void From_ProjectsLanguageProficiency_WithHonestNullForNotStated()
    {
        var model = From(Maximal());

        model.Languages.ShouldBe(
        [
            new CvDocumentModel.LanguageLine("Svenska", "Modersmål"),
            new CvDocumentModel.LanguageLine("Engelska", "Flytande"),
            new CvDocumentModel.LanguageLine("Franska", null), // NotStated → bare name, never fabricated (§5)
        ]);
    }

    [Fact]
    public void From_ProjectsEveryDynamicSection_EntryAndLine_Verbatim()
    {
        var model = From(Maximal());

        model.Sections.Select(s => s.Heading).ShouldBe(["Projekt och arbetsprov", "Certifikat"]);

        var projekt = model.Sections[0];
        projekt.Entries.Single().Title.ShouldBe("Betalplattform");
        projekt.Entries.Single().Lines.ShouldBe(["Ledde mikrotjanst-migrationen.", "Inforde CICD."]);

        var certifikat = model.Sections[1];
        certifikat.Entries.Single().Title.ShouldBe("AWS Certified");
        certifikat.Entries.Single().Lines.ShouldBe(["Utfardat 2022."]);
    }

    // ---------------------------------------------------------------
    // RENDERED fidelity — the binding invariant: every field reaches
    // the PDF text under BOTH profiles (loss can hide in a composer
    // branch, not only the projection). Text is extracted through the
    // repo's own deterministic PdfPig extractor (Infra-internal, no SDK
    // type crosses the port; reused — DRY).
    // ---------------------------------------------------------------

    // Distinctive single tokens (robust to the extractor's word spacing) covering each superset
    // surface: an UNGROUPED skill, a group name, a grouped member, a known + a NotStated language,
    // localised proficiency labels, both dynamic section headings, an entry title, and a body line token.
    private static readonly string[] ExpectedTokens =
    [
        "Ledarskap",              // ungrouped skill — the remainder a naive group-only render drops
        "Backend och .NET",       // skill group name
        "Kubernetes",             // grouped member
        "Svenska", "Modersmål",   // language + localised proficiency
        "Franska",                // NotStated language still rendered (bare name)
        "Flytande",               // proficiency label for Engelska
        "Projekt och arbetsprov", // dynamic section heading 1
        "Certifikat",             // dynamic section heading 2
        "Betalplattform",         // entry title
        "mikrotjanst-migrationen",// body line token
    ];

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task Render_EmitsEveryAppCopyField_IntoThePdfText_ForBothProfiles(RenderProfile profile)
    {
        var rendered = await new CvRenderer().RenderAsync(
            Maximal(), ResumeLanguage.Sv, profile, TestContext.Current.CancellationToken);

        var extracted = new PdfPigOpenXmlCvTextExtractor().Extract(
            rendered.PdfBytes, CvFileKind.Pdf, TestContext.Current.CancellationToken);

        extracted.Status.ShouldBe(CvExtractionStatus.Extracted);

        foreach (var token in ExpectedTokens)
        {
            extracted.RawText.ShouldContain(
                token,
                Case.Insensitive,
                $"'{token}' saknas i den renderade PDF-texten ({profile}) — innehåll får aldrig tappas (P2/P5).");
        }
    }

    [Fact]
    public async Task Render_WithSupersetContent_StaysDeterministic()
    {
        var content = Maximal();

        // Warm the process-global font-subset cache first — an embedded subset's byte-size is stable
        // only once the cache has seen this document's glyphs; another render test can leave the
        // lazily-built, process-global cache mid-build. The guarantee asserted is steady-state.
        _ = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, RenderProfile.Visual, TestContext.Current.CancellationToken);
        var first = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, RenderProfile.Visual, TestContext.Current.CancellationToken);
        var second = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, RenderProfile.Visual, TestContext.Current.CancellationToken);

        second.PdfBytes.Length.ShouldBe(first.PdfBytes.Length,
            "Samma superset-innehåll ska rendera till samma storlek (deterministisk renderare).");
    }
}
