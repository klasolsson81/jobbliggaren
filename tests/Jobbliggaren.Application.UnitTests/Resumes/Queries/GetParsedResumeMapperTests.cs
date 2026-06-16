using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// Fas 4 STEG B / B1b — the ParsedResume → ParsedResumeDetailDto projection. Pure (no DbContext):
/// asserts the loosely-parsed content is mapped VERBATIM — raw Period strings preserved, nothing
/// synthesised (CLAUDE.md §5, DQ3-3a) — and the PII-safe summary fields project correctly. (The
/// handler path can't exercise this with InMemory because the encrypted Content shadow is null
/// after AsNoTracking re-materialization — see GetParsedResumeQueryHandlerTests.)
/// </summary>
public class GetParsedResumeMapperTests
{
    private static ParsedResume BuildRichArtifact()
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience:
            [
                new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "Acme AB, 2021–2024"),
                new ParsedExperience("Konsult", "Beta AB", null, "Beta AB"),
            ],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2015–2020", "KTH 2015–2020")],
            skills: ["C#", "PostgreSQL"],
            languages: ["Svenska", "Engelska"]);

        var confidence = ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["e-post hittad"]),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Degraded, []),
        ]);

        var owner = JobSeeker.Register(Guid.NewGuid(), "Owner", FakeDateTimeProvider.Default).Value.Id;
        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", confidence,
            PersonnummerScanOutcome.None,
            [new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare")],
            FakeDateTimeProvider.Default).Value;
    }

    [Fact]
    public void ToDetailDto_MapsTopLevelAndSummary_Faithfully()
    {
        var artifact = BuildRichArtifact();

        var dto = artifact.ToDetailDto();

        dto.Id.ShouldBe(artifact.Id.Value);
        dto.Status.ShouldBe("PendingReview");
        dto.DetectedLanguage.ShouldBe(ResumeLanguage.Sv.Name);
        dto.SourceFileName.ShouldBe("CV_Anna.pdf");
        dto.Personnummer.Found.ShouldBeFalse();
        dto.Personnummer.Count.ShouldBe(0);
        dto.Confidence.Sections.Count.ShouldBe(2);
        dto.Confidence.Sections[0].Evidence.ShouldContain("e-post hittad");
        dto.OccupationProposals.Count.ShouldBe(1);
        dto.OccupationProposals[0].ConceptId.ShouldBe("q8wL_kdi_WaW");
        dto.OccupationProposals[0].Label.ShouldBe("Systemutvecklare");
        dto.OccupationProposals[0].MatchedOn.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public void ToDetailDto_MapsContentVerbatim_PreservingRawPeriodStrings_AndNulls()
    {
        var artifact = BuildRichArtifact();

        var content = artifact.ToDetailDto().Content;

        content.Contact.FullName.ShouldBe("Anna Andersson");
        content.Contact.Email.ShouldBe("anna@example.com");
        content.Contact.Location.ShouldBe("Stockholm");
        content.Profile.ShouldBe("Erfaren backend-utvecklare.");

        content.Experiences.Count.ShouldBe(2);
        content.Experiences[0].Title.ShouldBe("Backend-utvecklare");
        content.Experiences[0].Organization.ShouldBe("Acme AB");
        content.Experiences[0].Period.ShouldBe("2021–2024"); // raw string, never a guessed date
        content.Experiences[0].RawText.ShouldBe("Acme AB, 2021–2024");
        content.Experiences[1].Period.ShouldBeNull(); // honest about what was not found

        content.Educations.Count.ShouldBe(1);
        content.Educations[0].Institution.ShouldBe("KTH");
        content.Educations[0].Degree.ShouldBe("Civilingenjör");
        content.Educations[0].Period.ShouldBe("2015–2020");

        content.Skills.ShouldBe(["C#", "PostgreSQL"]);
        content.Languages.ShouldBe(["Svenska", "Engelska"]);
    }

    [Fact]
    public void ToDetailDto_DegradedContent_MapsHonestly_EmptyListsNotNull_RawTextSurvives()
    {
        // A degraded parse is first-class (OQ5): the projection stays honest about what was NOT
        // found (null structured fields, empty lists — never synthesised) while the verbatim
        // RawText still surfaces so the gap-fill form can show the user the source line.
        var content = new ParsedResumeContent(
            new ParsedContact(null, null, null, null),
            profile: null,
            experience: [new ParsedExperience(null, null, null, "Obekant rad ur CV:t")]);
        var confidence = ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed);
        var owner = JobSeeker.Register(Guid.NewGuid(), "Owner", FakeDateTimeProvider.Default).Value.Id;
        var artifact = ParsedResume.Create(
            owner, "scan.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Obekant rad ur CV:t", confidence, PersonnummerScanOutcome.None, [],
            FakeDateTimeProvider.Default).Value;

        var dto = artifact.ToDetailDto();

        dto.Content.Profile.ShouldBeNull();
        dto.Content.Contact.FullName.ShouldBeNull();
        dto.Content.Skills.ShouldBeEmpty();
        dto.Content.Languages.ShouldBeEmpty();
        dto.Content.Educations.ShouldBeEmpty();
        dto.Content.Experiences.Count.ShouldBe(1);
        dto.Content.Experiences[0].Title.ShouldBeNull();
        dto.Content.Experiences[0].Period.ShouldBeNull();
        dto.Content.Experiences[0].RawText.ShouldBe("Obekant rad ur CV:t");
        dto.OccupationProposals.ShouldBeEmpty();
        dto.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed.ToString());
        dto.Confidence.RequiresManualReview.ShouldBeTrue();
    }
}
