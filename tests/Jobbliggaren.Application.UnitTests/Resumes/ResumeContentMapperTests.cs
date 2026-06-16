using Jobbliggaren.Application.Resumes;
using Jobbliggaren.Application.Resumes.Queries;
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
}
