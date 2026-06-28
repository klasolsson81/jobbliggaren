using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
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
//
// CA2012: NSubstitute-stubbning/verifiering av ValueTask-returnerande port-medlemmar
// (ITaxonomyReadModel.GetRelatedOccupationGroupsAsync) är ett känt analyzer-false-positive —
// substitute-anropet KONSUMERAS aldrig, det interceptas av NSubstitute för Returns/Received.
// Suppression scoped till test-klassen (mock-setup), ej produktionskod. Speglar
// DeriveOccupationCodesQueryHandlerTests / TaxonomyQueryHandlersTests.
#pragma warning disable CA2012
public class MatchProfileBuilderFullTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // The confirmed skill set is sorted+distinct ordinal by MatchPreferences.NormalizeList,
    // so seed and assert against an already-sorted set to keep the assertion deterministic.
    private static readonly string[] ConfirmedSkills = ["skill_csharp", "skill_dotnet"];

    // CA1861: hoist the repeated constant occupation arrays out of the Arg.Is matchers so the
    // same instance is reused in both the taxonomy stub setup and the Received(1) verification.
    // Prefs() confirms exactly ["grp_12345"] as the occupation set — that is the exact set the
    // broadening cases feed the ACL.
    private static readonly string[] ExactGroups = ["grp_12345"];
    private static readonly string[] RelatedGroups = ["grp_R1", "grp_R2"];

    public MatchProfileBuilderFullTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // ADR 0084 §Implementation PR-3 (#300) widened the ctor to (db, currentUser, taxonomy). The
    // default substitute returns [] for GetRelatedOccupationGroupsAsync, so every existing Full
    // case (none of which passes includeRelated:true) is behavior-identical. Cases that broaden
    // pass their own configured taxonomy.
    private MatchProfileBuilder NewBuilder(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        ITaxonomyReadModel? taxonomy = null) =>
        new(db, _currentUser, taxonomy ?? NewTaxonomy());

    // A taxonomy substitute whose GetRelatedOccupationGroupsAsync returns [] by default.
    private static ITaxonomyReadModel NewTaxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<string>>((IReadOnlyList<string>)[]));
        return taxonomy;
    }

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
        var builder = new MatchProfileBuilder(db, anon, NewTaxonomy());

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
        var builder = new MatchProfileBuilder(db, anon, NewTaxonomy());

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
    // PerUserJobAdSearchQuery ORDER BY — so this can move NEITHER a grade nor a sort
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

    // =================================================================
    // ADR 0080 Vag 4 PR-2 (Beslut 3) — BuildFullForUserIdAsync: the BACKGROUND / SYSTEM
    // variant that builds the FULL profile by an EXPLICIT user-id (no ICurrentUser), for the
    // Worker background-matching scan. The build is IDENTICAL to BuildFullForSortAsync (shared
    // BuildFullCoreAsync) — only the LOAD KEY differs (passed id vs ICurrentUser). These cases
    // pin: parity, no-ICurrentUser-dependency (the decisive property), honest-empty for an
    // unknown user, owner-scoping by the passed id (no cross-contamination), and the DEK-free
    // LatestRole-only CV read.
    // =================================================================

    [Fact]
    public async Task BuildFullForUserId_ForSeededUser_MatchesBuildFullForSortExactly()
    {
        // PARITY: by-id and the owner-scoped sort path build the SAME profile for the SAME
        // user — identical Fast lists, identical CvSkillConceptIds (= confirmed PreferredSkills),
        // identical LatestRole Title. Only the load key differs.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var byId = await builder.BuildFullForUserIdAsync(seeker.UserId, CancellationToken.None);
        var bySort = await builder.BuildFullForSortAsync(CancellationToken.None);

        byId.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        byId.CvSkillConceptIds.ShouldBe(bySort.CvSkillConceptIds);
        byId.Fast.Title.ShouldBe("Backend-utvecklare");
        byId.Fast.Title.ShouldBe(bySort.Fast.Title);
        byId.Fast.SsykGroupConceptIds.ShouldBe(bySort.Fast.SsykGroupConceptIds);
        byId.Fast.PreferredRegionConceptIds.ShouldBe(bySort.Fast.PreferredRegionConceptIds);
        byId.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(bySort.Fast.PreferredEmploymentTypeConceptIds);
        byId.Fast.PreferredMunicipalityConceptIds.ShouldBe(bySort.Fast.PreferredMunicipalityConceptIds);
    }

    [Fact]
    public async Task BuildFullForUserId_WithNoCurrentUser_StillBuildsTheRealProfile()
    {
        // THE KEY PROPERTY: the background path does NOT consult ICurrentUser. With a null
        // current user (the Worker has no request-scoped identity), the by-id build STILL
        // returns the real profile for the passed user. Contrast: BuildFullForSortAsync() with
        // a null current user degrades to EmptyFull (pinned by the NoUser cases above).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        // Build a builder whose ICurrentUser yields NO user — proving the by-id path ignores it.
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new MatchProfileBuilder(db, anon, NewTaxonomy());

        var byId = await builder.BuildFullForUserIdAsync(seeker.UserId, CancellationToken.None);
        var bySort = await builder.BuildFullForSortAsync(CancellationToken.None);

        // The by-id path produced the real profile despite the absent current user.
        byId.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        AssertFastFromPrefs(byId.Fast);
        byId.Fast.Title.ShouldBe("Backend-utvecklare");

        // ...while the owner-scoped sort path correctly degrades to EmptyFull (the contrast that
        // proves the two paths read different keys).
        bySort.CvSkillConceptIds.ShouldBeEmpty();
        bySort.Fast.SsykGroupConceptIds.ShouldBeEmpty();
        bySort.Fast.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFullForUserId_UnknownUser_ReturnsHonestEmptyProfile()
    {
        // No matching JobSeeker → the honest EMPTY profile (empty Fast SSYK, empty
        // CvSkillConceptIds → the Worker simply produces no matches for that user), never an
        // error. The current user IS authenticated here, proving the empty result comes from
        // the unknown passed id, not from the gate.
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs());
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForUserIdAsync(Guid.NewGuid(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.CvSkillConceptIds.ShouldBeEmpty();
        profile.Fast.SsykGroupConceptIds.ShouldBeEmpty();
        profile.Fast.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.Fast.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.Fast.PreferredMunicipalityConceptIds.ShouldBeEmpty();
        profile.Fast.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFullForUserId_TwoUsers_ScopesByPassedIdWithoutCrossContamination()
    {
        // OWNER-SCOPING BY THE PASSED ID: seed TWO JobSeekers with DIFFERENT preferences; the
        // by-id build returns the right user's profile for each id — no leakage between users
        // (the Worker scan iterates many users and must keep them isolated).
        var db = TestAppDbContextFactory.Create();

        var userA = Guid.NewGuid();
        var prefsA = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_aaaaa"],
            preferredRegions: ["region_a"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: null,
            preferredSkills: ["skill_a1", "skill_a2"]).Value;
        var seekerA = await SeedSeekerAsync(db, userA, prefsA);

        var userB = Guid.NewGuid();
        var prefsB = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_bbbbb"],
            preferredRegions: ["region_b"],
            preferredEmploymentTypes: ["et_visstid"],
            preferredMunicipalities: null,
            preferredSkills: ["skill_b1"]).Value;
        var seekerB = await SeedSeekerAsync(db, userB, prefsB);

        var builder = NewBuilder(db);

        var profileA = await builder.BuildFullForUserIdAsync(seekerA.UserId, CancellationToken.None);
        var profileB = await builder.BuildFullForUserIdAsync(seekerB.UserId, CancellationToken.None);

        // userA → only userA's preferences/skills.
        profileA.Fast.SsykGroupConceptIds.ShouldBe(["grp_aaaaa"]);
        profileA.Fast.PreferredRegionConceptIds.ShouldBe(["region_a"]);
        profileA.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
        profileA.CvSkillConceptIds.ShouldBe(["skill_a1", "skill_a2"]);

        // userB → only userB's preferences/skills (no bleed-through from userA).
        profileB.Fast.SsykGroupConceptIds.ShouldBe(["grp_bbbbb"]);
        profileB.Fast.PreferredRegionConceptIds.ShouldBe(["region_b"]);
        profileB.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_visstid"]);
        profileB.CvSkillConceptIds.ShouldBe(["skill_b1"]);
    }

    [Fact]
    public async Task BuildFullForUserId_PromotedCv_ReadsOnlyLatestRole_DekFree()
    {
        // DEK-FREE: for a user with a promoted CV, the by-id build reads ONLY the denormalised
        // plaintext LatestRole for the Title dimension — same DEK-free contract as the owner
        // path (no Include(Versions), no encrypted content materialised). The CvSkillConceptIds
        // come from the CONFIRMED MatchPreferences.PreferredSkills, never from the CV content,
        // and the Title equals the LatestRole projection.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullForUserIdAsync(seeker.UserId, CancellationToken.None);

        // Title is the plaintext LatestRole projection (DEK-free read).
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        // Skills are the confirmed preference set, NOT anything off the CV content.
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        AssertFastFromPrefs(profile.Fast);
    }

    // =================================================================
    // ADR 0084 §Implementation PR-3 (#300) — SPOT injection on the Full path. The broadening
    // lives on the embedded Fast profile (FullCandidateMatchProfile mirrors
    // RelatedSsykGroupConceptIds via Fast — no own field). When includeRelated:true the two
    // OWNER Full methods (sort + verdict) carry the related set from the taxonomy ACL into
    // Fast.RelatedSsykGroupConceptIds; the rest of the Full profile (CvSkillConceptIds, Title)
    // is unchanged. When false (default), the ACL is NOT consulted and Related stays [].
    //
    // The BACKGROUND by-id method (BuildFullForUserIdAsync) has NO includeRelated parameter and
    // STRUCTURALLY cannot broaden (product answer D: related is list-only, drives no
    // notifications) — pinned by the load-bearing guard below.
    // =================================================================

    [Fact]
    public async Task BuildFullForSort_NarBreddningPa_FyllerFastRelaterade_FranACL_OvrigtOforandrat()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        // Prefs() confirms exactly ["grp_12345"] as the occupation set — the ACL broadens that.
        var taxonomy = NewTaxonomy();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(RelatedGroups));
        var builder = NewBuilder(db, taxonomy);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None, includeRelated: true);

        // Related lands on the embedded Fast profile (the Full profile has no own related field).
        profile.Fast.RelatedSsykGroupConceptIds.ShouldBe(
            RelatedGroups,
            "Full-vägen bär relaterade via Fast.RelatedSsykGroupConceptIds.");
        // Exact + the rest of the Full profile is untouched by broadening.
        profile.Fast.SsykGroupConceptIds.ShouldBe(ExactGroups);
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        await taxonomy.Received(1).GetRelatedOccupationGroupsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFullForVerdict_NarBreddningPa_FyllerFastRelaterade_FranACL_OvrigtOforandrat()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var taxonomy = NewTaxonomy();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(RelatedGroups));
        var builder = NewBuilder(db, taxonomy);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None, includeRelated: true);

        profile.Fast.RelatedSsykGroupConceptIds.ShouldBe(RelatedGroups);
        profile.Fast.SsykGroupConceptIds.ShouldBe(ExactGroups);
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        await taxonomy.Received(1).GetRelatedOccupationGroupsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFullForSort_NarBreddningAv_ArDefault_LamnarRelateradeTom_OchAnroparEjACL()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs());
        var taxonomy = NewTaxonomy();
        var builder = NewBuilder(db, taxonomy);

        var profile = await builder.BuildFullForSortAsync(CancellationToken.None);

        profile.Fast.RelatedSsykGroupConceptIds.ShouldBeEmpty(
            "Default (includeRelated:false) → ingen breddning → Related tom.");
        AssertFastFromPrefs(profile.Fast);
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFullForVerdict_NarBreddningAv_ArDefault_LamnarRelateradeTom_OchAnroparEjACL()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs());
        var taxonomy = NewTaxonomy();
        var builder = NewBuilder(db, taxonomy);

        var profile = await builder.BuildFullForVerdictAsync(CancellationToken.None);

        profile.Fast.RelatedSsykGroupConceptIds.ShouldBeEmpty();
        AssertFastFromPrefs(profile.Fast);
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // The load-bearing question-D guard: the BACKGROUND path NEVER broadens. There is no
    // includeRelated parameter on BuildFullForUserIdAsync to flip — so even with a taxonomy
    // substitute primed to return a non-empty related set, the by-id build leaves
    // Fast.RelatedSsykGroupConceptIds empty AND never consults the ACL. This proves the
    // structural guarantee (product answer D: related is list-only, drives no notifications),
    // not merely a default-value behaviour.
    // =================================================================

    [Fact]
    public async Task BuildFullForUserId_NeverBroadens_EvenWhenTaxonomySubstituteReturnsNonEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(db, seeker.Id, "Backend-utvecklare");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        // The substitute is PRIMED to broaden — if the background path consulted it, Related
        // would be non-empty. It must stay empty because there is no param to enable broadening.
        var taxonomy = NewTaxonomy();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(RelatedGroups));
        var builder = NewBuilder(db, taxonomy);

        var profile = await builder.BuildFullForUserIdAsync(seeker.UserId, CancellationToken.None);

        // The real profile is built (exact + skills + title) ...
        profile.Fast.SsykGroupConceptIds.ShouldBe(ExactGroups);
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        // ... but Related is EMPTY: the background path structurally cannot broaden.
        profile.Fast.RelatedSsykGroupConceptIds.ShouldBeEmpty(
            "Bakgrundsvägen breddar ALDRIG (svar D: relaterade är list-only, driver inga notiser).");
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }
}
#pragma warning restore CA2012
