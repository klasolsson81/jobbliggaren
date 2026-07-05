using System.Reflection;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Events;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

public class ResumeTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private const string ValidName = "Mitt CV";
    private const string ValidFullName = "Klas Olsson";

    // Giltigt Tailored-innehåll som passerar ValidateContent (Fas 4 STEG A).
    private static readonly ResumeContent ValidTailoredContent = new(
        new PersonalInfo("Klas Olsson", "klas@example.com", null, "Stockholm"),
        experiences: new[]
        {
            new Experience("Mastercard", "Backend Developer", new DateOnly(2022, 1, 1), null, null),
        },
        skills: new[] { new Skill("C#", 8) },
        summary: "Skräddarsytt CV för en specifik annons.");

    // ---------------------------------------------------------------
    // Create — happy path
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_ReturnsSuccess()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobSeekerId.ShouldBe(ValidJobSeekerId);
        result.Value.Name.ShouldBe(ValidName);
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.UpdatedAt.ShouldBe(Clock.UtcNow);
        result.Value.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void Create_TrimsName()
    {
        var result = Resume.Create(ValidJobSeekerId, "  Mitt CV  ", ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Mitt CV");
    }

    [Fact]
    public void Create_AddsInitialMasterVersion()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Versions.Count.ShouldBe(1);
        var master = result.Value.Versions[0];
        master.Kind.ShouldBe(ResumeVersionKind.Master);
        master.DeletedAt.ShouldBeNull();
        master.CreatedAt.ShouldBe(Clock.UtcNow);
        master.UpdatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Create_InitialMasterVersionContentIsEmptyWithFullName()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        var master = result.Value.MasterVersion;
        master.Content.PersonalInfo.FullName.ShouldBe(ValidFullName);
        master.Content.PersonalInfo.Email.ShouldBeNull();
        master.Content.PersonalInfo.Phone.ShouldBeNull();
        master.Content.PersonalInfo.Location.ShouldBeNull();
        master.Content.Summary.ShouldBeNull();
        master.Content.Experiences.ShouldBeEmpty();
        master.Content.Educations.ShouldBeEmpty();
        master.Content.Skills.ShouldBeEmpty();
    }

    [Fact]
    public void Create_TrimsFullNameInInitialContent()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, "  Klas Olsson  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MasterVersion.Content.PersonalInfo.FullName.ShouldBe("Klas Olsson");
    }

    [Fact]
    public void Create_RaisesResumeCreatedAndResumeVersionCreatedEvents()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        var events = result.Value.DomainEvents;
        events.Count.ShouldBe(2);

        var created = events.OfType<ResumeCreatedDomainEvent>().ShouldHaveSingleItem();
        created.ResumeId.ShouldBe(result.Value.Id);
        created.JobSeekerId.ShouldBe(ValidJobSeekerId);
        created.Name.ShouldBe(ValidName);
        created.OccurredAt.ShouldBe(Clock.UtcNow);

        var versionCreated = events.OfType<ResumeVersionCreatedDomainEvent>().ShouldHaveSingleItem();
        versionCreated.ResumeId.ShouldBe(result.Value.Id);
        versionCreated.VersionId.ShouldBe(result.Value.MasterVersion.Id);
        versionCreated.Kind.ShouldBe(ResumeVersionKind.Master);
        versionCreated.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // Create — validering
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = Resume.Create(default, ValidName, ValidFullName, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.JobSeekerIdRequired");
    }

    [Fact]
    public void Create_WithEmptyName_ReturnsFailure()
    {
        var result = Resume.Create(ValidJobSeekerId, string.Empty, ValidFullName, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public void Create_WithWhitespaceName_ReturnsFailure()
    {
        var result = Resume.Create(ValidJobSeekerId, "   ", ValidFullName, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public void Create_WithNameTooLong_ReturnsFailure()
    {
        var tooLong = new string('A', 201);

        var result = Resume.Create(ValidJobSeekerId, tooLong, ValidFullName, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameTooLong");
    }

    [Fact]
    public void Create_WithEmptyFullName_ReturnsFailure()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, string.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
    }

    [Fact]
    public void Create_WithFullNameTooLong_ReturnsFailure()
    {
        var tooLong = new string('A', 201);

        var result = Resume.Create(ValidJobSeekerId, ValidName, tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameTooLong");
    }

    // ---------------------------------------------------------------
    // CreateFromParsed — Fas 4 STEG A PR-2 (promote a ParsedResume into a
    // canonical Resume). Unlike Create (which builds from ResumeContent.Empty),
    // CreateFromParsed builds ONE Master version holding the FULL gap-filled
    // content, applies the denormalized projection FROM that content, validates
    // content via the SAME ValidateContent codes as UpdateMasterContent, and
    // raises the provenance event linking to the source ParsedResumeId.
    // RED until CreateFromParsed + ResumeCreatedFromParsedResumeDomainEvent ship.
    // ---------------------------------------------------------------

    private static readonly ParsedResumeId ValidSourceParsedId = ParsedResumeId.New();

    // Rich, valid content that passes ValidateContent — exercises the denormalized
    // projection (LatestRole/SectionCount/TopSkills must reflect THIS content).
    private static ResumeContent GapFilledContent() => new(
        new PersonalInfo("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
        experiences: new[]
        {
            new Experience("Acme AB", "Junior", new DateOnly(2018, 1, 1), new DateOnly(2020, 12, 31), null),
            new Experience("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        },
        educations: new[]
        {
            new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
        },
        skills: new[]
        {
            new Skill("C#", 8),
            new Skill("PostgreSQL", 5),
        },
        summary: "Erfaren backend-utvecklare.");

    [Fact]
    public void CreateFromParsed_WithValidContent_ReturnsSuccess_OneMasterHoldingFullContent()
    {
        var content = GapFilledContent();

        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, content, ValidSourceParsedId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var resume = result.Value;
        resume.JobSeekerId.ShouldBe(ValidJobSeekerId);
        resume.Name.ShouldBe(ValidName);
        resume.CreatedAt.ShouldBe(Clock.UtcNow);
        resume.UpdatedAt.ShouldBe(Clock.UtcNow);
        resume.DeletedAt.ShouldBeNull();

        // Exactly one Master version — holding the FULL content, NOT Empty.
        resume.Versions.Count.ShouldBe(1);
        var master = resume.MasterVersion;
        master.Kind.ShouldBe(ResumeVersionKind.Master);
        master.Content.ShouldBe(content);
        master.Content.PersonalInfo.FullName.ShouldBe("Anna Andersson");
        master.Content.Experiences.Count.ShouldBe(2);
    }

    [Fact]
    public void CreateFromParsed_TrimsName()
    {
        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, "  Mitt CV  ", GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Mitt CV");
    }

    [Fact]
    public void CreateFromParsed_AppliesDenormalizedProjectionFromContent()
    {
        // The projection must reflect the gap-filled content (NOT remain at the
        // empty-Master defaults that Create leaves).
        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var resume = result.Value;
        // Most-recent experience by StartDate (Beta AB, 2021) drives LatestRole.
        resume.LatestRole.ShouldBe("Backend-utvecklare");
        // summary + experiences + educations + skills = 4 sections.
        resume.SectionCount.ShouldBe(4);
        resume.TopSkills.Count.ShouldBe(2);
        resume.TopSkills[0].ShouldBe("C#");
        resume.TopSkills[1].ShouldBe("PostgreSQL");
    }

    [Fact]
    public void CreateFromParsed_RaisesCreatedVersionCreatedAndProvenanceEvents()
    {
        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var resume = result.Value;
        var events = resume.DomainEvents;
        events.Count.ShouldBe(3);

        var created = events.OfType<ResumeCreatedDomainEvent>().ShouldHaveSingleItem();
        created.ResumeId.ShouldBe(resume.Id);
        created.JobSeekerId.ShouldBe(ValidJobSeekerId);
        created.Name.ShouldBe(ValidName);
        created.OccurredAt.ShouldBe(Clock.UtcNow);

        var versionCreated = events.OfType<ResumeVersionCreatedDomainEvent>().ShouldHaveSingleItem();
        versionCreated.ResumeId.ShouldBe(resume.Id);
        versionCreated.VersionId.ShouldBe(resume.MasterVersion.Id);
        versionCreated.Kind.ShouldBe(ResumeVersionKind.Master);

        // Provenance event linking the new Resume to its source ParsedResume.
        var provenance = events.OfType<ResumeCreatedFromParsedResumeDomainEvent>().ShouldHaveSingleItem();
        provenance.ResumeId.ShouldBe(resume.Id);
        provenance.SourceParsedResumeId.ShouldBe(ValidSourceParsedId);
        provenance.JobSeekerId.ShouldBe(ValidJobSeekerId);
        provenance.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void CreateFromParsed_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = Resume.CreateFromParsed(
            default, ValidName, GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.JobSeekerIdRequired");
    }

    [Fact]
    public void CreateFromParsed_WithEmptyName_ReturnsFailure()
    {
        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, string.Empty, GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public void CreateFromParsed_WithNameTooLong_ReturnsFailure()
    {
        var tooLong = new string('A', 201);

        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, tooLong, GapFilledContent(), ValidSourceParsedId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameTooLong");
    }

    [Fact]
    public void CreateFromParsed_WithEmptyFullName_ReturnsFailure_NoSideEffects()
    {
        // Degraded content with an empty FullName fails with the SAME code as
        // UpdateMasterContent (ValidateContent parity) — Result.Failure, no Resume.
        var content = new ResumeContent(new PersonalInfo(string.Empty, null, null, null));

        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, content, ValidSourceParsedId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
    }

    [Fact]
    public void CreateFromParsed_WithExperienceCompanyMissing_ReturnsExperienceCompanyRequired()
    {
        // A degraded parse where an experience entry has no company → SAME code as
        // UpdateMasterContent (ValidateContent is shared).
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", null, null, null),
            experiences: new[]
            {
                new Experience(string.Empty, "Backend-utvecklare", new DateOnly(2021, 1, 1), null, null),
            });

        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, content, ValidSourceParsedId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceCompanyRequired");
    }

    // ---------------------------------------------------------------
    // Rename
    // ---------------------------------------------------------------

    [Fact]
    public void Rename_WithValidName_ReturnsSuccess()
    {
        var resume = CreateValidResume();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = resume.Rename("Nytt namn", laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.Name.ShouldBe("Nytt namn");
        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void Rename_TrimsName()
    {
        var resume = CreateValidResume();

        var result = resume.Rename("  Nytt namn  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.Name.ShouldBe("Nytt namn");
    }

    [Fact]
    public void Rename_WithEmptyName_ReturnsFailure()
    {
        var resume = CreateValidResume();

        var result = resume.Rename(string.Empty, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public void Rename_WithWhitespaceName_ReturnsFailure()
    {
        var resume = CreateValidResume();

        var result = resume.Rename("   ", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public void Rename_WithNameTooLong_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var tooLong = new string('A', 201);

        var result = resume.Rename(tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameTooLong");
    }

    // ---------------------------------------------------------------
    // UpdateMasterContent — happy path
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithValidContent_ReturnsSuccess()
    {
        var resume = CreateValidResume();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", "klas@example.com", "0701234567", "Stockholm"),
            summary: "Senior backend-utvecklare med 10 års erfarenhet.");

        var result = resume.UpdateMasterContent(content, laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.ShouldBe(content);
        resume.MasterVersion.UpdatedAt.ShouldBe(laterClock.UtcNow);
        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void UpdateMasterContent_WithValidContent_RaisesResumeContentUpdatedEvent()
    {
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var content = new ResumeContent(new PersonalInfo("Klas Olsson", null, null, null));

        resume.UpdateMasterContent(content, Clock);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeContentUpdatedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.VersionId.ShouldBe(resume.MasterVersion.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // UpdateMasterContent — validering
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithEmptyFullName_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(new PersonalInfo(string.Empty, null, null, null));

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithFullNameTooLong_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var tooLong = new string('A', 201);
        var content = new ResumeContent(new PersonalInfo(tooLong, null, null, null));

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameTooLong");
    }

    [Fact]
    public void UpdateMasterContent_WithSummaryTooLong_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var tooLong = new string('A', 2_001);
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            summary: tooLong);

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SummaryTooLong");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillNameEmpty_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill(string.Empty, 5) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillNameRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillYearsNegative_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", -1) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillYearsOutOfRange");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillYearsExceedingMax_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 71) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillYearsOutOfRange");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillYearsAtBoundaries_ReturnsSuccess()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 0), new Skill("Python", 70) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithExperienceCompanyEmpty_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience(string.Empty, "Backend Developer", new DateOnly(2020, 1, 1), null, null)
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceCompanyRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithExperienceRoleEmpty_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Mastercard", string.Empty, new DateOnly(2020, 1, 1), null, null)
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceRoleRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithExperienceEndDateBeforeStartDate_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience(
                    "Mastercard",
                    "Backend Developer",
                    new DateOnly(2024, 6, 1),
                    new DateOnly(2024, 1, 1),
                    null)
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceDatesInvalid");
    }

    [Fact]
    public void UpdateMasterContent_WithEducationInstitutionEmpty_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            educations: new[]
            {
                new Education(string.Empty, "MSc CS", new DateOnly(2018, 9, 1), null)
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationInstitutionRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithEducationDegreeEmpty_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            educations: new[]
            {
                new Education("KTH", string.Empty, new DateOnly(2018, 9, 1), null)
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDegreeRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithEducationEndDateBeforeStartDate_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            educations: new[]
            {
                new Education(
                    "KTH",
                    "MSc CS",
                    new DateOnly(2020, 9, 1),
                    new DateOnly(2018, 6, 1))
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDatesInvalid");
    }

    // ---------------------------------------------------------------
    // DeleteVersion
    // ---------------------------------------------------------------

    [Fact]
    public void DeleteVersion_WithUnknownVersionId_ReturnsNotFound()
    {
        var resume = CreateValidResume();
        var unknownId = ResumeVersionId.New();

        var result = resume.DeleteVersion(unknownId, isReferencedByOpenApplication: false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");
    }

    [Fact]
    public void DeleteVersion_WhenTargetIsMaster_ReturnsFailure()
    {
        var resume = CreateValidResume();
        var masterId = resume.MasterVersion.Id;

        var result = resume.DeleteVersion(masterId, isReferencedByOpenApplication: false, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.MasterCannotBeDeleted");
        resume.MasterVersion.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void DeleteVersion_WhenTargetIsMasterAndReferenced_StillReturnsMasterCannotBeDeleted()
    {
        // Master-checken kommer före VersionInUse-checken — viktigt
        // ordningsskydd. Detta är även det enda sättet att i nuvarande
        // publika API verifiera att Master-checken är prioriterad.
        var resume = CreateValidResume();
        var masterId = resume.MasterVersion.Id;

        var result = resume.DeleteVersion(masterId, isReferencedByOpenApplication: true, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.MasterCannotBeDeleted");
    }

    [Fact]
    public void DeleteVersion_WhenTargetIsTailoredAndReferenced_ReturnsVersionInUse()
    {
        // Fas 4 STEG A: VersionInUse-grenen är nu direkttestbar via det öppnade
        // CreateTailored-flödet. Master-checken får INTE kortsluta den här —
        // målet är en Tailored-version, så vi når VersionInUse-grenen.
        var resume = CreateValidResume();
        var tailoredId = resume.CreateTailored(ValidTailoredContent, Clock).Value;

        var result = resume.DeleteVersion(tailoredId, isReferencedByOpenApplication: true, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.VersionInUse");
        // Versionen är fortfarande aktiv — guarden raderar inte.
        resume.Versions.ShouldContain(v => v.Id == tailoredId && v.DeletedAt == null);
    }

    [Fact]
    public void DeleteVersion_WhenTargetIsTailoredAndNotReferenced_ReturnsSuccessAndSoftDeletes()
    {
        // Komplement till VersionInUse: en oreferenced Tailored-version kan raderas.
        var resume = CreateValidResume();
        var tailoredId = resume.CreateTailored(ValidTailoredContent, Clock).Value;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = resume.DeleteVersion(tailoredId, isReferencedByOpenApplication: false, laterClock);

        result.IsSuccess.ShouldBeTrue();
        var tailored = resume.Versions.Single(v => v.Id == tailoredId);
        tailored.DeletedAt.ShouldBe(laterClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // CreateTailored — happy path
    // ---------------------------------------------------------------

    [Fact]
    public void CreateTailored_WithValidContent_AddsTailoredVersionAndReturnsItsId()
    {
        var resume = CreateValidResume();
        var versionCountBefore = resume.Versions.Count;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = resume.CreateTailored(ValidTailoredContent, laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.Versions.Count.ShouldBe(versionCountBefore + 1);

        result.Value.ShouldNotBe(default);
        var newVersion = resume.Versions.Single(v => v.Id == result.Value);
        newVersion.Kind.ShouldBe(ResumeVersionKind.Tailored);
        newVersion.DeletedAt.ShouldBeNull();
        newVersion.Content.ShouldBe(ValidTailoredContent);
        newVersion.CreatedAt.ShouldBe(laterClock.UtcNow);
        newVersion.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void CreateTailored_WithValidContent_AdvancesUpdatedAt()
    {
        var resume = CreateValidResume();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));

        resume.CreateTailored(ValidTailoredContent, laterClock);

        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void CreateTailored_WithValidContent_KeepsMasterInvariantSatisfied()
    {
        // En Tailored-version får inte rubba "exakt en aktiv Master"-invarianten.
        var resume = CreateValidResume();
        var masterIdBefore = resume.MasterVersion.Id;

        resume.CreateTailored(ValidTailoredContent, Clock);

        var master = resume.MasterVersion; // får inte kasta MasterInvariantBroken
        master.Id.ShouldBe(masterIdBefore);
        master.Kind.ShouldBe(ResumeVersionKind.Master);
    }

    [Fact]
    public void CreateTailored_DoesNotTouchDenormalizedProjection()
    {
        // ADR 0059: bara Master-innehåll driver LatestRole/SectionCount/TopSkills.
        // En Tailored-version med rikt innehåll får INTE spegla in i projektionen.
        var resume = CreateValidResume();
        // Initialt (tom Master) projektionsläge.
        resume.LatestRole.ShouldBeNull();
        resume.SectionCount.ShouldBe(0);
        resume.TopSkills.ShouldBeEmpty();

        var richTailored = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Tailored AB", "Tailored Role", new DateOnly(2025, 1, 1), null, null),
            },
            skills: new[] { new Skill("Tailored-Skill", 3) },
            summary: "Skräddarsydd sammanfattning.");

        resume.CreateTailored(richTailored, Clock);

        // Projektionen är oförändrad — Tailored-innehåll syns inte.
        resume.LatestRole.ShouldBeNull();
        resume.SectionCount.ShouldBe(0);
        resume.TopSkills.ShouldBeEmpty();
    }

    [Fact]
    public void CreateTailored_WithValidContent_RaisesResumeVersionCreatedEventForTailored()
    {
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));

        var result = resume.CreateTailored(ValidTailoredContent, laterClock);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeVersionCreatedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.VersionId.ShouldBe(result.Value);
        evt.Kind.ShouldBe(ResumeVersionKind.Tailored);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // CreateTailored — validering (delar ValidateContent med UpdateMasterContent)
    // ---------------------------------------------------------------

    [Fact]
    public void CreateTailored_WithEmptyFullName_ReturnsFailureAndAddsNoVersion()
    {
        var resume = CreateValidResume();
        var versionCountBefore = resume.Versions.Count;
        var content = new ResumeContent(new PersonalInfo(string.Empty, null, null, null));

        var result = resume.CreateTailored(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
        resume.Versions.Count.ShouldBe(versionCountBefore);
    }

    [Fact]
    public void CreateTailored_WithExperienceCompanyEmpty_ReturnsFailureAndAddsNoVersion()
    {
        var resume = CreateValidResume();
        var versionCountBefore = resume.Versions.Count;
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience(string.Empty, "Backend Developer", new DateOnly(2020, 1, 1), null, null)
            });

        var result = resume.CreateTailored(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceCompanyRequired");
        resume.Versions.Count.ShouldBe(versionCountBefore);
    }

    [Fact]
    public void CreateTailored_WhenValidationFails_RaisesNoDomainEvent()
    {
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var content = new ResumeContent(new PersonalInfo(string.Empty, null, null, null));

        resume.CreateTailored(content, Clock);

        resume.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void CreateTailored_WhenValidationFails_DoesNotAdvanceUpdatedAt()
    {
        var resume = CreateValidResume();
        var updatedAtBefore = resume.UpdatedAt;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(4));
        var content = new ResumeContent(new PersonalInfo(string.Empty, null, null, null));

        resume.CreateTailored(content, laterClock);

        resume.UpdatedAt.ShouldBe(updatedAtBefore);
    }

    // ---------------------------------------------------------------
    // SoftDelete
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_SetsDeletedAt()
    {
        var resume = CreateValidResume();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));

        resume.SoftDelete(laterClock);

        resume.DeletedAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_CascadesToAllVersions()
    {
        var resume = CreateValidResume();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));

        resume.SoftDelete(laterClock);

        resume.Versions.ShouldAllBe(v => v.DeletedAt == laterClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_RaisesResumeDeletedDomainEvent()
    {
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));

        resume.SoftDelete(laterClock);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeDeletedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_KeepsFirstDeletedAtTimestamp()
    {
        // Idempotens-invariant: andra anropet får INTE skriva över DeletedAt
        // med en ny timestamp. Klockorna ger olika UtcNow så överskrivning
        // är detekterbar.
        var resume = CreateValidResume();
        var firstClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));
        var secondClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));

        resume.SoftDelete(firstClock);
        resume.SoftDelete(secondClock);

        resume.DeletedAt.ShouldBe(firstClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotentAndDoesNotRaiseEvent()
    {
        // Exakt ETT ResumeDeletedDomainEvent över två anrop — andra anropet
        // får inte raisa ett falskt historiskt faktum.
        var resume = CreateValidResume();
        var firstClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));
        var secondClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));

        resume.SoftDelete(firstClock);
        resume.ClearDomainEvents();
        resume.SoftDelete(secondClock);

        resume.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_KeepsFirstDeletedAtOnAllVersions()
    {
        // Cascaden får inte skriva om versionernas DeletedAt vid andra anropet.
        var resume = CreateValidResume();
        var firstClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));
        var secondClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));

        resume.SoftDelete(firstClock);
        resume.SoftDelete(secondClock);

        resume.Versions.ShouldAllBe(v => v.DeletedAt == firstClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenActive_SetsDeletedAtCascadesAndRaisesExactlyOneEvent()
    {
        // Happy-path-komplettering: första anropet sätter DeletedAt,
        // cascade:ar versioner och raisar exakt ett ResumeDeletedDomainEvent.
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(1));

        resume.SoftDelete(laterClock);

        resume.DeletedAt.ShouldBe(laterClock.UtcNow);
        resume.Versions.ShouldAllBe(v => v.DeletedAt == laterClock.UtcNow);
        resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeDeletedDomainEvent>();
    }

    // ---------------------------------------------------------------
    // F6 Prompt 3 — Language (Sv/En) default + SetLanguage
    // ---------------------------------------------------------------

    [Fact]
    public void Create_DefaultsLanguageToSv()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Language.ShouldBe(ResumeLanguage.Sv);
    }

    [Fact]
    public void Create_EmptyContent_DenormFieldsAreInitial()
    {
        var resume = CreateValidResume();

        resume.LatestRole.ShouldBeNull();
        resume.SectionCount.ShouldBe(0);
        resume.TopSkills.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateMasterContent_WithExperiences_LatestRoleMatchesMostRecentByStartDate()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Acme", "Junior", new DateOnly(2018, 1, 1), new DateOnly(2020, 1, 1), null),
                new Experience("Beta", "Lead", new DateOnly(2024, 6, 1), null, null),
                new Experience("Gamma", "Mid", new DateOnly(2021, 1, 1), new DateOnly(2024, 5, 1), null),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.LatestRole.ShouldBe("Lead");
    }

    [Fact]
    public void UpdateMasterContent_WithExperiencesSameStartDate_LatestRoleIsStableFirst()
    {
        // OrderByDescending är stable i LINQ-to-Objects — vid lika nyckel
        // behålls input-ordningen. Första experience i input ska vinna.
        var sameStart = new DateOnly(2024, 1, 1);
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Acme", "Role-First", sameStart, null, null),
                new Experience("Beta", "Role-Second", sameStart, null, null),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.LatestRole.ShouldBe("Role-First");
    }

    [Fact]
    public void UpdateMasterContent_WithSixSkills_TopSkillsLimitedToFive()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[]
            {
                new Skill("C#", 10),
                new Skill("PostgreSQL", 5),
                new Skill("TypeScript", 7),
                new Skill("Docker", 4),
                new Skill("AWS", 3),
                new Skill("Kubernetes", 2),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.TopSkills.Count.ShouldBe(5);
        resume.TopSkills[0].ShouldBe("C#");
        resume.TopSkills[1].ShouldBe("PostgreSQL");
        resume.TopSkills[2].ShouldBe("TypeScript");
        resume.TopSkills[3].ShouldBe("Docker");
        resume.TopSkills[4].ShouldBe("AWS");
    }

    [Fact]
    public void UpdateMasterContent_WithAllFourSections_SectionCountIsFour()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Acme", "Dev", new DateOnly(2020, 1, 1), null, null),
            },
            educations: new[]
            {
                new Education("KTH", "MSc CS", new DateOnly(2015, 9, 1), new DateOnly(2018, 6, 1)),
            },
            skills: new[] { new Skill("C#", 10) },
            summary: "Sammanfattning.");

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.SectionCount.ShouldBe(4);
    }

    [Fact]
    public void UpdateMasterContent_WithOnlySummary_SectionCountIsOne()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            summary: "Bara en sammanfattning.");

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.SectionCount.ShouldBe(1);
    }

    [Fact]
    public void UpdateMasterContent_WithWhitespaceSummary_SectionCountDoesNotIncrement()
    {
        // Whitespace-only summary räknas inte som en sektion
        // (IsNullOrWhiteSpace-check i ComputeDenormalizedProjection).
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            summary: "   ");

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.SectionCount.ShouldBe(0);
    }

    [Fact]
    public void UpdateMasterContent_FromPopulatedToEmpty_DenormFieldsReset()
    {
        // Regression-skydd: ApplyDenormalizedProjection måste nolla state.
        var resume = CreateValidResume();
        var populated = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Acme", "Senior", new DateOnly(2024, 1, 1), null, null),
            },
            skills: new[] { new Skill("C#", 10), new Skill("SQL", 5) },
            summary: "Något.");
        resume.UpdateMasterContent(populated, Clock).IsSuccess.ShouldBeTrue();
        resume.LatestRole.ShouldBe("Senior");
        resume.TopSkills.Count.ShouldBe(2);
        resume.SectionCount.ShouldBe(3);

        var empty = new ResumeContent(new PersonalInfo(ValidFullName, null, null, null));
        var result = resume.UpdateMasterContent(empty, Clock);

        result.IsSuccess.ShouldBeTrue();
        resume.LatestRole.ShouldBeNull();
        resume.SectionCount.ShouldBe(0);
        resume.TopSkills.ShouldBeEmpty();
    }

    [Fact]
    public void SetLanguage_ChangesLanguage_RaisesEventAndUpdatesTimestamp()
    {
        var resume = CreateValidResume();
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));

        var result = resume.SetLanguage(ResumeLanguage.En, laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.Language.ShouldBe(ResumeLanguage.En);
        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);
        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeLanguageChangedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.NewLanguage.ShouldBe(ResumeLanguage.En);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SetLanguage_Null_ReturnsValidationFailure()
    {
        var resume = CreateValidResume();

        var result = resume.SetLanguage(null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.LanguageRequired");
    }

    [Fact]
    public void SetLanguage_SameLanguage_NoEventNoTimestampUpdate()
    {
        var resume = CreateValidResume();
        var initialUpdatedAt = resume.UpdatedAt;
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(5));

        var result = resume.SetLanguage(ResumeLanguage.Sv, laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.Language.ShouldBe(ResumeLanguage.Sv);
        resume.UpdatedAt.ShouldBe(initialUpdatedAt);
        resume.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // MasterVersion — invariant
    // ---------------------------------------------------------------

    [Fact]
    public void MasterVersion_AfterCreate_ReturnsTheInitialMasterVersion()
    {
        var resume = CreateValidResume();

        var master = resume.MasterVersion;

        master.Kind.ShouldBe(ResumeVersionKind.Master);
        master.DeletedAt.ShouldBeNull();
        resume.Versions.ShouldContain(master);
    }

    [Fact]
    public void MasterVersion_WhenNoActiveMaster_ThrowsDomainException()
    {
        // N-3: invariant-brott simulerat via EF-rehydrering-scenario (0 aktiva
        // Master-versioner). Backing-fältet manipuleras via reflection eftersom
        // domain-API:t inte tillåter Master-deletion — invarianten skyddas just
        // av detta property.
        var resume = CreateValidResume();
        ClearVersions(resume);

        var ex = Should.Throw<DomainException>(() => _ = resume.MasterVersion);
        ex.Code.ShouldBe("Resume.MasterInvariantBroken");
    }

    [Fact]
    public void MasterVersion_WhenMultipleActiveMasters_ThrowsDomainException()
    {
        // N-3: invariant-brott simulerat via EF-rehydrering-scenario (2 aktiva
        // Master-versioner) — db-corruption-skydd.
        var resume = CreateValidResume();
        DuplicateMaster(resume);

        var ex = Should.Throw<DomainException>(() => _ = resume.MasterVersion);
        ex.Code.ShouldBe("Resume.MasterInvariantBroken");
    }

    // ---------------------------------------------------------------
    // UpdateMasterContent — Fas 4b AppCopy superset invariants (ADR 0095 D-E, #651).
    // ValidateContent is shared with CreateFromParsed/CreateTailored, so pinning the codes
    // here covers every write surface.
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_WithLanguageNameEmpty_ReturnsLanguageNameRequired()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            languages: new[] { new SpokenLanguage(string.Empty, LanguageProficiency.Native) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.LanguageNameRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillGroupNameEmpty_ReturnsSkillGroupNameRequired()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 8) },
            skillGroups: new[] { new SkillGroup(string.Empty, ["C#"]) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillGroupNameRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillGroupMemberNotInSkills_ReturnsSkillGroupMemberUnknown()
    {
        // A group member absent from the flat Skills list is a dangling reference — a phantom
        // skill the user did not write (ADR 0095 D-A membership invariant, CLAUDE.md §5).
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 8) },
            skillGroups: new[] { new SkillGroup("Backend", ["Rust"]) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillGroupMemberUnknown");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillGroupMemberCaseMismatch_ReturnsSkillGroupMemberUnknown()
    {
        // Membership is ordinal-exact (StringComparer.Ordinal): "c#" is not the skill "C#".
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 8) },
            skillGroups: new[] { new SkillGroup("Backend", ["c#"]) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SkillGroupMemberUnknown");
    }

    [Fact]
    public void UpdateMasterContent_WithSkillGroupMembersAllKnown_ReturnsSuccess()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 8), new Skill("PostgreSQL", 5) },
            skillGroups: new[] { new SkillGroup("Backend", ["C#", "PostgreSQL"]) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithUngroupedSkills_ReturnsSuccess()
    {
        // Not every skill need be grouped (design handoff P4 — "the file wins"): PostgreSQL is
        // ungrouped while the group references only C#. Legitimate, never an error.
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            skills: new[] { new Skill("C#", 8), new Skill("PostgreSQL", 5) },
            skillGroups: new[] { new SkillGroup("Backend", ["C#"]) });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithSectionHeadingEmpty_ReturnsSectionHeadingRequired()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            sections: new[]
            {
                new ResumeSection(string.Empty, new[] { new SectionEntry("Titel", ["Rad"]) }),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SectionHeadingRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithSectionEntryTitleEmpty_ReturnsSectionEntryTitleRequired()
    {
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            sections: new[]
            {
                new ResumeSection("Projekt", new[] { new SectionEntry(string.Empty, ["Rad"]) }),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SectionEntryTitleRequired");
    }

    [Fact]
    public void UpdateMasterContent_WithSectionEntryLinesOverLimit_ReturnsSectionEntryTooLong()
    {
        // Sum of an entry's Lines lengths > 2000 → too long. Two lines summing to 2001.
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            sections: new[]
            {
                new ResumeSection("Projekt", new[]
                {
                    new SectionEntry("Titel", new[] { new string('A', 1_000), new string('B', 1_001) }),
                }),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.SectionEntryTooLong");
    }

    [Fact]
    public void UpdateMasterContent_WithSectionEntryLinesExactlyAtLimit_ReturnsSuccess()
    {
        // Exactly 2000 chars across the entry's Lines passes — the bound is strictly > 2000.
        var resume = CreateValidResume();
        var content = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            sections: new[]
            {
                new ResumeSection("Projekt", new[]
                {
                    new SectionEntry("Titel", new[] { new string('A', 1_000), new string('B', 1_000) }),
                }),
            });

        var result = resume.UpdateMasterContent(content, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateMasterContent_WithFullyPopulatedValidSuperset_ReturnsSuccess()
    {
        var resume = CreateValidResume();

        var result = resume.UpdateMasterContent(FullSupersetContent(), Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Fas 4b superset — denormalized-projection invariance (MatchProfileBuilder regression
    // check, #651). MatchProfileBuilder reads only LatestRole + confirmed skills; the superset
    // fields must NOT leak into the denormalized projection (ComputeDenormalizedProjection
    // ignores Languages/SkillGroups/Sections).
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_AddingSupersetFields_DoesNotChangeDenormalizedProjection()
    {
        var baseContent = new ResumeContent(
            new PersonalInfo(ValidFullName, null, null, null),
            experiences: new[]
            {
                new Experience("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde."),
            },
            educations: new[]
            {
                new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
            },
            skills: new[] { new Skill("C#", 8), new Skill("PostgreSQL", 5) },
            summary: "Erfaren backend-utvecklare.");

        var supersetContent = baseContent with
        {
            Languages = new[] { new SpokenLanguage("Svenska", LanguageProficiency.Native) },
            SkillGroups = new[] { new SkillGroup("Backend", ["C#"]) },
            Sections = new[]
            {
                new ResumeSection("Projekt", new[] { new SectionEntry("X", ["Rad"]) }),
            },
        };

        var withoutSuperset = CreateValidResume();
        withoutSuperset.UpdateMasterContent(baseContent, Clock).IsSuccess.ShouldBeTrue();

        var withSuperset = CreateValidResume();
        withSuperset.UpdateMasterContent(supersetContent, Clock).IsSuccess.ShouldBeTrue();

        // Denormalized projection is identical — the superset fields are invisible to it.
        withSuperset.LatestRole.ShouldBe(withoutSuperset.LatestRole);
        withSuperset.SectionCount.ShouldBe(withoutSuperset.SectionCount);
        withSuperset.TopSkills.ShouldBe(withoutSuperset.TopSkills);

        // Anchor the concrete values: summary + experiences + educations + skills = 4 canonical
        // sections, regardless of the superset fields (SectionCount stays the fixed 0–4 count).
        withSuperset.LatestRole.ShouldBe("Backend-utvecklare");
        withSuperset.SectionCount.ShouldBe(4);
        withSuperset.TopSkills.ShouldBe(["C#", "PostgreSQL"]);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    // Rich, valid content exercising every Fas 4b superset field (ADR 0095 D-E). The skill-group
    // members are a subset of the flat Skills list so the membership invariant holds.
    private static ResumeContent FullSupersetContent() => new(
        new PersonalInfo("Klas Olsson", "klas@example.com", "0701234567", "Stockholm"),
        experiences: new[]
        {
            new Experience("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        },
        educations: new[]
        {
            new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
        },
        skills: new[] { new Skill("C#", 8), new Skill("PostgreSQL", 5) },
        summary: "Erfaren backend-utvecklare.",
        languages: new[]
        {
            new SpokenLanguage("Svenska", LanguageProficiency.Native),
            new SpokenLanguage("Tyska", LanguageProficiency.NotStated),
        },
        skillGroups: new[] { new SkillGroup("Backend", ["C#", "PostgreSQL"]) },
        sections: new[]
        {
            new ResumeSection("Projekt och arbetsprov", new[]
            {
                new SectionEntry("Betalplattform", ["Ledde ett team om 8."]),
            }),
        });

    private static Resume CreateValidResume() =>
        Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock).Value;

    // Reflection-helpers används bara för att simulera EF-rehydrering med
    // inkonsistent state — invarianten skyddas av domain-API:t, så det finns ingen
    // legitim väg att nå "0 Masters" eller "2 Masters" via public surface. Om
    // backing-fältet `_versions` renamas: uppdatera fält-strängen här.
    private static void ClearVersions(Resume resume)
    {
        var field = typeof(Resume).GetField("_versions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var list = (System.Collections.IList)field!.GetValue(resume)!;
        list.Clear();
    }

    private static void DuplicateMaster(Resume resume)
    {
        var field = typeof(Resume).GetField("_versions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var list = (System.Collections.IList)field!.GetValue(resume)!;
        // Lägger samma referens igen — tillräckligt för LINQ-Where-count att
        // räkna 2 aktiva Masters. Semantiskt: simulerar att db-row förekommer
        // dubblerat efter korrupt rehydrering.
        list.Add(list[0]!);
    }
}
