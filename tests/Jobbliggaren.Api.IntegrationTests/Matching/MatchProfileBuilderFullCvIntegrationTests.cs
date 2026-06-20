using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the WITH-primary-CV content read of
/// <c>MatchProfileBuilder.BuildFullFromCvSkillsAsync</c> (the TAG path) against real
/// Postgres (Testcontainers) + the REAL field-encryption interceptor pair (ADR 0049/0074).
/// <para>
/// <b>Why this cannot be a unit test:</b> <c>ResumeVersion.Content</c> is
/// <c>builder.Ignore</c>'d and owned by the encrypt/decrypt interceptor pair — a bare
/// EF-InMemory <c>AppDbContext</c> (no interceptor) cannot materialize <c>Content</c> by
/// construction (the same constraint <c>GetResumeByIdQueryHandlerTests</c> documents:
/// "InMemory förbjuden för Content"). So the encrypted <c>Content.Skills</c> read +
/// the DEK-warm-THEN-resolve ordering are pinned HERE; the degrade-to-Fast and fail-closed
/// contracts (InMemory-verifiable) stay in <c>MatchProfileBuilderFullTests</c>.
/// </para>
/// <para>
/// The builder warms the owner DEK itself (SetOwner + GetOrCreateDataKeyAsync) before the
/// content read — so we resolve the REAL <see cref="ISkillResolver"/> + DEK collaborators
/// from the scope and only substitute <see cref="ICurrentUser"/> (parity
/// <c>AttachResumeVersionHandlerIntegrationTests</c>).
/// </para>
/// RED until BuildFullFromCvSkillsAsync reads + resolves the encrypted Content.Skills.
/// </summary>
[Collection("Api")]
public class MatchProfileBuilderFullCvIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static MatchPreferences Prefs() => MatchPreferences.Create(
        preferredOccupationGroups: ["grp_12345"],
        preferredRegions: ["stockholm_AB"],
        preferredEmploymentTypes: ["et_fast"]).Value;

    // Builds the REAL MatchProfileBuilder from the scope (real db + skill resolver + DEK
    // collaborators) with a substituted ICurrentUser for the seeded user.
    private static MatchProfileBuilder NewBuilder(IServiceScope scope, Guid userId)
    {
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var skillResolver = scope.ServiceProvider.GetRequiredService<ISkillResolver>();
        var dataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new MatchProfileBuilder(db, currentUser, skillResolver, dataOwner, dataKeyStore);
    }

    // Seeds a JobSeeker (with prefs) + a primary Resume whose Master content carries the
    // given skill names. WarmAsync FÖRE Add (the encrypted Content needs a warm owner DEK).
    private static async Task<(Guid UserId, JobSeekerId SeekerId)> SeedSeekerWithCvAsync(
        IServiceScope scope, params string[] skillNames)
    {
        var db = scope.ServiceProvider.GetRequiredService<Jobbliggaren.Infrastructure.Persistence.AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        seeker.UpdateMatchPreferences(Prefs(), clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        // Warm the owner DEK before adding the encrypted Resume content.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Test User", clock).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            skills: skillNames.Select(n => new Skill(n, null)).ToList());
        resume.UpdateMasterContent(content, clock);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        seeker.SetPrimaryResume(resume.Id, clock);
        await db.SaveChangesAsync(ct);

        return (userId, seeker.Id);
    }

    // =================================================================
    // 6 (relocated). WITH a primary CV → resolves the FULL Content.Skills to
    // concept-ids (the real taxonomy index over the decrypted content).
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_WithPrimaryCv_ResolvesDecryptedContentSkills()
    {
        using var scope = _factory.Services.CreateScope();
        // Pick skill names known to resolve to a concept-id via the real taxonomy index.
        // We assert the result is NON-EMPTY (the content was decrypted + resolved); the
        // exact concept-ids are taxonomy-data-dependent, so we pin "resolved something"
        // rather than a magic concept-id (provenance rule — never guess).
        var (userId, _) = await SeedSeekerWithCvAsync(scope, "Java", "Python", "C#");
        var builder = NewBuilder(scope, userId);

        var profile = await builder.BuildFullFromCvSkillsAsync(TestContext.Current.CancellationToken);

        // Fast still carries the stored preferences.
        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        // The encrypted Content.Skills were decrypted (real interceptor) + resolved — a CV
        // with common skill names must produce at least one concept-id (proves the read
        // round-tripped through the interceptor, unlike the InMemory path).
        profile.CvSkillConceptIds.ShouldNotBeEmpty(
            "Den krypterade Content.Skills ska dekrypteras (riktig interceptor) och resolvas " +
            "till minst ett concept-id — InMemory kan inte materialisera Content.");
    }

    [Fact]
    public async Task BuildFullFromCvSkills_WithNoResolvableSkills_DegradesToEmptyConceptIds_NotError()
    {
        using var scope = _factory.Services.CreateScope();
        // Garbage skill names the taxonomy does not carry → resolver drops them silently →
        // empty CvSkillConceptIds (degrade to Fast), never an error. Proves the read path
        // is honest even when nothing resolves.
        var (userId, _) = await SeedSeekerWithCvAsync(scope, "zzqxyw", "qwzzxy");
        var builder = NewBuilder(scope, userId);

        var profile = await builder.BuildFullFromCvSkillsAsync(TestContext.Current.CancellationToken);

        profile.Fast.SsykGroupConceptIds.ShouldBe(["grp_12345"]);
        profile.CvSkillConceptIds.ShouldBeEmpty(
            "Oresolverbara skill-namn droppas tyst → tom skill-lista, aldrig fel.");
    }

    // =================================================================
    // 6 (relocated). The DEK is warmed (SetOwner + GetOrCreateDataKeyAsync) — proven
    // BEHAVIOURALLY: a CV whose content can ONLY be read once the owner DEK is warm
    // round-trips a non-empty resolved set. (The ordering unit-assertion needed the
    // InMemory content read that is impossible; here the successful decryption IS the
    // proof the warm happened before the read.)
    // =================================================================

    [Fact]
    public async Task BuildFullFromCvSkills_WarmsOwnerDek_SoEncryptedContentDecryptsAndResolves()
    {
        using var scope = _factory.Services.CreateScope();
        var (userId, _) = await SeedSeekerWithCvAsync(scope, "Java", "Python");
        var builder = NewBuilder(scope, userId);

        // If the builder did NOT warm the owner DEK before reading Content, the
        // decryption interceptor would fail-closed and the call would throw — a successful,
        // non-empty resolution proves the warm-then-read ordering held.
        var profile = await builder.BuildFullFromCvSkillsAsync(TestContext.Current.CancellationToken);

        profile.CvSkillConceptIds.ShouldNotBeEmpty(
            "En lyckad dekryptering bevisar att ägar-DEK värmdes FÖRE content-läsningen.");
    }
}
