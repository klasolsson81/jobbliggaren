using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.AutoPromoteParsedResume;

// CV-pivot PR 5a — the bound verbatim projection (CTO-bind 2026-07-17 §1), pinned row by
// row so a single mapping regression fails a single named test. The mapper is a PURE
// projection: no filtering, no truncation, no synthesis (ADR 0071/CLAUDE.md §5) — the
// buildability gate downstream owns rejection.
public class AutoPromoteContentMapperTests
{
    private const string AccountName = "Anna Kontosson";

    private static ParsedResumeContent FullParse() => new(
        new ParsedContact("Fil Namnsson", "fil@example.com", "070-1234567", "Stockholm"),
        profile: "Erfaren backend-utvecklare.",
        experience:
        [
            new ParsedExperience("Backend-utvecklare", "Beta AB", "2019–2022",
                "Backend-utvecklare, Beta AB\n2019–2022\nByggde betaltjänster."),
        ],
        education: [new ParsedEducation("KTH", "Civilingenjör", "2013–2018", "raw edu")],
        skills: ["C#", "PostgreSQL"],
        languages: ["Svenska", "Engelska"],
        sections: [new ParsedSection("Projekt", [new ParsedSectionEntry("Kassasystem", ["Byggde kassasystem."])])]);

    // ── PersonalInfo ─────────────────────────────────────────────────────

    [Fact]
    public void ToContentDto_FullNameIsTheResolvedAccountName_NeverTheParsedContactName()
    {
        var dto = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName);

        dto.PersonalInfo.FullName.ShouldBe(AccountName);
        dto.PersonalInfo.FullName.ShouldNotBe("Fil Namnsson");
    }

    [Fact]
    public void ToContentDto_ContactFieldsCarryOneToOne()
    {
        var dto = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName);

        dto.PersonalInfo.Email.ShouldBe("fil@example.com");
        dto.PersonalInfo.Phone.ShouldBe("070-1234567");
        dto.PersonalInfo.Location.ShouldBe("Stockholm");
    }

    // ── Summary ──────────────────────────────────────────────────────────

    [Fact]
    public void ToContentDto_SummaryIsTheParsedProfile()
    {
        AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .Summary.ShouldBe("Erfaren backend-utvecklare.");

        AutoPromoteContentMapper.ToContentDto(
                new ParsedResumeContent(ParsedContact.Empty), AccountName)
            .Summary.ShouldBeNull(); // no profile → honest null, never invented
    }

    // ── Experience ───────────────────────────────────────────────────────

    [Fact]
    public void ToContentDto_ExperienceMapsOrganizationToCompany_TitleToRole()
    {
        var exp = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .Experiences.ShouldHaveSingleItem();

        exp.Company.ShouldBe("Beta AB");
        exp.Role.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public void ToContentDto_ExperienceDatesAreNull_PeriodRidesRawPeriodVerbatim()
    {
        var exp = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .Experiences.ShouldHaveSingleItem();

        exp.StartDate.ShouldBeNull();  // the parse holds no structured dates — honest absence
        exp.EndDate.ShouldBeNull();
        exp.RawPeriod.ShouldBe("2019–2022");
    }

    /// <summary>The Description fork, bound Option (a): RawText is the whole entry block —
    /// header lines included — and must never become the canonical description (render
    /// duplication + TextIsDescriptionOnly review corruption; CTO-bind §1).</summary>
    [Fact]
    public void ToContentDto_ExperienceDescriptionIsNull_RawTextNeverCarried()
    {
        var exp = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .Experiences.ShouldHaveSingleItem();

        exp.Description.ShouldBeNull();
    }

    /// <summary>Pure projection: no truncation (an over-cap period reaches the gate
    /// verbatim) and no filtering (a field-less entry projects empty, the gate rejects) —
    /// the mapper never makes the CV say less, or different, than the file did.</summary>
    [Fact]
    public void ToContentDto_NeverTruncatesPeriod_NeverDropsEntries()
    {
        var overlong = new string('x', 101);
        var parse = new ParsedResumeContent(
            ParsedContact.Empty,
            experience:
            [
                new ParsedExperience("Utvecklare", "Beta AB", overlong, "raw"),
                new ParsedExperience("Utvecklare", null, null, "raw two"),
                new ParsedExperience(null, null, null, "raw three"),
            ]);

        var dto = AutoPromoteContentMapper.ToContentDto(parse, AccountName);

        dto.Experiences.Count.ShouldBe(3);
        dto.Experiences[0].RawPeriod.ShouldBe(overlong); // verbatim, 101 chars intact
        dto.Experiences[1].Company.ShouldBe(string.Empty); // absent org → honest empty
        dto.Experiences[1].RawPeriod.ShouldBeNull();
        dto.Experiences[2].Role.ShouldBe(string.Empty);
    }

    // ── Education ────────────────────────────────────────────────────────

    /// <summary>The never-drops/never-truncates guarantee witnessed PER collection block
    /// (test-writer gate): the education block is its own `.Select()` — an accidental
    /// `.Where()`/`.Take()`/truncation edit there would slip past the experience pins.</summary>
    [Fact]
    public void ToContentDto_NeverDropsEducations_NorTruncatesPeriods_NorReorders()
    {
        var overlong = new string('y', 101);
        var parse = new ParsedResumeContent(
            ParsedContact.Empty,
            education:
            [
                new ParsedEducation("KTH", "Civilingenjör", overlong, "raw1"),
                new ParsedEducation("Chalmers", null, "2010–2013", "raw2"),
                new ParsedEducation(null, "Master", null, "raw3"),
            ]);

        var dto = AutoPromoteContentMapper.ToContentDto(parse, AccountName);

        dto.Educations.Count.ShouldBe(3);
        dto.Educations[0].Institution.ShouldBe("KTH");
        dto.Educations[0].RawPeriod.ShouldBe(overlong); // verbatim, 101 chars intact
        dto.Educations[1].Institution.ShouldBe("Chalmers");
        dto.Educations[1].Degree.ShouldBe(string.Empty); // null → honest empty
        dto.Educations[2].Institution.ShouldBe(string.Empty);
        dto.Educations[2].RawPeriod.ShouldBeNull();
    }

    [Fact]
    public void ToContentDto_EducationMapsInstitutionDegree_NullDates_RawPeriod()
    {
        var edu = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .Educations.ShouldHaveSingleItem();

        edu.Institution.ShouldBe("KTH");
        edu.Degree.ShouldBe("Civilingenjör");
        edu.StartDate.ShouldBeNull();
        edu.EndDate.ShouldBeNull();
        edu.RawPeriod.ShouldBe("2013–2018");
    }

    // ── Skills / Languages / SkillGroups ─────────────────────────────────

    [Fact]
    public void ToContentDto_SkillsCarryNamesWithNullYears()
    {
        var dto = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName);

        dto.Skills.Select(s => s.Name).ShouldBe(["C#", "PostgreSQL"]);
        dto.Skills.ShouldAllBe(s => s.YearsExperience == null); // the parse knows no years
    }

    /// <summary>An imported language's level is unknown, not "basic" — every language maps
    /// to NotStated; the user states a real level later (ADR 0074 OQ3 honesty).</summary>
    [Fact]
    public void ToContentDto_LanguagesMapToNotStated()
    {
        var dto = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName);

        dto.Languages.ShouldNotBeNull();
        dto.Languages.Select(l => l.Name).ShouldBe(["Svenska", "Engelska"]);
        dto.Languages.ShouldAllBe(l => l.Proficiency == LanguageProficiency.NotStated.Name);
    }

    [Fact]
    public void ToContentDto_SkillGroupsAreEmpty_TheParseHasNoGroupingConcept()
    {
        AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName)
            .SkillGroups.ShouldNotBeNull().ShouldBeEmpty();
    }

    // ── Sections ─────────────────────────────────────────────────────────

    [Fact]
    public void ToContentDto_FreeSectionsMapOneToOne_HeadingAndEntriesVerbatim()
    {
        var dto = AutoPromoteContentMapper.ToContentDto(FullParse(), AccountName);

        var section = dto.Sections.ShouldNotBeNull().ShouldHaveSingleItem();
        section.Heading.ShouldBe("Projekt");
        var entry = section.Entries.ShouldNotBeNull().ShouldHaveSingleItem();
        entry.Title.ShouldBe("Kassasystem");
        entry.Lines.ShouldBe(["Byggde kassasystem."]);
    }

    /// <summary>Multi-section, multi-entry, multi-line order preservation — the sections
    /// block is its own projection and needs its own never-drops witness (test-writer gate).</summary>
    [Fact]
    public void ToContentDto_NeverDropsSections_NorEntries_PreservingOrder()
    {
        var parse = new ParsedResumeContent(
            ParsedContact.Empty,
            sections:
            [
                new ParsedSection("Projekt",
                [
                    new ParsedSectionEntry("Kassasystem", ["Rad 1.", "Rad 2."]),
                    new ParsedSectionEntry("Bokningsmotor", ["Rad A."]),
                ]),
                new ParsedSection("Referenser",
                    [new ParsedSectionEntry(null, ["Lämnas på begäran."])]),
            ]);

        var dto = AutoPromoteContentMapper.ToContentDto(parse, AccountName);

        dto.Sections.ShouldNotBeNull();
        dto.Sections.Count.ShouldBe(2);
        dto.Sections.Select(s => s.Heading).ShouldBe(["Projekt", "Referenser"]);
        var first = dto.Sections[0].Entries.ShouldNotBeNull();
        first.Count.ShouldBe(2);
        first.Select(e => e.Title).ShouldBe(["Kassasystem", "Bokningsmotor"]);
        first[0].Lines.ShouldBe(["Rad 1.", "Rad 2."]); // multi-line order intact
    }

    [Fact]
    public void ToContentDto_TitleLessSectionEntryCarriesItsLines()
    {
        var parse = new ParsedResumeContent(
            ParsedContact.Empty,
            sections: [new ParsedSection("Referenser", [new ParsedSectionEntry(null, ["Lämnas på begäran."])])]);

        var entry = AutoPromoteContentMapper.ToContentDto(parse, AccountName)
            .Sections.ShouldNotBeNull().ShouldHaveSingleItem()
            .Entries.ShouldNotBeNull().ShouldHaveSingleItem();

        entry.Title.ShouldBeNull(); // the parser found no title and the mapper invents none
        entry.Lines.ShouldBe(["Lämnas på begäran."]);
    }
}
