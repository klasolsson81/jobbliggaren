using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

/// <summary>
/// The honest-date-absence contract (CV-pivot 2026-07-17, CTO-bind 5a-pre): the canonical
/// model admits date-less entries so a verbatim auto-promoted parse never fabricates a
/// date the user did not write. Structured dates stay authoritative when present;
/// <c>RawPeriod</c> is the verbatim display/citation fallback, capped, never scored.
/// </summary>
public class HonestDateAbsenceTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private const string ValidFullName = "Klas Olsson";

    private static Resume CreateValidResume() =>
        Resume.Create(ValidJobSeekerId, "Mitt CV", ValidFullName, Clock).Value;

    private static ResumeContent ContentWith(
        Experience[]? experiences = null, Education[]? educations = null) =>
        new(new PersonalInfo(ValidFullName, null, null, null),
            experiences: experiences,
            educations: educations);

    // ---------------------------------------------------------------
    // Null dates are VALID — absence is honest, never an error
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithDatelessExperienceCarryingRawPeriod_Succeeds()
    {
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, null, null, "2019–2022"),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithEndOnlyExperience_Succeeds()
    {
        // "examen 2020"-shape: a known end without a known start is legitimate.
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, new DateOnly(2020, 6, 1), null),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithDatelessEducation_Succeeds()
    {
        var resume = CreateValidResume();
        var content = ContentWith(educations:
        [
            new Education("KTH", "MSc CS", null, null, "2015–2020"),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // The end-before-start rule survives — when BOTH dates are present
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithBothDatesInverted_StillFails()
    {
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience(
                "Mastercard", "Backend Developer",
                new DateOnly(2024, 6, 1), new DateOnly(2024, 1, 1), null),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceDatesInvalid");
    }

    [Fact]
    public void UpdateMasterContent_WithBothEducationDatesInverted_StillFails()
    {
        var resume = CreateValidResume();
        var content = ContentWith(educations:
        [
            new Education("KTH", "MSc CS", new DateOnly(2024, 6, 1), new DateOnly(2024, 1, 1)),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDatesInvalid");
    }

    // ---------------------------------------------------------------
    // RawPeriod is capped (parity the label-field discipline)
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithOverlongExperienceRawPeriod_Fails()
    {
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, null, null, new string('x', 101)),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceRawPeriodTooLong");
    }

    [Fact]
    public void UpdateMasterContent_WithOverlongEducationRawPeriod_Fails()
    {
        var resume = CreateValidResume();
        var content = ContentWith(educations:
        [
            new Education("KTH", "MSc CS", null, null, new string('x', 101)),
        ]);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationRawPeriodTooLong");
    }

    // ---------------------------------------------------------------
    // Denormalized projection: null StartDate sorts LAST — a dated entry
    // wins LatestRole (CTO-bind 5a-pre: no code change, pinned here)
    // ---------------------------------------------------------------

    [Fact]
    public void ApplyDenormalizedProjection_DatedEntryWinsLatestRole_OverDatelessEntry()
    {
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience("Gammalt AB", "Dateless Role", null, null, null, "2019–2022"),
            new Experience("Mastercard", "Dated Role", new DateOnly(2001, 1, 1), null, null),
        ]);

        resume.UpdateMasterContent(content, Clock).IsSuccess.ShouldBeTrue();

        // Even an ANCIENT dated entry beats a dateless one — Comparer<DateOnly?> ranks
        // null below every value, so OrderByDescending puts nulls last.
        resume.LatestRole.ShouldBe("Dated Role");
    }

    [Fact]
    public void ApplyDenormalizedProjection_AllDateless_StillProjectsARole()
    {
        var resume = CreateValidResume();
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Only Role", null, null, null, "2019–2022"),
        ]);

        resume.UpdateMasterContent(content, Clock).IsSuccess.ShouldBeTrue();

        resume.LatestRole.ShouldBe("Only Role");
    }

    // ---------------------------------------------------------------
    // Linearizer: RawPeriod is the citable fallback; no period line at all
    // when neither exists — never an empty line (#815 parity)
    // ---------------------------------------------------------------

    [Fact]
    public void Linearize_DatelessExperienceWithRawPeriod_EmitsTheVerbatimPeriod()
    {
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, null, null, "2019–2022"),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        linearized.Text.ShouldContain("2019–2022");
    }

    [Fact]
    public void Linearize_DatelessExperienceWithoutRawPeriod_EmitsNoEmptyPeriodLine()
    {
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, null, "Byggde API:er.", null),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        // The entry's block must be Role\nCompany\nDescription — no blank period line
        // injecting a phantom block boundary into the citation substrate.
        linearized.Text.ShouldContain("Backend Developer\nMastercard\nByggde API:er.");
    }

    [Fact]
    public void Linearize_DatelessEducationWithRawPeriod_EmitsTheVerbatimPeriod()
    {
        var content = ContentWith(educations:
        [
            new Education("KTH", "MSc CS", null, null, "2015–2020"),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        linearized.Text.ShouldContain("2015–2020");
    }

    [Fact]
    public void Linearize_DatelessEducationWithoutRawPeriod_EmitsNoEmptyPeriodLine()
    {
        var content = ContentWith(educations:
        [
            new Education("KTH", "MSc CS", null, null),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        linearized.Text.ShouldContain("MSc CS\nKTH");
    }

    // ---------------------------------------------------------------
    // End-only (null start + set end): VALIDATES, but v1 display gates on
    // StartDate-presence — the lone EndDate is stored yet not rendered.
    // CHARACTERIZATION pins (deliberate v1 drop, degrades honestly; whether
    // display should honor a lone EndDate is CTO-triage for the auto-promote
    // PR, which itself never emits end-only entries — see Resume.cs comment).
    // ---------------------------------------------------------------

    [Fact]
    public void Linearize_EndOnlyExperienceWithoutRawPeriod_EmitsNoPeriodLine()
    {
        var content = ContentWith(experiences:
        [
            new Experience("Mastercard", "Backend Developer", null, new DateOnly(2020, 6, 1), "Byggde API:er."),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        linearized.Text.ShouldContain("Backend Developer\nMastercard\nByggde API:er.");
        linearized.Text.ShouldNotContain("2020");
    }

    [Fact]
    public void Linearize_DatedExperience_StillEmitsTheStructuredPeriod()
    {
        var content = ContentWith(experiences:
        [
            new Experience(
                "Mastercard", "Backend Developer",
                new DateOnly(2021, 3, 1), new DateOnly(2024, 6, 1), null, "ignoreras"),
        ]);

        var linearized = ResumeContentLinearizer.Linearize(content);

        // Structured dates are authoritative — RawPeriod must NOT override them.
        linearized.Text.ShouldContain("2021-03 – 2024-06");
        linearized.Text.ShouldNotContain("ignoreras");
    }
}
