using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers;

// F4-12 (CTO-frozen) — read-side HasStatedDesiredOccupation: härledd bool =
// PreferredOccupationGroups icke-tom. Driver en setup-nudge i UI:t. Exponeras på
// "me"-profil-read-DTO:n.
//
// ANTAGANDE (att verifiera av Klas): den härledda boolen läggs på
// JobSeekerProfileDto (GetMyProfileQuery är den tydligaste "me"/översikts-
// readen — JobSeekerProfileDto.FromDomain mappar redan Preferences). Om Klas
// vill exponera den på en annan översiktsyta (t.ex. en framtida Översikt-query)
// flyttas detta test dit. Dokumenterat antagande per uppdragsinstruktionen.
//
// RÖD tills (a) MatchPreferences + JobSeeker.MatchPreferences finns och (b)
// JobSeekerProfileDto fått HasStatedDesiredOccupation-fältet som
// FromDomain härleder ur MatchPreferences.PreferredOccupationGroups.
public class GetMyProfileHasStatedDesiredOccupationTests
{
    private static async Task<JobSeekerProfileDto?> HandleForSeekerWithPrefsAsync(
        MatchPreferences? prefs)
    {
        var userId = Guid.NewGuid();
        var db = TestAppDbContextFactory.Create();

        var seeker = JobSeeker.Register(userId, "Klas Olsson", FakeDateTimeProvider.Default).Value;
        if (prefs is not null)
            seeker.UpdateMatchPreferences(prefs, FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new GetMyProfileQueryHandler(db, currentUser);
        return await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WhenOccupationGroupsStated_HasStatedDesiredOccupationIsTrue()
    {
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;

        var result = await HandleForSeekerWithPrefsAsync(prefs);

        result.ShouldNotBeNull();
        result.HasStatedDesiredOccupation.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenNoOccupationGroupsStated_HasStatedDesiredOccupationIsFalse()
    {
        // Tomma occupation-grupper (men ev. andra dims) → falskt (driver nudgen).
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"]).Value;

        var result = await HandleForSeekerWithPrefsAsync(prefs);

        result.ShouldNotBeNull();
        result.HasStatedDesiredOccupation.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenPreferencesNeverSet_HasStatedDesiredOccupationIsFalse()
    {
        // Default-MatchPreferences (aldrig satt) → tom → falskt.
        var result = await HandleForSeekerWithPrefsAsync(prefs: null);

        result.ShouldNotBeNull();
        result.HasStatedDesiredOccupation.ShouldBeFalse();
    }

    // F4-12 PR-B — read-side projektion av de tre stated-preference-listorna på
    // JobSeekerProfileDto. Krävs för att inställnings-kortet ska kunna förifylla
    // användarens nuvarande val (PUT är full-replace; utan nuläget skulle redigering
    // tyst nolla tidigare val). CTO-bunden: assertas i det BEFINTLIGA handler-testet
    // (ingen ny handler → inget nytt coverage-golv). Verifierar att concept-id-
    // projektionen är lika med det satta settet.
    [Fact]
    public async Task Handle_WhenAllPreferenceDimensionsStated_ProjectsAllThreeLists()
    {
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345", "grp_67890"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"]).Value;

        var result = await HandleForSeekerWithPrefsAsync(prefs);

        result.ShouldNotBeNull();
        result.PreferredOccupationGroups.ShouldBe(["grp_12345", "grp_67890"]);
        result.PreferredRegions.ShouldBe(["stockholm_AB"]);
        result.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task Handle_WhenPreferencesNeverSet_ProjectsEmptyLists()
    {
        var result = await HandleForSeekerWithPrefsAsync(prefs: null);

        result.ShouldNotBeNull();
        result.PreferredOccupationGroups.ShouldBeEmpty();
        result.PreferredRegions.ShouldBeEmpty();
        result.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenOnlySomeDimensionsStated_ProjectsOnlyThoseLists()
    {
        // Regioner + anställningsformer satta, yrkesgrupper tomma → respektive
        // lista projiceras oberoende (yrkesgrupper tom, övriga två fyllda).
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: ["stockholm_AB", "vastra_gotaland_VG"],
            preferredEmploymentTypes: ["et_fast"]).Value;

        var result = await HandleForSeekerWithPrefsAsync(prefs);

        result.ShouldNotBeNull();
        result.PreferredOccupationGroups.ShouldBeEmpty();
        result.PreferredRegions.ShouldBe(["stockholm_AB", "vastra_gotaland_VG"]);
        result.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
    }
}
