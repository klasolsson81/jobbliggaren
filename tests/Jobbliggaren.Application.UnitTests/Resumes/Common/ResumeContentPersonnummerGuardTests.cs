using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Queries;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Common;

/// <summary>
/// #499 (ADR 0074 Invariant 1 — the highest-priority PII invariant). The shared
/// <see cref="ResumeContentPersonnummerGuard"/> is exercised DIRECTLY here (visible via
/// InternalsVisibleTo) so FIELD COMPLETENESS is pinned centrally, not duplicated across the
/// promote/update handler tests: a personnummer in ANY user free-text field of a
/// <see cref="ResumeContentDto"/> must block, so removing a single <c>CollectFreeText</c>
/// line fails a test (a silent per-field leak, not a green build). The guard runs BEFORE the
/// aggregate's <c>ValidateContent</c>, so a personnummer in a field that structure-validation
/// would also reject (e.g. FullName) is still testable here at guard level.
///
/// <para>All vectors are SYNTHETIC (811218-9876 personnummer). Each carries a non-digit
/// boundary around the token ("Fält {Pnr}") so the scanner's <c>(?&lt;!\d)...(?!\d)</c>
/// token-boundary guard never rejects it (which would make a vector falsely green).</para>
/// </summary>
public class ResumeContentPersonnummerGuardTests
{
    private const string Pnr = "811218-9876";
    private const string ExpectedCode = "Resume.PersonnummerMustBeRemoved";

    private static PersonalInfoDto CleanPersonalInfo() =>
        new("Klas Olsson", "klas@example.se", "0701234567", "Stockholm");

    private static ExperienceDto CleanExperience() =>
        new("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster.");

    private static EducationDto CleanEducation() =>
        new("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1));

    // Fas 4b AppCopy superset (ADR 0095 D-E) clean free-text fixtures. The proficiency token
    // ("Native") is a closed vocabulary, not scanned free text, so it never carries a personnummer.
    private static SpokenLanguageDto CleanLanguage() =>
        new("Svenska", "Native");

    private static SkillGroupDto CleanSkillGroup() =>
        new("Backend", ["C#"]);

    private static SectionEntryDto CleanSectionEntry() =>
        new("Projekt X", ["Byggde en betaltjänst."]);

    private static ResumeSectionDto CleanSection() =>
        new("Projekt", [CleanSectionEntry()]);

    private static ResumeContentDto Clean() =>
        new(CleanPersonalInfo(),
            Experiences: [CleanExperience()],
            Educations: [CleanEducation()],
            Skills: [new SkillDto("C#", 8)],
            Summary: "Erfaren backend-utvecklare.");

    // One entry per free-text field class CollectFreeText concatenates — 17 in total (11 original
    // + the 6 Fas 4b AppCopy superset free-text fields, ADR 0095 D-E; SkillGroup.Members scanned
    // directly per security-auditor 2026-07-05 so the guard is self-contained). If a field is ever
    // dropped from CollectFreeText its entry here fails (the personnummer is no longer seen), so
    // field-completeness is pinned rather than merely reviewed.
    public static IEnumerable<object[]> PersonnummerInEachFreeTextField()
    {
        yield return ["PersonalInfo.FullName",
            Clean() with { PersonalInfo = CleanPersonalInfo() with { FullName = $"Klas {Pnr}" } }];
        yield return ["PersonalInfo.Email",
            Clean() with { PersonalInfo = CleanPersonalInfo() with { Email = $"kontakt {Pnr}" } }];
        yield return ["PersonalInfo.Phone",
            Clean() with { PersonalInfo = CleanPersonalInfo() with { Phone = $"tel {Pnr}" } }];
        yield return ["PersonalInfo.Location",
            Clean() with { PersonalInfo = CleanPersonalInfo() with { Location = $"Ort {Pnr}" } }];
        yield return ["Summary",
            Clean() with { Summary = $"Erfaren utvecklare. Pnr {Pnr}." }];
        yield return ["Experience.Company",
            Clean() with { Experiences = [CleanExperience() with { Company = $"Bolag {Pnr}" }] }];
        yield return ["Experience.Role",
            Clean() with { Experiences = [CleanExperience() with { Role = $"Roll {Pnr}" }] }];
        yield return ["Experience.Description",
            Clean() with { Experiences = [CleanExperience() with { Description = $"Anställd, nr {Pnr}." }] }];
        yield return ["Education.Institution",
            Clean() with { Educations = [CleanEducation() with { Institution = $"Skola {Pnr}" }] }];
        yield return ["Education.Degree",
            Clean() with { Educations = [CleanEducation() with { Degree = $"Examen {Pnr}" }] }];
        yield return ["Skill.Name",
            Clean() with { Skills = [new SkillDto($"Kompetens {Pnr}", 3)] }];

        // Fas 4b AppCopy superset free text (ADR 0095 D-E). A personnummer typed into any of
        // these five must be flagged — dropping the corresponding CollectFreeText line fails here.
        yield return ["SpokenLanguage.Name",
            Clean() with { Languages = [CleanLanguage() with { Name = $"Svenska {Pnr}" }] }];
        yield return ["SkillGroup.Name",
            Clean() with { SkillGroups = [CleanSkillGroup() with { Name = $"Grupp {Pnr}" }] }];
        yield return ["SkillGroup.Members",
            Clean() with { SkillGroups = [CleanSkillGroup() with { Members = [$"Kompetens {Pnr}"] }] }];
        yield return ["Section.Heading",
            Clean() with { Sections = [CleanSection() with { Heading = $"Rubrik {Pnr}" }] }];
        yield return ["SectionEntry.Title",
            Clean() with { Sections = [new ResumeSectionDto("Projekt", [CleanSectionEntry() with { Title = $"Titel {Pnr}" }])] }];
        yield return ["SectionEntry.Lines",
            Clean() with { Sections = [new ResumeSectionDto("Projekt", [CleanSectionEntry() with { Lines = [$"Rad {Pnr}"] }])] }];
    }

    [Theory]
    [MemberData(nameof(PersonnummerInEachFreeTextField))]
    public void Check_WhenPersonnummerInAnyFreeTextField_ReturnsMustBeRemoved(
        string field, ResumeContentDto content)
    {
        var result = ResumeContentPersonnummerGuard.Check(content);

        result.IsFailure.ShouldBeTrue($"a personnummer in {field} must be blocked");
        result.Error.Code.ShouldBe(ExpectedCode);
    }

    [Fact]
    public void Check_WhenCleanFullyPopulatedContent_Succeeds()
    {
        // A realistic, fully-populated clean CV must NOT be over-blocked — in particular a
        // 10-digit phone number ("0701234567" is a \d{6}\d{4} candidate SHAPE, gated only by
        // the date+Luhn check) must pass. Exercises the guard's success branch on a rich payload.
        var result = ResumeContentPersonnummerGuard.Check(Clean());

        result.IsSuccess.ShouldBeTrue();
    }

    // The 12-digit full-century form (YYYYMMDD[sep]XXXX) in each Fas 4b superset field. Same
    // significant digits as Pnr (century-prefixed), so it passes the SAME date+Luhn gate
    // (Personnummer.TryParse) — a random 12-digit run would be a false vector.
    private const string PnrTwelveDigit = "19811218-9876";

    public static IEnumerable<object[]> TwelveDigitPersonnummerInEachSupersetField()
    {
        yield return ["SpokenLanguage.Name",
            Clean() with { Languages = [CleanLanguage() with { Name = $"Svenska {PnrTwelveDigit}" }] }];
        yield return ["SkillGroup.Name",
            Clean() with { SkillGroups = [CleanSkillGroup() with { Name = $"Grupp {PnrTwelveDigit}" }] }];
        yield return ["Section.Heading",
            Clean() with { Sections = [CleanSection() with { Heading = $"Rubrik {PnrTwelveDigit}" }] }];
        yield return ["SectionEntry.Title",
            Clean() with { Sections = [new ResumeSectionDto("Projekt", [CleanSectionEntry() with { Title = $"Titel {PnrTwelveDigit}" }])] }];
        yield return ["SectionEntry.Lines",
            Clean() with { Sections = [new ResumeSectionDto("Projekt", [CleanSectionEntry() with { Lines = [$"Rad {PnrTwelveDigit}"] }])] }];
    }

    [Theory]
    [MemberData(nameof(TwelveDigitPersonnummerInEachSupersetField))]
    public void Check_WhenTwelveDigitPersonnummerInAnySupersetField_ReturnsMustBeRemoved(
        string field, ResumeContentDto content)
    {
        var result = ResumeContentPersonnummerGuard.Check(content);

        result.IsFailure.ShouldBeTrue($"a 12-digit personnummer in {field} must be blocked");
        result.Error.Code.ShouldBe(ExpectedCode);
    }

    [Fact]
    public void Check_WhenCleanFullyPopulatedSupersetContent_Succeeds()
    {
        // The success branch over a rich payload that also fills every superset collection with
        // clean text — the widened CollectFreeText must not over-block legitimate content.
        var content = Clean() with
        {
            Languages = [CleanLanguage()],
            SkillGroups = [CleanSkillGroup()],
            Sections = [CleanSection()],
        };

        var result = ResumeContentPersonnummerGuard.Check(content);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Check_WhenNestedSupersetCollectionsAreNull_Succeeds_NoNre()
    {
        // STJ passes null for an omitted JSON member (NRT is not runtime-enforced), so a
        // partial-but-parseable payload like {"sections":[{"heading":"Projekt"}]} reaches the
        // guard with null NESTED lists. The guard must scan it (null-guarded), never NRE→500
        // (dotnet-architect 2026-07-05).
        var content = Clean() with
        {
            SkillGroups = [new SkillGroupDto("Backend")],
            Sections = [new ResumeSectionDto("Projekt"), new ResumeSectionDto("Kurser", [new SectionEntryDto("HLR")])],
        };

        var result = ResumeContentPersonnummerGuard.Check(content);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Check_WhenPersonnummerHalvesSplitAcrossTwoFields_Succeeds_NotBridgedOverFieldBoundary()
    {
        // CollectFreeText joins fields with AppendLine (a newline between fields), and the
        // normalizer deliberately never bridges a newline. So "811218" in one field + "9876" in
        // the next are NOT a coherent personnummer and must NOT block. This pins the load-bearing
        // AppendLine choice: switching to Append (no separator) would concatenate the two halves
        // into a false-positive "8112189876". FullName and Email are appended consecutively.
        var content = Clean() with
        {
            PersonalInfo = CleanPersonalInfo() with { FullName = "Kandidat 811218", Email = "9876@example.se" },
        };

        var result = ResumeContentPersonnummerGuard.Check(content);

        result.IsSuccess.ShouldBeTrue();
    }
}
