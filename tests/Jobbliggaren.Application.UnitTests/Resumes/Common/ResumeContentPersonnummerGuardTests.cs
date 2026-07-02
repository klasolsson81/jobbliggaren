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

    private static ResumeContentDto Clean() =>
        new(CleanPersonalInfo(),
            Experiences: [CleanExperience()],
            Educations: [CleanEducation()],
            Skills: [new SkillDto("C#", 8)],
            Summary: "Erfaren backend-utvecklare.");

    // One entry per free-text field class CollectFreeText concatenates — 11 in total. If a field
    // is ever dropped from CollectFreeText its entry here fails (the personnummer is no longer
    // seen), so field-completeness is pinned rather than merely reviewed.
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
