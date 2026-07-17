using Jobbliggaren.Application.Resumes;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes;

// Fas 4 STEG A PR-2 — the shared ResumeContentDto → ResumeContent mapper, extracted from
// UpdateMasterContentCommandHandler.MapToDomain (DRY; CC refactors that handler to call it).
// Pure mapping, no I/O. SPEC-DRIVEN. RED until ResumeContentMapper.ToDomain ships.
public class ResumeContentMapperTests
{
    [Fact]
    public void ToDomain_MapsEveryField_OneToOne()
    {
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
            Experiences:
            [
                new ExperienceDto("Beta AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30), "Byggde betaltjänster."),
            ],
            Educations:
            [
                new EducationDto("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
            ],
            Skills:
            [
                new SkillDto("C#", 8),
                new SkillDto("PostgreSQL", null),
            ],
            Summary: "Erfaren backend-utvecklare.");

        var content = ResumeContentMapper.ToDomain(dto);

        content.PersonalInfo.FullName.ShouldBe("Anna Andersson");
        content.PersonalInfo.Email.ShouldBe("anna@example.com");
        content.PersonalInfo.Phone.ShouldBe("0701234567");
        content.PersonalInfo.Location.ShouldBe("Stockholm");
        content.Summary.ShouldBe("Erfaren backend-utvecklare.");

        var exp = content.Experiences.ShouldHaveSingleItem();
        exp.Company.ShouldBe("Beta AB");
        exp.Role.ShouldBe("Backend-utvecklare");
        exp.StartDate.ShouldBe(new DateOnly(2021, 1, 1));
        exp.EndDate.ShouldBe(new DateOnly(2024, 6, 30));
        exp.Description.ShouldBe("Byggde betaltjänster.");

        var edu = content.Educations.ShouldHaveSingleItem();
        edu.Institution.ShouldBe("KTH");
        edu.Degree.ShouldBe("Civilingenjör");
        edu.StartDate.ShouldBe(new DateOnly(2013, 9, 1));
        edu.EndDate.ShouldBe(new DateOnly(2018, 6, 1));

        content.Skills.Count.ShouldBe(2);
        content.Skills[0].Name.ShouldBe("C#");
        content.Skills[0].YearsExperience.ShouldBe(8);
        content.Skills[1].Name.ShouldBe("PostgreSQL");
        content.Skills[1].YearsExperience.ShouldBeNull();
    }

    [Fact]
    public void ToDomain_WithEmptyCollections_ProducesEmptyCollections_NullSummary()
    {
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: null);

        var content = ResumeContentMapper.ToDomain(dto);

        content.PersonalInfo.FullName.ShouldBe("Anna Andersson");
        content.Experiences.ShouldBeEmpty();
        content.Educations.ShouldBeEmpty();
        content.Skills.ShouldBeEmpty();
        content.Summary.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Fas 4b AppCopy superset (#651, ADR 0095 D-A/B/C) — Languages/SkillGroups/Sections
    // ---------------------------------------------------------------

    [Fact]
    public void ToDomain_MapsSupersetFields_OneToOne()
    {
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [new SkillDto("C#", 8), new SkillDto("PostgreSQL", null)],
            Summary: null,
            Languages:
            [
                new SpokenLanguageDto("Svenska", "Native"),
                new SpokenLanguageDto("Tyska", "NotStated"),
            ],
            SkillGroups: [new SkillGroupDto("Backend", ["C#", "PostgreSQL"])],
            Sections:
            [
                new ResumeSectionDto("Projekt", [new SectionEntryDto("Betalplattform", ["Rad 1", "Rad 2"])]),
            ]);

        var content = ResumeContentMapper.ToDomain(dto);

        content.Languages.Count.ShouldBe(2);
        content.Languages[0].Name.ShouldBe("Svenska");
        content.Languages[0].Proficiency.ShouldBe(LanguageProficiency.Native);
        content.Languages[1].Name.ShouldBe("Tyska");
        content.Languages[1].Proficiency.ShouldBe(LanguageProficiency.NotStated);

        var group = content.SkillGroups.ShouldHaveSingleItem();
        group.Name.ShouldBe("Backend");
        group.Members.ShouldBe(["C#", "PostgreSQL"]);

        var section = content.Sections.ShouldHaveSingleItem();
        section.Heading.ShouldBe("Projekt");
        var entry = section.Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("Betalplattform");
        entry.Lines.ShouldBe(["Rad 1", "Rad 2"]);
    }

    [Fact]
    public void ToDomainThenToDto_RoundTripsSupersetFields_ProficiencyTokenPreserved()
    {
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [new SkillDto("C#", 8)],
            Summary: null,
            Languages: [new SpokenLanguageDto("Svenska", "Native")],
            SkillGroups: [new SkillGroupDto("Backend", ["C#"])],
            Sections: [new ResumeSectionDto("Projekt", [new SectionEntryDto("X", ["Rad 1"])])]);

        var roundTripped = ResumeContentMapper.ToDomain(dto).ToDto();

        var lang = roundTripped.Languages.ShouldHaveSingleItem();
        lang.Name.ShouldBe("Svenska");
        // "Native" → LanguageProficiency.Native → "Native" (SmartEnum Name token round-trips).
        lang.Proficiency.ShouldBe("Native");
        roundTripped.SkillGroups.ShouldHaveSingleItem().Members.ShouldBe(["C#"]);
        var entry = roundTripped.Sections.ShouldHaveSingleItem().Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("X");
        entry.Lines.ShouldBe(["Rad 1"]);
    }

    [Theory]
    [InlineData("Native", "Native")]
    [InlineData("native", "Native")]      // TryFromName(ignoreCase: true)
    [InlineData("NOTSTATED", "NotStated")]
    [InlineData("Basic", "Basic")]
    [InlineData("Good", "Good")]
    [InlineData("Fluent", "Fluent")]
    public void ToDomain_MapsKnownProficiencyToken_CaseInsensitive(string token, string expectedName)
    {
        var dto = MinimalWithLanguage(token);

        var content = ResumeContentMapper.ToDomain(dto);

        content.Languages.ShouldHaveSingleItem().Proficiency.Name.ShouldBe(expectedName);
    }

    [Theory]
    [InlineData("Sagolik")]  // unknown token
    [InlineData("")]          // empty token
    [InlineData("   ")]       // whitespace token
    public void ToDomain_MapsUnknownProficiencyToken_ToNotStated(string token)
    {
        // Tolerant, never synthesised (CLAUDE.md §5) — the level is the user's to state.
        var dto = MinimalWithLanguage(token);

        var content = ResumeContentMapper.ToDomain(dto);

        content.Languages.ShouldHaveSingleItem().Proficiency.ShouldBe(LanguageProficiency.NotStated);
    }

    [Fact]
    public void ToDomain_MapsNullProficiencyToken_ToNotStated()
    {
        var dto = MinimalWithLanguage(null!);

        var content = ResumeContentMapper.ToDomain(dto);

        content.Languages.ShouldHaveSingleItem().Proficiency.ShouldBe(LanguageProficiency.NotStated);
    }

    [Fact]
    public void ToDomain_WithNullSupersetCollections_ProducesEmptyDomainCollections_NoNre()
    {
        // A pre-superset client omits the three fields → they default to null on the DTO. The
        // mapper coalesces null to empty, so the domain VO carries empty collections, never null.
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: null);

        var content = ResumeContentMapper.ToDomain(dto);

        content.Languages.ShouldBeEmpty();
        content.SkillGroups.ShouldBeEmpty();
        content.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void ToDomain_WithNullNestedCollections_ProducesEmptyNestedCollections_NoNre()
    {
        // STJ passes null for an omitted JSON member on the NESTED lists too (Members/Entries/
        // Lines are nullable-with-default on the DTOs). A partial-but-parseable payload like
        // {"sections":[{"heading":"Projekt"}]} must map to empty nested collections, never
        // NRE→500 (dotnet-architect 2026-07-05).
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: null,
            SkillGroups: [new SkillGroupDto("Backend")],
            Sections:
            [
                new ResumeSectionDto("Projekt"),
                new ResumeSectionDto("Kurser", [new SectionEntryDto("HLR")]),
            ]);

        var content = ResumeContentMapper.ToDomain(dto);

        content.SkillGroups.ShouldHaveSingleItem().Members.ShouldBeEmpty();
        content.Sections.Count.ShouldBe(2);
        content.Sections[0].Entries.ShouldBeEmpty();
        var entry = content.Sections[1].Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("HLR");
        entry.Lines.ShouldBeEmpty();
    }

    private static ResumeContentDto MinimalWithLanguage(string proficiencyToken) =>
        new(new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: null,
            Languages: [new SpokenLanguageDto("Svenska", proficiencyToken)]);

    // ===============================================================
    // Fas 4b PR-7 (#656, CTO D-F) — ToDto losslessness. The apply handler routes
    // server-COMPOSED domain content through ToDto so the ONE personnummer-guard surface
    // (Check(ResumeContentDto)) covers it. A field ToDto drops is a field the guard never
    // scans — this round-trip pin makes that drift a test failure, not a silent leak.
    // ===============================================================

    [Fact]
    public void ToDto_ThenToDomain_ThenToDto_IsLossless_OverEveryFieldIncludingTheSuperset()
    {
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
            experiences:
            [
                new Experience("Beta AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30), "Byggde betaltjänster.\nAnsvarade för drift."),
                new Experience("Gamma AB", "Konsult", new DateOnly(2019, 3, 1), null, null),
            ],
            educations:
            [
                new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
            ],
            skills: [new Skill("C#", 8), new Skill("PostgreSQL", null)],
            summary: "Erfaren backend-utvecklare.",
            languages:
            [
                new SpokenLanguage("Svenska", LanguageProficiency.Native),
                new SpokenLanguage("Engelska", LanguageProficiency.Fluent),
            ],
            skillGroups: [new SkillGroup("Backend", ["C#", "PostgreSQL"])],
            sections:
            [
                new ResumeSection("Kurser", [new SectionEntry("HLR", ["Grundkurs", "Repetition"])]),
            ]);

        var dto = ResumeContentMapper.ToDto(content);
        var roundTripped = ResumeContentMapper.ToDto(ResumeContentMapper.ToDomain(dto));

        // DTOs are pure string/date records — JSON equality proves every field survived
        // both directions (a field missing from either mapping direction diverges here).
        System.Text.Json.JsonSerializer.Serialize(roundTripped)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(dto));

        // Spot-anchor the superset free-text fields the personnummer guard walks.
        dto.Summary.ShouldBe("Erfaren backend-utvecklare.");
        dto.Experiences[0].Description.ShouldBe("Byggde betaltjänster.\nAnsvarade för drift.");
        dto.Languages!.Select(l => l.Proficiency).ShouldBe(["Native", "Fluent"]);
        dto.SkillGroups!.ShouldHaveSingleItem().Members.ShouldBe(["C#", "PostgreSQL"]);
        dto.Sections!.ShouldHaveSingleItem().Entries!.ShouldHaveSingleItem()
            .Lines.ShouldBe(["Grundkurs", "Repetition"]);
    }

    [Fact]
    public void ToDto_MapsEveryFieldDirectly_AgainstAHandBuiltExpectedDto()
    {
        // Security review Minor 1: the round-trip pin above only catches ASYMMETRY between
        // ToDto and ToDomain — a field dropped by BOTH directions would round-trip cleanly.
        // This direct pin compares ToDto's output to a hand-built expected DTO, so a field
        // silently dropped by ToDto (= a field the personnummer guard never scans on the
        // apply path) fails HERE regardless of what ToDomain does.
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
            experiences:
            [
                new Experience("Beta AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30), "Byggde betaltjänster."),
            ],
            educations:
            [
                new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
            ],
            skills: [new Skill("C#", 8)],
            summary: "Erfaren backend-utvecklare.",
            languages: [new SpokenLanguage("Svenska", LanguageProficiency.Native)],
            skillGroups: [new SkillGroup("Backend", ["C#"])],
            sections: [new ResumeSection("Kurser", [new SectionEntry("HLR", ["Grundkurs"])])]);

        var expected = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
            Experiences:
            [
                new ExperienceDto("Beta AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30), "Byggde betaltjänster."),
            ],
            Educations:
            [
                new EducationDto("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
            ],
            Skills: [new SkillDto("C#", 8)],
            Summary: "Erfaren backend-utvecklare.",
            Languages: [new SpokenLanguageDto("Svenska", "Native")],
            SkillGroups: [new SkillGroupDto("Backend", ["C#"])],
            Sections: [new ResumeSectionDto("Kurser", [new SectionEntryDto("HLR", ["Grundkurs"])])]);

        var dto = ResumeContentMapper.ToDto(content);

        System.Text.Json.JsonSerializer.Serialize(dto)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(expected));
    }

    // Honest date absence (CV-pivot 2026-07-17, CTO-bind 5a-pre): null dates + the
    // verbatim RawPeriod must survive ToDomain→ToDto losslessly — a field added to one
    // side without the other fails HERE, not silently.
    [Fact]
    public void ToDomainThenToDto_RoundTripsNullDatesAndRawPeriod()
    {
        var dto = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", null, null, null),
            Experiences:
            [
                new ExperienceDto("Beta AB", "Utvecklare", null, null, null, "2019–2022"),
            ],
            Educations:
            [
                new EducationDto("KTH", "MSc", null, null, "2015–2019"),
            ],
            Skills: [],
            Summary: null);

        var roundTripped = ResumeContentMapper.ToDto(ResumeContentMapper.ToDomain(dto));

        var exp = roundTripped.Experiences.ShouldHaveSingleItem();
        exp.StartDate.ShouldBeNull();
        exp.EndDate.ShouldBeNull();
        exp.RawPeriod.ShouldBe("2019–2022");
        var edu = roundTripped.Educations.ShouldHaveSingleItem();
        edu.StartDate.ShouldBeNull();
        edu.RawPeriod.ShouldBe("2015–2019");
    }
}
