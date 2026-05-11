using System.Reflection;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using JobbPilot.Domain.Resumes.Events;
using JobbPilot.Domain.UnitTests.JobAds;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Resumes;

public class ResumeTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private const string ValidName = "Mitt CV";
    private const string ValidFullName = "Klas Olsson";

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

    // OBS: VersionInUse-grenen (isReferencedByOpenApplication=true för en
    // Tailored-version) går inte att direkttesta i STEG 7 — det publika
    // API:et exponerar inte CreateTailoredVersion ännu. Regression-skyddet
    // för den path:en läggs till när Tailored-flödet öppnas i Fas 4.

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
    // Hjälpmetoder
    // ---------------------------------------------------------------

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
