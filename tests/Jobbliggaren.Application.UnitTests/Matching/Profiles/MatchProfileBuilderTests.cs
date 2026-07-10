using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Profiles;

// F4-12/F4-13 (senior-cto-advisor 2026-06-19 Decision B = B2) — the SSOT
// preference→CandidateMatchProfile mapper. These cases were relocated verbatim from
// BuildMatchProfileFromPreferencesQueryHandlerTests when the handler was thinned to a
// delegation: the mapping logic (DB-load + map + honest-empty fallback) now lives on
// MatchProfileBuilder, so the behavioural contract is pinned here (the handler keeps a
// thin delegation test of its own).
//
// INGEN CV-läsning, INGEN DEK (ej IRequiresFieldEncryptionKey), ingen PII.
// Mappning: SsykGroupConceptIds ← PreferredOccupationGroups;
// PreferredRegionConceptIds ← PreferredRegions; PreferredEmploymentTypeConceptIds
// ← PreferredEmploymentTypes; Title ← "" (tom sträng). "Ingen JobSeeker / inga prefs /
// unauthenticated" → HONEST TOM profil (Title "", tomma listor), INTE fel/null.
//
// CA2012: NSubstitute-stubbning/verifiering av ValueTask-returnerande port-medlemmar
// (ITaxonomyReadModel.GetRelatedOccupationGroupsAsync) är ett känt analyzer-false-positive —
// substitute-anropet KONSUMERAS aldrig, det interceptas av NSubstitute för Returns/Received.
// Suppression scoped till test-klassen (mock-setup), ej produktionskod. Speglar
// DeriveOccupationCodesQueryHandlerTests / TaxonomyQueryHandlersTests.
#pragma warning disable CA2012
public class MatchProfileBuilderTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // CA1861: hoist the repeated constant occupation arrays out of the Arg.Is matchers so the
    // same instance is reused in both the taxonomy stub setup and the Received(1) verification.
    private static readonly string[] ExactGroups = ["grp_A", "grp_B"];
    private static readonly string[] RelatedGroups = ["grp_R1", "grp_R2"];

    // #477 Low 1 — kommun→län-containment. PrefMunicipalities is the user's stated kommun set
    // (single element → normalization leaves it untouched, so the SequenceEqual matcher is
    // stable); ContainmentRegions is the derived parent-län set the taxonomy ACL returns for it.
    private static readonly string[] PrefMunicipalities = ["kommun_x"];
    private static readonly string[] ContainmentRegions = ["lan_x"];

    public MatchProfileBuilderTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // ADR 0084 §Implementation PR-3 (#300) widened the ctor to a 3rd dependency:
    // (db, currentUser, taxonomy). The taxonomy ACL is the SPOT that broadens exact→related
    // occupation groups. The default substitute returns [] for GetRelatedOccupationGroupsAsync,
    // so every existing case (none of which passes includeRelated:true) is behavior-identical —
    // the ACL is constructed but never observably broadens.
    //
    // ADR 0079 STEG 3 PR-D had narrowed the ctor to (db, currentUser): the builder is DEK-FREE
    // and resolver-FREE (the confirmed skill set is plaintext on MatchPreferences). The UNCHANGED
    // preference path (BuildFromPreferencesAsync reads no CV / no DEK) is pinned here; the
    // Full-path behaviour lives in MatchProfileBuilderFullTests.
    private MatchProfileBuilder NewBuilder(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        ICurrentUser? user = null,
        ITaxonomyReadModel? taxonomy = null) =>
        new(db, user ?? _currentUser, taxonomy ?? NewTaxonomy());

    // A taxonomy substitute whose GetRelatedOccupationGroupsAsync returns [] by default — so a
    // builder constructed with it behaves EXACTLY as before for any caller that does not pass
    // includeRelated:true. Cases that DO broaden override the return for a specific input set.
    private static ITaxonomyReadModel NewTaxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<string>>((IReadOnlyList<string>)[]));
        // #477 Low 1 — default the containment ACL to [] by default (parity the related stub
        // above), so a builder constructed with it behaves EXACTLY as pre-#477 for any caller that
        // does not preferer a kommun. Cases that DERIVE containment override the return for a
        // specific municipality set. (NSubstitute 5.x already auto-returns an empty list for this
        // ValueTask<IReadOnlyList<string>> member, so this is an EXPLICIT restatement of that
        // default, not a behaviour change — the existing cases stay green either way.)
        taxonomy
            .GetContainingRegionsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<string>>((IReadOnlyList<string>)[]));
        return taxonomy;
    }

    private static async Task<JobSeeker> SeedSeekerWithPrefsAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        MatchPreferences prefs)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        seeker.UpdateMatchPreferences(prefs, FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task BuildFromPreferences_WithStoredPreferences_MapsAllFieldsToProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345", "grp_67890"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: ["sthlm_kn"]).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_12345", "grp_67890"]);
        profile.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        profile.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
        // Spår 3 PR-A — municipality-dimensionen bärs igenom till profilen.
        profile.PreferredMunicipalityConceptIds.ShouldBe(["sthlm_kn"]);
    }

    // Spår 3 PR-A — fokuserat mappnings-fall: lagrade municipalities → profilens
    // PreferredMunicipalityConceptIds (parity med region/employment-type-mappningen).
    [Fact]
    public async Task BuildFromPreferences_WithStoredMunicipalities_MapsToProfileMunicipalityConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredMunicipalities: ["sthlm_kn", "gbg_kn"]).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.PreferredMunicipalityConceptIds.ShouldBe(["gbg_kn", "sthlm_kn"]); // sorterad ordinal
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_TitleIsAlwaysEmptyString_FromPreferenceSide()
    {
        // Preferens-vägen bär ingen titel → Title måste vara "" (inte null) så
        // F4-5-title-dimensionen rapporterar honest "ingen titel-signal".
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFromPreferences_WhenJobSeekerHasEmptyPreferences_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerWithPrefsAsync(db, _userId, MatchPreferences.Empty);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_WhenNoJobSeekerForUser_ReturnsHonestEmptyProfile()
    {
        // Ingen JobSeeker → honest TOM profil (inte fel/null) — F4-5-paritet:
        // tom SSYK-lista → NotAssessed, aldrig NoMatch.
        var db = TestAppDbContextFactory.Create();
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_WhenNotAuthenticated_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var builder = NewBuilder(db, currentUser);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_IsOwnerScoped_OnlyMapsCurrentUsersPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        // Annan användare med preferenser som INTE får läcka in i profilen.
        await SeedSeekerWithPrefsAsync(
            db, Guid.NewGuid(),
            MatchPreferences.Create(
                preferredOccupationGroups: ["grp_OTHER"],
                preferredRegions: null,
                preferredEmploymentTypes: null).Value);
        // Aktuell användare med sina egna preferenser.
        await SeedSeekerWithPrefsAsync(
            db, _userId,
            MatchPreferences.Create(
                preferredOccupationGroups: ["grp_MINE"],
                preferredRegions: null,
                preferredEmploymentTypes: null).Value);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_MINE"]);
        profile.SsykGroupConceptIds.ShouldNotContain("grp_OTHER");
    }

    // =================================================================
    // ADR 0084 §Implementation PR-3 (#300) — SPOT injection: when includeRelated is true the
    // Fast builder broadens the EXACT confirmed occupation set into the RELATED set via the
    // taxonomy ACL and carries the result in RelatedSsykGroupConceptIds (the PR-2 init-property).
    // When false (the default), the ACL is NOT consulted and Related stays []. This is dormant
    // capability in PR-3 — no production caller passes includeRelated:true (the FE toggle is PR-5),
    // so the gate-off cases double as the "existing behaviour unchanged" guard.
    // =================================================================

    [Fact]
    public async Task BuildFromPreferences_NarBreddningPa_FyllerRelaterade_FranTaxonomiACL_OchLamnarExaktOforandrat()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ExactGroups,
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);

        // The ACL returns the related set for the EXACT confirmed occupation set [A, B].
        var taxonomy = NewTaxonomy();
        taxonomy
            .GetRelatedOccupationGroupsAsync(
                Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(RelatedGroups));
        var builder = NewBuilder(db, taxonomy: taxonomy);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None, includeRelated: true);

        profile.ShouldNotBeNull();
        // Related is filled from the ACL.
        profile.RelatedSsykGroupConceptIds.ShouldBe(
            RelatedGroups,
            "includeRelated:true → RelatedSsykGroupConceptIds bärs från taxonomi-ACL:n.");
        // Exact (SsykGroupConceptIds) is UNCHANGED — broadening is additive, never mutates exact.
        profile.SsykGroupConceptIds.ShouldBe(
            ExactGroups,
            "Breddning är additiv: exakt-mängden förblir orörd.");
        // The ACL was consulted exactly once, with the EXACT confirmed occupation set.
        await taxonomy.Received(1).GetRelatedOccupationGroupsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(ExactGroups)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFromPreferences_NarBreddningAv_ArDefault_LamnarRelateradeTom_OchAnroparEjACL()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ExactGroups,
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);

        var taxonomy = NewTaxonomy();
        var builder = NewBuilder(db, taxonomy: taxonomy);

        // Default (includeRelated omitted == false) — the broadening gate is closed.
        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.RelatedSsykGroupConceptIds.ShouldBeEmpty(
            "Default (includeRelated:false) → ingen breddning → Related tom.");
        // Exact dimension is the normal mapped set; broadening did not touch it.
        profile.SsykGroupConceptIds.ShouldBe(ExactGroups);
        // The ACL is NOT consulted at all when the gate is off.
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFromPreferences_NarBreddningPa_MenIngaYrkesgrupper_GerTomRelaterad_UtanKrasch()
    {
        // Seeded prefs with NO occupation groups → the ACL is called with [] (honest: nothing to
        // broaden) → returns [] → Related empty, no crash. The builder still forwards the empty
        // exact set rather than short-circuiting, keeping the SPOT a single unconditional seam.
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);

        var taxonomy = NewTaxonomy(); // returns [] for any input, including []
        var builder = NewBuilder(db, taxonomy: taxonomy);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None, includeRelated: true);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.RelatedSsykGroupConceptIds.ShouldBeEmpty(
            "Tom exakt-mängd + breddning på → tom relaterad-mängd, aldrig krasch.");
        await taxonomy.Received(1).GetRelatedOccupationGroupsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildFromPreferences_NarBreddningPa_MenIngenJobSeeker_GerTomProfil_UtanACL()
    {
        // No JobSeeker / unauthenticated path is reached BEFORE any broadening: with no prefs
        // there is nothing to broaden, so the honest-empty fallback is unchanged and the ACL is
        // never consulted even with includeRelated:true.
        var db = TestAppDbContextFactory.Create();
        var taxonomy = NewTaxonomy();
        var builder = NewBuilder(db, taxonomy: taxonomy);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None, includeRelated: true);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.RelatedSsykGroupConceptIds.ShouldBeEmpty();
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // #477 Low 1 — kommun→län-containment. The builder derives
    // ContainmentRegionConceptIds from the taxonomy ACL (kommun→parent-län) and populates it
    // UNCONDITIONALLY — it is a CORRECTNESS fix, NOT the ?includeRelated-gated broadening. So it
    // fires even on the non-related default path (unlike RelatedSsykGroupConceptIds). An empty
    // municipality preference → the ACL returns [] → containment stays empty (pre-#477 byte-for-byte).
    // =================================================================

    [Fact]
    public async Task BuildFromPreferences_PopulatesContainmentRegions_FromTaxonomyACL_EvenWithBreddningOff()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ExactGroups,
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredMunicipalities: PrefMunicipalities).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);

        // The containment ACL returns the parent-län set for the user's stated kommun set.
        var taxonomy = NewTaxonomy();
        taxonomy
            .GetContainingRegionsAsync(
                Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(PrefMunicipalities)),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(ContainmentRegions));
        var builder = NewBuilder(db, taxonomy: taxonomy);

        // Default call — includeRelated is OFF. Containment must STILL be populated.
        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.ContainmentRegionConceptIds.ShouldBe(
            ContainmentRegions,
            "ContainmentRegionConceptIds fylls från taxonomi-ACL:n även med breddning AV — det är " +
            "en korrekthetsfix, ovillkorlig (till skillnad från ?includeRelated-grindade RelatedSsyk).");
        // The containment ACL fired exactly once, on the NON-related default path, with the stated
        // kommun set — the load-bearing "unconditional" proof.
        await taxonomy.Received(1).GetContainingRegionsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.SequenceEqual(PrefMunicipalities)),
            Arg.Any<CancellationToken>());
        // ... while the RELATED ACL did NOT fire (includeRelated:false) — the asymmetry that proves
        // containment is unconditional but broadening is gated.
        profile.RelatedSsykGroupConceptIds.ShouldBeEmpty();
        await taxonomy.DidNotReceive().GetRelatedOccupationGroupsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        // Containment is a DISTINCT derived set — the municipality dimension itself is unchanged
        // (deliberately NOT unioned into the region/municipality preference lists).
        profile.PreferredMunicipalityConceptIds.ShouldBe(PrefMunicipalities);
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_WhenNoMunicipalities_LeavesContainmentEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ExactGroups,
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: null,
            preferredMunicipalities: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);

        var taxonomy = NewTaxonomy(); // containment ACL returns [] for any input, incl. []
        var builder = NewBuilder(db, taxonomy: taxonomy);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.ContainmentRegionConceptIds.ShouldBeEmpty(
            "Ingen kommun-preferens → tom containment-mängd (pre-#477-beteende bit-för-bit).");
        // The ACL is STILL consulted unconditionally (with the empty municipality set) — a single
        // unconditional seam, never short-circuited on an empty preference.
        await taxonomy.Received(1).GetContainingRegionsAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.Count == 0),
            Arg.Any<CancellationToken>());
    }
}
#pragma warning restore CA2012
