using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Profiles;

/// <summary>
/// ADR 0079 STEG 3 PR-D (Beslut 1, the scorer reroute) — the TWO FULL-profile builders on
/// <see cref="IMatchProfileBuilder"/> (the existing
/// <see cref="IMatchProfileBuilder.BuildFromPreferencesAsync"/> stays unchanged and is pinned
/// by <see cref="MatchProfileBuilderTests"/>):
/// <list type="bullet">
/// <item><b>BuildFullForSortAsync (the global SORT path)</b> and</item>
/// <item><b>BuildFullForVerdictAsync (the page-scoped TAG/modal path)</b></item>
/// </list>
/// are now IDENTICAL and DEK-FREE: both return
/// <c>FullCandidateMatchProfile(fast-with-LatestRole, CvSkillConceptIds)</c> where
/// <c>CvSkillConceptIds = jobSeeker.MatchPreferences.PreferredSkills</c> — the user-CONFIRMED
/// plaintext concept-ids (the trusted capability source, ADR 0079 Beslut 1), NOT the raw CV
/// skill list. The reroute removes:
/// <list type="bullet">
/// <item>the <c>ISkillResolver</c> resolve (the confirmed set is already concept-ids);</item>
/// <item>the DEK warm (<c>ICurrentDataOwner.SetOwner</c> / <c>IUserDataKeyStore</c>) — the
/// confirmed set is plaintext on the JobSeeker, no encrypted Content read;</item>
/// <item>any read of <c>Resume.TopSkills</c> / <c>MasterVersion.Content.Skills</c>.</item>
/// </list>
/// The ONLY CV read is the denormalised plaintext <see cref="Resume.LatestRole"/> (ADR
/// 0058/0059, DEK-free, no <c>Include(Versions)</c>) for the Title dimension (STEG 4).
///
/// <para>
/// <b>NEW semantic (ADR 0079 Beslut 1):</b> the confirmed skills drive matching EVEN WITHOUT a
/// promoted CV (no <see cref="JobSeeker.PrimaryResumeId"/> → still
/// <c>CvSkillConceptIds = PreferredSkills</c>; only Title is empty). An empty confirmed set →
/// empty <c>CvSkillConceptIds</c> → the skill/requirement dimensions report
/// <c>NotAssessed</c> downstream (honest — nothing confirmed yet).
/// </para>
///
/// <para>
/// The ctor under test is the narrowed 2-arg form:
/// <c>MatchProfileBuilder(IAppDbContext db, ICurrentUser currentUser)</c>.
/// </para>
/// </summary>
public class MatchProfileBuilderFullTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // The confirmed skill set is sorted+distinct ordinal by MatchPreferences.NormalizeList,
    // so seed and assert against an already-sorted set to keep the assertion deterministic.
    private static readonly string[] ConfirmedSkills = ["skill_csharp", "skill_dotnet"];

    public MatchProfileBuilderFullTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private MatchProfileBuilder NewBuilder(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser);

    // ---------------------------------------------------------------
    // Seeding helpers.
    // ---------------------------------------------------------------

    private static async Task<JobSeeker> SeedSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        MatchPreferences prefs,
        ResumeId? primaryResumeId = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        seeker.UpdateMatchPreferences(prefs, FakeDateTimeProvider.Default);
        if (primaryResumeId is { } id)
            seeker.SetPrimaryResume(id, FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // A Resume whose Master content carries an Experience with the given role → the
    // denormalised plaintext Resume.LatestRole projection (ADR 0058/0059) is populated. The
    // builder reads ONLY LatestRole off the resume (DEK-free), so skills on the content are
    // irrelevant to the profile — the confirmed set comes from MatchPreferences.
    private static async Task<Resume> SeedResumeWithLatestRoleAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        JobSeekerId jobSeekerId,
        string latestRole)
    {
        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Test User", FakeDateTimeProvider.Default).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            experiences:
            [
                new Experience("Acme AB", latestRole,
                    new DateOnly(2024, 1, 1), null, null),
            ]);
        resume.UpdateMasterContent(content, FakeDateTimeProvider.Default);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    // A Resume with NO experiences → Resume.LatestRole stays null (it is derived from
    // content.Experiences) → honest empty Title.
    private static async Task<Resume> SeedResumeWithoutRoleAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        JobSeekerId jobSeekerId)
    {
        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Test User", FakeDateTimeProvider.Default).Value;
        var content = new ResumeContent(new PersonalInfo("Test User", null, null, null));
        resume.UpdateMasterContent(content, FakeDateTimeProvider.Default);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    // Prefs carrying the confirmed skill set + the four location/occupation dimensions.
    private static MatchPreferences PrefsWithSkills(IEnumerable<string>? skills) =>
        MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: null,
            preferredSkills: skills).Value;

    private static MatchPreferences Prefs() => PrefsWithSkills(ConfirmedSkills);

    private static void AssertFastFromPrefs(CandidateMatchProfile fast)
    {
        fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        fast.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
    }

    // =================================================================
    // The reroute (ADR 0079 Beslut 1): CvSkillConceptIds = the CONFIRMED
    // MatchPreferences.PreferredSkills — NOT the raw CV skills, NOT resolved, DEK-free.
    // Both Full overloads share one implementation, so each case runs against BOTH.
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_CvSkillConceptIds_AreTheConfirmedPreferredSkills()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        // The confirmed plaintext set IS the capability source (no resolve, no CV-skill read).
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        AssertFastFromPrefs(profile.Fast);
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public async Task BuildFullFromCvSkills_CvSkillConceptIds_AreTheConfirmedPreferredSkills()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        // Same source as the SORT path (the verdict surface reads what the user CONFIRMED).
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        AssertFastFromPrefs(profile.Fast);
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public async Task BothFullBuilders_AreEquivalent_SameSourceSameResult()
    {
        // The two overloads stay distinct interface members for their call-sites (sort vs
        // tag/modal) but share ONE implementation reading the SAME confirmed source — so for
        // any given JobSeeker they return an equivalent profile (the decisive sort==grade
        // coherence guarantee: no path can lift/drop an ad the other ignores).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var sort = await builder.BuildFullForSortAsync(CancellationToken.None);
        var tag = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        sort.CvSkillConceptIds.ShouldBe(tag.CvSkillConceptIds);
        sort.Fast.Title.ShouldBe(tag.Fast.Title);
        sort.Fast.SsykGroupConceptIds.ShouldBe(tag.Fast.SsykGroupConceptIds);
        sort.Fast.PreferredRegionConceptIds.ShouldBe(tag.Fast.PreferredRegionConceptIds);
        sort.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(tag.Fast.PreferredEmploymentTypeConceptIds);
        sort.Fast.PreferredMunicipalityConceptIds.ShouldBe(tag.Fast.PreferredMunicipalityConceptIds);
    }

    // =================================================================
    // NEW semantic (ADR 0079 Beslut 1): confirmed skills drive matching even WITHOUT a
    // promoted CV — no PrimaryResumeId → CvSkillConceptIds is still the confirmed set; only
    // Title is empty (no role to compare).
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_ConfirmedSkillsButNoPrimaryResume_CarriesSkills_EmptyTitle()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        profile.CvSkillConceptIds.ShouldBe(
            ConfirmedSkills,
            "Confirmed skills drive matching even without a promoted CV (ADR 0079 Beslut 1).");
        AssertFastFromPrefs(profile.Fast);
        profile.Fast.Title.ShouldBe(string.Empty, "No primary CV → no role → honest empty Title.");
    }

    [Fact]
    public async Task BuildFullFromCvSkills_ConfirmedSkillsButNoPrimaryResume_CarriesSkills_EmptyTitle()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        AssertFastFromPrefs(profile.Fast);
        profile.Fast.Title.ShouldBe(string.Empty);
    }

    // =================================================================
    // Empty confirmed set → empty CvSkillConceptIds (→ NotAssessed downstream, honest).
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_EmptyConfirmedSkills_ReturnsEmptyCvSkillConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, PrefsWithSkills(skills: null), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        AssertFastFromPrefs(profile.Fast);
        profile.CvSkillConceptIds.ShouldBeEmpty(
            "No confirmed skills → empty set → the skill/requirement dimensions report " +
            "NotAssessed downstream (honest, never faked).");
    }

    [Fact]
    public async Task BuildFullFromCvSkills_EmptyConfirmedSkills_ReturnsEmptyCvSkillConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, PrefsWithSkills(skills: null), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        AssertFastFromPrefs(profile.Fast);
        profile.CvSkillConceptIds.ShouldBeEmpty();
    }

    // =================================================================
    // Degrade-to-empty when there is no user / no JobSeeker.
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_NoUser_ReturnsDegradedFastEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new MatchProfileBuilder(db, anon);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.CvSkillConceptIds.ShouldBeEmpty();
        profile.Fast.SsykGroupConceptIds.ShouldBeEmpty();
        profile.Fast.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFullFromCvSkills_NoUser_ReturnsDegradedFastEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new MatchProfileBuilder(db, anon);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.CvSkillConceptIds.ShouldBeEmpty();
        profile.Fast.SsykGroupConceptIds.ShouldBeEmpty();
        profile.Fast.Title.ShouldBe(string.Empty);
    }

    // =================================================================
    // STEG 4 (ADR 0079 / #5a) — the title dimension reads the primary CV's denormalised
    // plaintext Resume.LatestRole (DEK-free) into Fast.Title, on BOTH Full builders.
    // EVIDENCE-ONLY: Title is absent from MatchGradeCalculator AND from the
    // MatchSortedJobAdSearchQuery ORDER BY — so this can move NEITHER a grade nor a sort
    // position (regression-pinned by the unchanged MatchGradeCalculatorTests + the SORT
    // oracle). These cases verify the projection, not any grade/sort effect.
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_PrimaryCvWithExperience_SetsFastTitleToLatestRole()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        // ...without disturbing the preference-derived Fast dimensions.
        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        profile.Fast.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        profile.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task BuildFullFromTopSkills_PrimaryCvWithoutExperience_SetsFastTitleToEmpty()
    {
        // A CV that carries NO experience → Resume.LatestRole is null (it is derived from
        // content.Experiences) → honest empty title (NotAssessed), never a null/placeholder.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithoutRoleAsync(db, seeker.Id);
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        profile.Fast.Title.ShouldBe(string.Empty);
    }
}
