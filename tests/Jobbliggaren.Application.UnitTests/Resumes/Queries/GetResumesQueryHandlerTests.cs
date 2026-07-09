using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Review;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

public class GetResumesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // Fas 4b CV-motor v2 PR-8.1 (#657, CTO-bind Q6): the list projection gains OpenFindingCount, driven
    // by the CURRENT rubric version the handler reads from IRubricProvider. Uses the REAL provider
    // (committed asset — golden source, parity SetFindingStatusCommandHandlerTests) so "current" is the
    // shipped version; StaleVersion is any older version deliberately != current.
    private readonly IRubricProvider _rubricProvider = CvReviewFixtures.RealRubricProvider();
    private static readonly string CurrentVersion = CvReviewFixtures.RealRubric().Version.ToString();
    private const string StaleVersion = "1.0.0";
    private static readonly string Fp = new('a', 64);

    public GetResumesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static async Task<Resume> AddResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        JobSeeker seeker,
        string name,
        IDateTimeProvider clock)
    {
        var resume = Resume.Create(seeker.Id, name, "Klas Olsson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetResumesQueryHandler(db, currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyResumesBelongingToUser()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        await AddResumeAsync(db, ownSeeker, "Mitt CV", FakeDateTimeProvider.Default);

        var otherSeeker = await SeedSeekerAsync(db, Guid.NewGuid());
        await AddResumeAsync(db, otherSeeker, "Annans CV", FakeDateTimeProvider.Default);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        result.Items[0].Name.ShouldBe("Mitt CV");
    }

    [Fact]
    public async Task Handle_ReturnsResumesSortedByUpdatedAtDescending()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var older = new FakeDateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = new FakeDateTimeProvider(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        await AddResumeAsync(db, seeker, "Äldre CV", older);
        await AddResumeAsync(db, seeker, "Nyare CV", newer);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        result.Items[0].Name.ShouldBe("Nyare CV");
        result.Items[1].Name.ShouldBe("Äldre CV");
    }

    [Fact]
    public async Task Handle_AppliesPagination()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        for (var i = 0; i < 5; i++)
        {
            var clock = new FakeDateTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i));
            await AddResumeAsync(db, seeker, $"CV {i}", clock);
        }

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var page1 = await handler.Handle(new GetResumesQuery(Page: 1, PageSize: 2), CancellationToken.None);
        var page2 = await handler.Handle(new GetResumesQuery(Page: 2, PageSize: 2), CancellationToken.None);
        var page3 = await handler.Handle(new GetResumesQuery(Page: 3, PageSize: 2), CancellationToken.None);

        page1.Items.Count.ShouldBe(2);
        page2.Items.Count.ShouldBe(2);
        page3.Items.Count.ShouldBe(1);

        // TotalCount är independent of page-size — regression-skydd för PagedResult-kontraktet.
        page1.TotalCount.ShouldBe(5);
        page2.TotalCount.ShouldBe(5);
        page3.TotalCount.ShouldBe(5);
        page1.TotalPages.ShouldBe(3);
    }

    // Note: ett tidigare test "Handle_VersionCountIncludesOnlyNonDeletedVersions" togs bort —
    // EF InMemory applicerar inte global query filter på relaterade collections som Postgres
    // gör, vilket gjorde testet vilseledande. Verifiering av soft-delete-räkning sker
    // naturligare i integration-tester när Tailored-flödet öppnas i Fas 4.

    // ---------------------------------------------------------------
    // F6 Prompt 3 — IsPrimary + Language + denormaliserade fält
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsDtoWithIsPrimaryTrueOnlyForJobSeekerPrimaryResume()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var resumeA = await AddResumeAsync(db, seeker, "CV-A", FakeDateTimeProvider.Default);
        var resumeB = await AddResumeAsync(db, seeker, "CV-B", FakeDateTimeProvider.Default);

        // Markera CV-B som primary
        seeker.SetPrimaryResume(resumeB.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        var dtoA = result.Items.Single(i => i.Id == resumeA.Id.Value);
        var dtoB = result.Items.Single(i => i.Id == resumeB.Id.Value);
        dtoA.IsPrimary.ShouldBeFalse();
        dtoB.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_JobSeekerWithoutPrimary_AllDtosHaveIsPrimaryFalse()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        await AddResumeAsync(db, seeker, "CV-A", FakeDateTimeProvider.Default);
        await AddResumeAsync(db, seeker, "CV-B", FakeDateTimeProvider.Default);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.Items.ShouldAllBe(dto => dto.IsPrimary == false);
    }

    [Fact]
    public async Task Handle_PopulatesLanguageLatestRoleSectionCountTopSkillsFromResume()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var resume = await AddResumeAsync(db, seeker, "Profil", FakeDateTimeProvider.Default);

        // Sätt språk + populera content för denorm-fält
        resume.SetLanguage(ResumeLanguage.En, FakeDateTimeProvider.Default);
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            experiences: new[]
            {
                new Experience("Acme", "Senior Backend", new DateOnly(2023, 1, 1), null, null),
                new Experience("Beta", "Junior", new DateOnly(2018, 1, 1), new DateOnly(2020, 1, 1), null),
            },
            skills: new[]
            {
                new Skill("C#", 10),
                new Skill("PostgreSQL", 5),
                new Skill("Docker", 3),
            },
            summary: "Sammanfattning.");
        resume.UpdateMasterContent(content, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.Language.ShouldBe("En");
        dto.LatestRole.ShouldBe("Senior Backend");
        // Experience + Skill + Summary populerade
        dto.SectionCount.ShouldBe(3);
        dto.TopSkills.Count.ShouldBe(3);
        dto.TopSkills[0].ShouldBe("C#");
    }

    // ===============================================================
    // Fas 4b CV-motor v2 PR-8.1 (#657, CTO-bind Q6): OpenFindingCount + Origin + Template projection.
    // OpenFindingCount counts OPEN findings AT THE CURRENT rubric version only, and is null (never 0)
    // when the CV was not reviewed at the current version — so "not reviewed" stays distinguishable
    // from "reviewed, nothing open". RED until the DTO fields + the IRubricProvider-driven projection
    // ship. (Rows arranged via ReconcileFindingStatuses, itself RED until PR-8.1's aggregate method.)
    // ===============================================================

    [Fact]
    public async Task Handle_OpenFindingCount_CountsOnlyCurrentVersionOpenRows()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var resume = await AddResumeAsync(db, seeker, "CV", FakeDateTimeProvider.Default);

        // 3 Open rows at an OLDER rubric version (must NOT count).
        resume.SetFindingStatus(StaleVersion, "D1", ReviewFindingStatus.Open, Fp, FakeDateTimeProvider.Default);
        resume.SetFindingStatus(StaleVersion, "D2", ReviewFindingStatus.Open, Fp, FakeDateTimeProvider.Default);
        resume.SetFindingStatus(StaleVersion, "D3", ReviewFindingStatus.Open, Fp, FakeDateTimeProvider.Default);
        // At the CURRENT version: 1 Resolved + 1 Ignored (not Open, must NOT count) ...
        resume.SetFindingStatus(CurrentVersion, "A3", ReviewFindingStatus.Resolved, Fp, FakeDateTimeProvider.Default);
        resume.SetFindingStatus(CurrentVersion, "C2", ReviewFindingStatus.Ignored, Fp, FakeDateTimeProvider.Default);
        // ... plus 2 Open, stamped via the reconcile path (ReviewedRubricVersion := current).
        resume.ReconcileFindingStatuses(
            CurrentVersion,
            [new ReviewFindingSnapshot("A1", Fp), new ReviewFindingSnapshot("A2", Fp)],
            FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);
        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldHaveSingleItem().OpenFindingCount.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_OpenFindingCount_IsNull_WhenResumeNeverReviewed()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        await AddResumeAsync(db, seeker, "CV", FakeDateTimeProvider.Default);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);
        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        // Never reviewed (ReviewedRubricVersion null) → null, NEVER 0.
        result.Items.ShouldHaveSingleItem().OpenFindingCount.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_OpenFindingCount_IsNull_WhenReviewedAtAStaleRubricVersion()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var resume = await AddResumeAsync(db, seeker, "CV", FakeDateTimeProvider.Default);
        // Reviewed against an OLD rubric version → the current-version open count is unknowable → null.
        resume.ReconcileFindingStatuses(
            StaleVersion, [new ReviewFindingSnapshot("A1", Fp)], FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);
        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldHaveSingleItem().OpenFindingCount.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_OpenFindingCount_IsZero_WhenReviewedAtCurrentWithNoOpenRows()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var resume = await AddResumeAsync(db, seeker, "CV", FakeDateTimeProvider.Default);
        // Reviewed at the current version, everything passed (empty actionable set).
        resume.ReconcileFindingStatuses(CurrentVersion, [], FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);
        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        // Reviewed-and-clean is 0, distinct from never-reviewed (null).
        result.Items.ShouldHaveSingleItem().OpenFindingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ProjectsOriginAndTemplateNames_FromResume()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Resume.Create stamps Origin = Template; TemplateOptions defaults to Klar.
        await AddResumeAsync(db, seeker, "CV", FakeDateTimeProvider.Default);

        var handler = new GetResumesQueryHandler(db, _currentUser, _rubricProvider);
        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.Origin.ShouldBe("Template");
        dto.Template.ShouldBe("Klar");
    }
}
