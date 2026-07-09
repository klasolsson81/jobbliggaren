using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes.Parsing;

// Fas 4b CV-motor v2 PR-8.1 (issue #657, ADR 0093; CTO-bind Q5 — the "complete your CV" gap card).
// ParsedGapSummary is a non-PII, all-boolean derivation of "which sections did the deterministic
// parse actually find" — nine flags, honest about absence (whitespace-only or empty is NOT present;
// CLAUDE.md §5 never synthesises). It rides onto ParsedResume via an additive trailing gaps param on
// Create (parity CvLayoutMetrics in PR-6b — the ~30 existing callers stay unchanged).
//
// SPEC-DRIVEN. RED until ParsedGapSummary + ParsedGapSummary.FromContent + ParsedResume.Gaps + the
// trailing Create(gaps:) param ship.
public class ParsedGapSummaryTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId Owner = JobSeekerId.New();

    private static ParsedResumeContent FullContent() => new(
        new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
        profile: "Erfaren backend-utvecklare.",
        experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "raw")],
        education: [new ParsedEducation("KTH", "Civilingenjör", "2016–2021", "raw")],
        skills: ["C#", "PostgreSQL"],
        languages: ["Svenska", "Engelska"]);

    // ===============================================================
    // FromContent — flag derivation
    // ===============================================================

    [Fact]
    public void FromContent_WhenEveryFieldIsPresent_AllFlagsAreTrue()
    {
        var summary = ParsedGapSummary.FromContent(FullContent());

        summary.HasFullName.ShouldBeTrue();
        summary.HasEmail.ShouldBeTrue();
        summary.HasPhone.ShouldBeTrue();
        summary.HasLocation.ShouldBeTrue();
        summary.HasProfile.ShouldBeTrue();
        summary.HasExperience.ShouldBeTrue();
        summary.HasEducation.ShouldBeTrue();
        summary.HasSkills.ShouldBeTrue();
        summary.HasLanguages.ShouldBeTrue();
    }

    [Fact]
    public void FromContent_WhenPhoneIsWhitespaceOnly_HasPhoneIsFalse_OtherContactFlagsTrue()
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "   ", "Stockholm"),
            profile: "Profil.");

        var summary = ParsedGapSummary.FromContent(content);

        // Whitespace is absence, not presence (honest gap reporting).
        summary.HasPhone.ShouldBeFalse();
        summary.HasFullName.ShouldBeTrue();
        summary.HasEmail.ShouldBeTrue();
        summary.HasLocation.ShouldBeTrue();
    }

    [Fact]
    public void FromContent_WhenCollectionsAreEmpty_ExperienceEducationSkillsLanguagesAreFalse()
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Profil.",
            experience: [],
            education: [],
            skills: [],
            languages: []);

        var summary = ParsedGapSummary.FromContent(content);

        summary.HasExperience.ShouldBeFalse();
        summary.HasEducation.ShouldBeFalse();
        summary.HasSkills.ShouldBeFalse();
        summary.HasLanguages.ShouldBeFalse();
    }

    [Fact]
    public void FromContent_WhenProfileIsNull_HasProfileIsFalse()
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: null);

        ParsedGapSummary.FromContent(content).HasProfile.ShouldBeFalse();
    }

    [Fact]
    public void FromContent_WhenContactIsEmpty_AllContactFlagsAreFalse()
    {
        var content = new ParsedResumeContent(ParsedContact.Empty, profile: "Profil.");

        var summary = ParsedGapSummary.FromContent(content);

        summary.HasFullName.ShouldBeFalse();
        summary.HasEmail.ShouldBeFalse();
        summary.HasPhone.ShouldBeFalse();
        summary.HasLocation.ShouldBeFalse();
    }

    // ===============================================================
    // ParsedResume.Create — the additive gaps carrier (parity CvLayoutMetrics)
    // ===============================================================

    [Fact]
    public void Create_WhenGapsProvided_PersistsToGapsProperty()
    {
        var summary = new ParsedGapSummary(true, true, false, true, false, true, false, true, false);

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ParsedResumeContent.Empty, "raw", ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            PersonnummerScanOutcome.None, [], Clock, gaps: summary);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Gaps.ShouldBe(summary);
    }

    [Fact]
    public void Create_WhenGapsParamOmitted_LeavesGapsNull()
    {
        // The trailing gaps param defaults to null (additive — existing callers stay unchanged).
        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ParsedResumeContent.Empty, "raw", ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Gaps.ShouldBeNull();
    }
}
