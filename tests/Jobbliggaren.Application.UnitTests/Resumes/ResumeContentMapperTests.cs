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
}
