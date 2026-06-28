using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// ADR 0079 STEG 3 PR-D (Beslut 1, the scorer reroute) — the WITH-primary-CV path of
/// <c>MatchProfileBuilder.BuildFullForVerdictAsync</c> against real Postgres
/// (Testcontainers) + the REAL field-encryption interceptor pair (ADR 0049/0074).
/// <para>
/// <b>What the reroute changed:</b> the builder is now DEK-FREE and resolver-FREE. The former
/// TAG path warmed the owner DEK, decrypted <c>MasterVersion.Content.Skills</c>, and resolved
/// them via <c>ISkillResolver</c>. That entire encrypted-skills path is GONE: the trusted
/// capability source is the user-CONFIRMED set
/// (<c>MatchPreferences.PreferredSkills</c>, plaintext concept-ids on the JobSeeker), so
/// <c>CvSkillConceptIds = PreferredSkills</c> with NO DEK warm and NO content read. The ONLY
/// CV read is the denormalised plaintext <see cref="Resume.LatestRole"/> column (ADR 0058/0059,
/// DEK-free, no <c>Include(Versions)</c>) for the Title dimension (STEG 4).
/// </para>
/// <para>
/// <b>Why this stays an integration test:</b> seeding the primary Resume still writes encrypted
/// <c>Content</c> through the real interceptor (which needs a warm owner DEK on the WRITE side);
/// a bare EF-InMemory context cannot materialize that. This suite proves, against the real DB,
/// that the builder returns the confirmed set as <c>CvSkillConceptIds</c> and the plaintext
/// <c>LatestRole</c> as <c>Fast.Title</c> WITHOUT the builder itself touching the DEK — i.e. the
/// confirmed-set reroute reads a resume's plaintext projection while leaving the encrypted
/// content untouched.
/// </para>
/// </summary>
[Collection("Api")]
public class MatchProfileBuilderFullCvIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The confirmed skill set is sorted+distinct ordinal by MatchPreferences.NormalizeList,
    // so seed and assert against an already-sorted set.
    private static readonly string[] ConfirmedSkills = ["skill_csharp", "skill_dotnet"];

    private static MatchPreferences Prefs(IEnumerable<string>? confirmedSkills) =>
        MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: null,
            preferredSkills: confirmedSkills).Value;

    // Builds the REAL MatchProfileBuilder from the scope (real db) with a substituted
    // ICurrentUser for the seeded user. The narrowed 2-arg ctor (ADR 0079 STEG 3 PR-D): the
    // builder is DEK-FREE and resolver-FREE, so no skill-resolver / DEK collaborators.
    private static MatchProfileBuilder NewBuilder(IServiceScope scope, Guid userId)
    {
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        // #300 PR-3: the ctor now also takes ITaxonomyReadModel (the related-occupation ACL).
        // Resolved from the real scope; these CV tests never broaden, so it is never called.
        var taxonomy = scope.ServiceProvider
            .GetRequiredService<Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new MatchProfileBuilder(db, currentUser, taxonomy);
    }

    // Seeds a JobSeeker (with prefs carrying the confirmed skill set) + a primary Resume whose
    // Master content carries an Experience with the given role → the denormalised plaintext
    // Resume.LatestRole projection (ADR 0058/0059) is populated. WarmAsync FÖRE Add (the
    // encrypted Content still needs a warm owner DEK to WRITE — the builder never reads it).
    private static async Task<Guid> SeedSeekerWithCvAsync(
        IServiceScope scope, string latestRole, IEnumerable<string>? confirmedSkills)
    {
        var db = scope.ServiceProvider.GetRequiredService<Jobbliggaren.Infrastructure.Persistence.AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        seeker.UpdateMatchPreferences(Prefs(confirmedSkills), clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        // Warm the owner DEK before writing the encrypted Resume content (the WRITE path needs
        // it; the builder under test does NOT — that is exactly what this suite proves).
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Test User", clock).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            experiences:
            [
                new Experience("Acme AB", latestRole, new DateOnly(2024, 1, 1), null, null),
            ],
            // Raw CV skills that DELIBERATELY differ from the confirmed set — the reroute must
            // ignore these and read only the confirmed PreferredSkills.
            skills: [new Skill("Java", null), new Skill("Python", null)]);
        resume.UpdateMasterContent(content, clock);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        seeker.SetPrimaryResume(resume.Id, clock);
        await db.SaveChangesAsync(ct);

        return userId;
    }

    // =================================================================
    // The reroute (ADR 0079 Beslut 1): CvSkillConceptIds = the CONFIRMED
    // MatchPreferences.PreferredSkills — proven against the real DB, DEK-free, ignoring the
    // (differing) raw CV skills.
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_WithPrimaryCv_ReturnsConfirmedSkills_NotRawCvSkills()
    {
        using var scope = _factory.Services.CreateScope();
        var userId = await SeedSeekerWithCvAsync(
            scope, "Backend-utvecklare", confirmedSkills: ConfirmedSkills);
        var builder = NewBuilder(scope, userId);

        var profile = await builder.BuildFullForVerdictAsync(TestContext.Current.CancellationToken);

        // Fast still carries the stored preferences.
        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        // The capability source is the CONFIRMED set, NOT the raw CV skills (Java/Python),
        // read DEK-free (no encrypted Content materialization) against the real DB.
        profile.CvSkillConceptIds.ShouldBe(
            ConfirmedSkills,
            "Den betrodda kapacitetskällan är den bekräftade PreferredSkills, inte CV:ets råa " +
            "skills (ADR 0079 Beslut 1) — läst DEK-fritt mot riktig Postgres.");
    }

    [Fact]
    public async Task BuildFullFromTopSkills_WithPrimaryCv_ReturnsConfirmedSkills_SameAsCvSkillsPath()
    {
        using var scope = _factory.Services.CreateScope();
        var userId = await SeedSeekerWithCvAsync(
            scope, "Backend-utvecklare", confirmedSkills: ConfirmedSkills);
        var builder = NewBuilder(scope, userId);

        var sort = await builder.BuildFullForSortAsync(TestContext.Current.CancellationToken);
        var tag = await builder.BuildFullForVerdictAsync(TestContext.Current.CancellationToken);

        // The SORT path and the TAG/modal path read the SAME confirmed source → equivalent
        // (the decisive sort==grade coherence guarantee, proven end-to-end on the real DB).
        sort.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        sort.CvSkillConceptIds.ShouldBe(tag.CvSkillConceptIds);
        sort.Fast.Title.ShouldBe(tag.Fast.Title);
    }

    [Fact]
    public async Task BuildFullFromCvSkills_WithEmptyConfirmedSkills_ReturnsEmptyConceptIds_NotError()
    {
        using var scope = _factory.Services.CreateScope();
        // No confirmed skills (even though the CV carries raw Java/Python) → empty set →
        // NotAssessed downstream, never an error and never the raw CV skills.
        var userId = await SeedSeekerWithCvAsync(
            scope, "Backend-utvecklare", confirmedSkills: null);
        var builder = NewBuilder(scope, userId);

        var profile = await builder.BuildFullForVerdictAsync(TestContext.Current.CancellationToken);

        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        profile.CvSkillConceptIds.ShouldBeEmpty(
            "Inga bekräftade kompetenser → tom skill-lista (NotAssessed), aldrig CV:ets råa skills.");
    }

    // =================================================================
    // STEG 4 (ADR 0079 / #5a) — the primary CV's denormalised plaintext Resume.LatestRole
    // flows into Fast.Title, read DEK-free against the real DB (evidence-only: Title is absent
    // from MatchGradeCalculator + the SORT ORDER BY, pinned by the unchanged
    // MatchGradeCalculatorTests + MatchSortOracleTests).
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_WithPrimaryCvHavingExperience_SetsFastTitleFromLatestRole()
    {
        using var scope = _factory.Services.CreateScope();
        var userId = await SeedSeekerWithCvAsync(
            scope, "Backend-utvecklare", confirmedSkills: ConfirmedSkills);
        var builder = NewBuilder(scope, userId);

        var profile = await builder.BuildFullForVerdictAsync(TestContext.Current.CancellationToken);

        // The plaintext LatestRole projection flows into the title dimension as evidence,
        // read off the Resume row without Include(Versions) / no DEK.
        profile.Fast.Title.ShouldBe("Backend-utvecklare");
        // ...alongside the confirmed skill set and the stored preferences.
        profile.CvSkillConceptIds.ShouldBe(ConfirmedSkills);
        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
    }
}
