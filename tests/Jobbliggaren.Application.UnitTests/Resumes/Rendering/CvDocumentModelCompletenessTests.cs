using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
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

    private static string Extract(byte[] pdf) =>
        new PdfPigOpenXmlCvTextExtractor()
            .Extract(pdf, CvFileKind.Pdf, TestContext.Current.CancellationToken).RawText;

    private static async Task<string> RenderTextAsync(ResumeContent content, RenderProfile profile)
    {
        var rendered = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, profile, TestContext.Current.CancellationToken);
        return Extract(rendered.PdfBytes);
    }

    private static ResumeContent WithSections(params ResumeSection[] sections) =>
        new(new PersonalInfo("Test Person", null, null, null), sections: sections);

    private static ResumeContent WithSkills(IReadOnlyList<Skill> skills, IReadOnlyList<SkillGroup> groups) =>
        new(new PersonalInfo("Test Person", null, null, null), skills: skills, skillGroups: groups);

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

        var first = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, RenderProfile.Visual, TestContext.Current.CancellationToken);
        var second = await new CvRenderer().RenderAsync(
            content, ResumeLanguage.Sv, RenderProfile.Visual, TestContext.Current.CancellationToken);

        // Content determinism — byte-identity is impossible (QuestPDF /ID + font-subset packing);
        // the extracted text is the honest, parallel-safe determinism signal (§5, FixedTimestamp).
        Extract(second.PdfBytes).ShouldBe(Extract(first.PdfBytes),
            "Samma superset-innehåll ska rendera till samma innehåll (deterministisk renderare).");
    }

    // ---------------------------------------------------------------
    // Proficiency localisation — every STATED level maps to a non-null
    // label in both languages (a growing SmartEnum must not silently
    // fall through to a bare name — content degradation, P2/P5). Guards
    // the `_ => null` fallback in ProficiencyLabel.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProficiencyLabel_ReturnsNonNullLabel_ForEveryStatedLevel(bool swedish)
    {
        var language = swedish ? ResumeLanguage.Sv : ResumeLanguage.En;
        foreach (var level in LanguageProficiency.List.Where(l => l != LanguageProficiency.NotStated))
        {
            CvRenderStrings.ProficiencyLabel(level, language).ShouldNotBeNullOrWhiteSpace(
                $"Nivån '{level.Name}' ({language.Name}) saknar etikett — en tyst content-degradation (P2/P5).");
        }
    }

    [Theory]
    [InlineData(true, "Grundläggande", "God", "Flytande", "Modersmål")]
    [InlineData(false, "Basic", "Good", "Fluent", "Native")]
    public void ProficiencyLabel_MapsEachLevel_ToItsLocalisedLabel(
        bool swedish, string basic, string good, string fluent, string native)
    {
        var language = swedish ? ResumeLanguage.Sv : ResumeLanguage.En;
        CvRenderStrings.ProficiencyLabel(LanguageProficiency.Basic, language).ShouldBe(basic);
        CvRenderStrings.ProficiencyLabel(LanguageProficiency.Good, language).ShouldBe(good);
        CvRenderStrings.ProficiencyLabel(LanguageProficiency.Fluent, language).ShouldBe(fluent);
        CvRenderStrings.ProficiencyLabel(LanguageProficiency.Native, language).ShouldBe(native);
        CvRenderStrings.ProficiencyLabel(LanguageProficiency.NotStated, language).ShouldBeNull(); // honest omission
    }

    // ---------------------------------------------------------------
    // Skill grouping edge cases — every skill reaches the PDF once,
    // regardless of grouping shape. Loss/double-render can only hide in
    // the composer's remainder branch, so these assert rendered output.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Render_UngroupedSkillsOnly_TheCommonCase_AllReachThePdf()
    {
        var content = WithSkills([new Skill("C#", null), new Skill("Rust", null), new Skill("Go", null)], []);

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        foreach (var skill in new[] { "C#", "Rust", "Go" })
        {
            text.ShouldContain(skill, Case.Insensitive, $"Ogrupperad kompetens '{skill}' tappades.");
        }
    }

    [Fact]
    public void From_SkillSharedByTwoGroups_ProjectsIntoBoth_AndKeepsFlatStore()
    {
        var content = WithSkills(
            [new Skill("C#", null), new Skill("SQL", null)],
            [new SkillGroup("Backend", ["C#", "SQL"]), new SkillGroup("Data", ["SQL"])]);

        var model = From(content);

        // Faithful to the user's grouping — SQL is shown in both groups; the flat store is preserved.
        model.SkillGroups[0].Members.ShouldContain("SQL");
        model.SkillGroups[1].Members.ShouldContain("SQL");
        model.Skills.ShouldBe(["C#", "SQL"]);
    }

    [Fact]
    public async Task Render_GroupsCoverAllSkills_NoUngroupedRemainderRow_ButNoLoss()
    {
        // All skills are grouped → the ungrouped remainder is empty; every skill still reaches the PDF.
        var content = WithSkills(
            [new Skill("C#", null), new Skill("SQL", null)],
            [new SkillGroup("Backend och .NET", ["C#", "SQL"])]);

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("Backend och .NET", Case.Insensitive);
        text.ShouldContain("C#", Case.Insensitive);
        text.ShouldContain("SQL", Case.Insensitive);
    }

    [Fact]
    public async Task Render_GroupMemberNotInFlatSkills_StillRenders_NeverDropped()
    {
        // Defensive: a member absent from Skills[] (Resume.ValidateContent rejects this upstream, but
        // the renderer must never DROP content). It renders in its group; the remainder is unaffected.
        var content = WithSkills(
            [new Skill("C#", null)],
            [new SkillGroup("Backend", ["C#", "Phantom"])]);

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("Phantom", Case.Insensitive);
        text.ShouldContain("C#", Case.Insensitive);
    }

    [Fact]
    public async Task Render_BlankGroupName_RendersMembersWithoutPrefix_NoLoss()
    {
        var content = WithSkills(
            [new Skill("C#", null), new Skill("SQL", null)],
            [new SkillGroup("   ", ["C#", "SQL"])]);

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("C#", Case.Insensitive);
        text.ShouldContain("SQL", Case.Insensitive);
    }

    [Fact]
    public async Task Render_AllWhitespaceGroupMembers_GroupOmitted_UngroupedSkillStillRenders()
    {
        // A group whose members are all whitespace contributes nothing (nothing to show), but a real
        // ungrouped skill alongside it must still render — no collateral loss.
        var content = WithSkills(
            [new Skill("Ledarskap", null)],
            [new SkillGroup("Tom grupp", ["  ", ""])]);

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("Ledarskap", Case.Insensitive);
    }

    // ---------------------------------------------------------------
    // Dynamic-section edge cases — a section/entry that is partially
    // empty must render whatever content it DOES carry, never drop it.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Render_DynamicSection_HeadingWithoutEntries_StillRendersHeading()
    {
        var content = WithSections(new ResumeSection("Referenser", []));

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("Referenser", Case.Insensitive);
    }

    [Fact]
    public async Task Render_DynamicEntry_TitleWithoutLines_StillRendersTitle()
    {
        var content = WithSections(
            new ResumeSection("Certifikat", [new SectionEntry("AWS Certified", [])]));

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("AWS Certified", Case.Insensitive);
    }

    [Fact]
    public async Task Render_DynamicEntry_LinesWithoutTitle_StillRendersLines()
    {
        var content = WithSections(
            new ResumeSection("Övrigt", [new SectionEntry("", ["Körkort B", "Volontärarbete"])]));

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        text.ShouldContain("Körkort B", Case.Insensitive);
        text.ShouldContain("Volontärarbete", Case.Insensitive);
    }

    [Fact]
    public async Task Render_DynamicEntry_AllWhitespaceLines_EntrySkipped_OtherContentSurvives()
    {
        var content = WithSections(
            new ResumeSection("Projekt", [new SectionEntry("Riktigt projekt", ["  ", ""])]));

        var text = await RenderTextAsync(content, RenderProfile.Ats);

        // The all-whitespace lines contribute nothing, but the entry title (real content) survives.
        text.ShouldContain("Riktigt projekt", Case.Insensitive);
    }

    // ---------------------------------------------------------------
    // Parsed staging path — carries no groups/proficiency/sections, so
    // those project empty/null (an honest partial, never fabricated).
    // ---------------------------------------------------------------

    [Fact]
    public void From_ParsedContent_ProjectsEmptyGroupsAndSections_AndNullProficiency()
    {
        var parsed = new ParsedResumeContent(
            new ParsedContact("Parsed Person", null, null, null),
            skills: ["C#", "SQL"],
            languages: ["Svenska", "Engelska"]);

        var model = CvDocumentModel.From(parsed);

        model.Skills.ShouldBe(["C#", "SQL"]);
        model.SkillGroups.ShouldBeEmpty();
        model.Sections.ShouldBeEmpty();
        model.Languages.ShouldBe(
        [
            new CvDocumentModel.LanguageLine("Svenska", null),
            new CvDocumentModel.LanguageLine("Engelska", null),
        ]);
    }
}
