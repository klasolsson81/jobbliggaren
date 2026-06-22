using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Profiles;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the TWO new FULL-profile builders on
/// <see cref="IMatchProfileBuilder"/> (the existing <see cref="IMatchProfileBuilder.BuildFromPreferencesAsync"/>
/// stays unchanged and is pinned by <see cref="MatchProfileBuilderTests"/>):
/// <list type="bullet">
/// <item><b>BuildFullFromTopSkillsAsync (SORT path):</b> reads the plaintext denormalised
/// <see cref="Resume.TopSkills"/> (text[] projection, ADR 0059) — NO DEK. It MUST NOT call
/// <see cref="ICurrentDataOwner.SetOwner"/> nor
/// <see cref="IUserDataKeyStore.GetOrCreateDataKeyAsync"/> (the per-user match-sort hot path
/// must not pay a KMS round-trip per request).</item>
/// <item><b>BuildFullFromCvSkillsAsync (TAG path):</b> reads the full encrypted
/// <c>MasterVersion.Content.Skills</c> — so it MUST warm the DEK first:
/// <c>SetOwner(jobSeekerId)</c> then <c>GetOrCreateDataKeyAsync(jobSeekerId, ct)</c> BEFORE
/// the content read.</item>
/// </list>
/// Both resolve the free-text skill names via <see cref="ISkillResolver"/> and degrade to a
/// Fast-only profile (empty <see cref="FullCandidateMatchProfile.CvSkillConceptIds"/>) when
/// there is no user / no JobSeeker / no <see cref="JobSeeker.PrimaryResumeId"/>.
///
/// <para>
/// The widened ctor under test:
/// <c>MatchProfileBuilder(IAppDbContext db, ICurrentUser currentUser, ISkillResolver
/// skillResolver, ICurrentDataOwner currentDataOwner, IUserDataKeyStore dataKeyStore)</c>.
/// </para>
///
/// RED until F4-15 adds the two methods + widens the ctor (today the builder has only
/// BuildFromPreferencesAsync and the 2-arg ctor).
/// </summary>
public class MatchProfileBuilderFullTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ISkillResolver _skillResolver = Substitute.For<ISkillResolver>();
    private readonly ICurrentDataOwner _dataOwner = Substitute.For<ICurrentDataOwner>();
    private readonly IUserDataKeyStore _dataKeyStore = Substitute.For<IUserDataKeyStore>();
    private readonly Guid _userId = Guid.NewGuid();

    // Hoisted to satisfy CA1861 (constant array argument in a repeatedly-evaluated predicate).
    private static readonly string[] TopSkillsAB = ["a", "b"];

    public MatchProfileBuilderFullTests()
    {
        _currentUser.UserId.Returns(_userId);
        // A benign DEK by default — the fail-closed test overrides to throw.
        _dataKeyStore.GetOrCreateDataKeyAsync(Arg.Any<JobSeekerId>(), Arg.Any<CancellationToken>())
            .Returns(new byte[32]);
        // Default resolver: resolves nothing (overridden per test).
        _skillResolver.Resolve(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));
    }

    private MatchProfileBuilder NewBuilder(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _skillResolver, _dataOwner, _dataKeyStore);

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

    // A Resume whose Master content carries the given skill names → both the plaintext
    // TopSkills projection (first 5) AND MasterVersion.Content.Skills are populated.
    private static async Task<Resume> SeedResumeWithSkillsAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        JobSeekerId jobSeekerId,
        params string[] skillNames)
    {
        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Test User", FakeDateTimeProvider.Default).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            skills: skillNames.Select(n => new Skill(n, null)).ToList());
        resume.UpdateMasterContent(content, FakeDateTimeProvider.Default);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    // A Resume whose Master content carries an Experience with the given role → the
    // denormalised plaintext Resume.LatestRole projection (ADR 0058/0059) is populated.
    // LatestRole is derived from content.Experiences (most recent by StartDate), NOT from
    // skills — so the skills-only SeedResumeWithSkillsAsync leaves LatestRole null. When
    // skillNames is supplied, the Master also carries those skills (so the TopSkills path
    // still resolves alongside the title evidence).
    private static async Task<Resume> SeedResumeWithLatestRoleAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        JobSeekerId jobSeekerId,
        string latestRole,
        params string[] skillNames)
    {
        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Test User", FakeDateTimeProvider.Default).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            experiences:
            [
                new Experience("Acme AB", latestRole,
                    new DateOnly(2024, 1, 1), null, null),
            ],
            skills: skillNames.Select(n => new Skill(n, null)).ToList());
        resume.UpdateMasterContent(content, FakeDateTimeProvider.Default);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    private static MatchPreferences Prefs() => MatchPreferences.Create(
        preferredOccupationGroups: ["grp_12345"],
        preferredRegions: ["stockholm_AB"],
        preferredEmploymentTypes: ["et_fast"]).Value;

    private static void AssertFastFromPrefs(CandidateMatchProfile fast)
    {
        fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        fast.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
        fast.Title.ShouldBe(string.Empty);
    }

    // =================================================================
    // BuildFullFromTopSkillsAsync — SORT path (plaintext TopSkills, NO DEK)
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_NoUser_ReturnsDegradedFastEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new MatchProfileBuilder(db, anon, _skillResolver, _dataOwner, _dataKeyStore);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.CvSkillConceptIds.ShouldBeEmpty();
        profile.Fast.SsykGroupConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFullFromTopSkills_JobSeekerWithoutPrimaryResume_ReturnsFastFromPrefsEmptySkills()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        AssertFastFromPrefs(profile.Fast);
        profile.CvSkillConceptIds.ShouldBeEmpty(
            "Utan PrimaryResumeId finns inga CV-skills → degradera till Fast (tom skill-lista).");
    }

    [Fact]
    public async Task BuildFullFromTopSkills_WithPrimaryCv_ResolvesTopSkills_AndCarriesFastFromPrefs()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithSkillsAsync(db, seeker.Id, "a", "b");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        _skillResolver.Resolve(
                Arg.Is<IEnumerable<string>>(s => s.SequenceEqual(TopSkillsAB)),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["c1"], StringComparer.Ordinal));
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        profile.CvSkillConceptIds.ShouldBe(["c1"]);
        AssertFastFromPrefs(profile.Fast);
    }

    [Fact]
    public async Task BuildFullFromTopSkills_NeverWarmsTheDek_NoSetOwner_NoGetOrCreateDataKey()
    {
        // THE DEK-FREE CONTRACT (ADR 0076 Decision 6): the per-user match-SORT hot path
        // reads the plaintext TopSkills projection only — it must never pay a KMS round-trip.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithSkillsAsync(db, seeker.Id, "a", "b");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        _dataOwner.DidNotReceive().SetOwner(Arg.Any<JobSeekerId>());
        await _dataKeyStore.DidNotReceive().GetOrCreateDataKeyAsync(
            Arg.Any<JobSeekerId>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // STEG 4 (ADR 0079 / #5a) — the title dimension reads the primary CV's denormalised
    // plaintext Resume.LatestRole (DEK-free) into Fast.Title, on BOTH Full builders.
    // EVIDENCE-ONLY: Title is absent from MatchGradeCalculator AND from the
    // MatchSortedJobAdSearchQuery ORDER BY — so this can move NEITHER a grade nor a sort
    // position (regression-pinned by the unchanged MatchGradeCalculatorTests + the SORT
    // oracle). These cases verify the projection, not any grade/sort effect.
    // The TopSkills path is plaintext (no DEK) so LatestRole is honestly assertable here on
    // InMemory; the CvSkills DEK path's LatestRole projection is pinned in the
    // Testcontainers suite (MatchProfileBuilderFullCvIntegrationTests).
    // =================================================================

    [Fact]
    public async Task BuildFullFromTopSkills_WithPrimaryCvHavingExperience_SetsFastTitleToLatestRole()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithLatestRoleAsync(
            db, seeker.Id, "Backend-utvecklare", "a", "b");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        // The plaintext LatestRole projection flows into the title dimension as evidence.
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        // ...without disturbing the preference-derived Fast dimensions.
        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        profile.Fast.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        profile.Fast.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task BuildFullFromTopSkills_WithPrimaryCvWithoutExperience_SetsFastTitleToEmpty()
    {
        // A CV that carries skills but NO experience → Resume.LatestRole is null
        // (it is derived from content.Experiences, not skills) → honest empty title
        // (NotAssessed), never a null/placeholder.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithSkillsAsync(db, seeker.Id, "a", "b");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        profile.Fast.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFullFromTopSkills_JobSeekerWithoutPrimaryResume_SetsFastTitleToEmpty()
    {
        // No primary CV at all → no role to compare → honest empty title (parity with the
        // preference/no-CV path which keeps Title = "").
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromTopSkillsAsync(CancellationToken.None);

        profile.Fast.Title.ShouldBe(string.Empty);
    }

    // =================================================================
    // BuildFullFromCvSkillsAsync — TAG path (encrypted Content.Skills, DEK-warmed)
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_NoUser_ReturnsDegradedFastEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var builder = new MatchProfileBuilder(db, anon, _skillResolver, _dataOwner, _dataKeyStore);

        var profile = await builder.BuildFullFromCvSkillsAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.CvSkillConceptIds.ShouldBeEmpty();
        profile.Fast.SsykGroupConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFullFromCvSkills_JobSeekerWithoutPrimaryResume_ReturnsFastFromPrefsEmptySkills()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFullFromCvSkillsAsync(CancellationToken.None);

        AssertFastFromPrefs(profile.Fast);
        profile.CvSkillConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFullFromCvSkills_JobSeekerWithoutPrimaryResume_DoesNotWarmTheDek()
    {
        // No CV → no encrypted content to read → no DEK warm (the warm is bound to the
        // actual content read, not done eagerly).
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, Prefs(), primaryResumeId: null);
        var builder = NewBuilder(db);

        await builder.BuildFullFromCvSkillsAsync(CancellationToken.None);

        _dataOwner.DidNotReceive().SetOwner(Arg.Any<JobSeekerId>());
        await _dataKeyStore.DidNotReceive().GetOrCreateDataKeyAsync(
            Arg.Any<JobSeekerId>(), Arg.Any<CancellationToken>());
    }

    // NOTE (test-writer advisory): the WITH-primary-CV content-read assertions for
    // BuildFullFromCvSkillsAsync (the DEK-warm-THEN-resolve ordering + the resolved
    // Content.Skills set) CANNOT be honestly verified on the InMemory provider:
    // ResumeVersion.Content is `builder.Ignore`'d and owned by the field-encryption
    // interceptor pair (ADR 0049/0074 — same constraint GetResumeByIdQueryHandlerTests
    // documents: "InMemory förbjuden för Content"). A bare TestAppDbContextFactory has no
    // interceptor → Content never materializes → master.Content is null. Those two
    // assertions therefore live in the Testcontainers integration suite
    // (MatchProfileBuilderFullCvIntegrationTests), where the real interceptor decrypts
    // Content. Here we pin only what InMemory CAN honestly verify: degrade-to-Fast and the
    // fail-closed contract (the KMS throw fires BEFORE the content read).

    // =================================================================
    // FAIL-CLOSED (security-critical) — a KMS failure PROPAGATES; it must NOT
    // be swallowed into a dishonest empty/partial skill set.
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_WhenDataKeyStoreThrows_PropagatesException_DoesNotReturnEmptySkills()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, Prefs());
        var resume = await SeedResumeWithSkillsAsync(db, seeker.Id, "C#");
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        _dataKeyStore.GetOrCreateDataKeyAsync(Arg.Any<JobSeekerId>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("KMS unavailable"));
        var builder = NewBuilder(db);

        // A dishonest empty NotAssessed on a KMS failure is the exact thing forbidden —
        // the exception must surface, never be swallowed into a partial profile.
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await builder.BuildFullFromCvSkillsAsync(CancellationToken.None));

        // And the resolver was never reached with a half-decrypted/empty skill set.
        // Resolve is synchronous → DidNotReceive is NOT awaited.
        _skillResolver.DidNotReceive().Resolve(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }
}
