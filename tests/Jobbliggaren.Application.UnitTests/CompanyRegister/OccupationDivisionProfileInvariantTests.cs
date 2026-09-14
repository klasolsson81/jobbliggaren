using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #1682 — the four constructor guards on <see cref="OccupationDivisionProfile"/>, which make the
/// contradictory shapes UNREPRESENTABLE rather than merely unproduced (the <c>MaterialisedAdResultTests</c>
/// form, and for the same reason: production never constructs a contradictory value, so nothing but
/// a test that tries the lock can tell a guard from prose). Calling a guard with the input it exists
/// to reject is not a §5 premise problem — the actor IS the constructor, and what is asserted is its
/// own predicate. Each rejection sits beside the admitted shape: an unconditional guard would pass a
/// throw-only assertion.
/// </summary>
public class OccupationDivisionProfileInvariantTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 3, 35, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<OccupationDivisionShare> OneDivision = [new("62", 21)];

    [Fact]
    public void TheThreeFactories_AreTheAdmittedShapes()
    {
        var profiled = OccupationDivisionProfile.Profiled(21, OneDivision, 0, At);
        profiled.State.ShouldBe(OccupationDivisionProfileState.Profiled);
        profiled.TotalAds.ShouldBe(21);
        profiled.Divisions.ShouldBe(OneDivision);
        profiled.WithoutDivisionAdCount.ShouldBe(0, "zero is an answer under Profiled, never an absence");
        profiled.ProfiledAt.ShouldBe(At);

        var tooFew = OccupationDivisionProfile.TooFewAds(3, At);
        tooFew.State.ShouldBe(OccupationDivisionProfileState.TooFewAds);
        tooFew.TotalAds.ShouldBe(3);
        tooFew.Divisions.ShouldBeNull();
        tooFew.WithoutDivisionAdCount.ShouldBeNull();

        var unknown = OccupationDivisionProfile.NotProfiled;
        unknown.State.ShouldBe(OccupationDivisionProfileState.NotProfiled);
        unknown.TotalAds.ShouldBeNull();
        unknown.ProfiledAt.ShouldBeNull();
    }

    [Fact]
    public void TotalAds_ExistsExactlyWhenARunExists()
    {
        // A number under NotProfiled is a figure we have no run for; a run without a number is a
        // measurement thrown away. Both directions, because a one-sided guard admits the other.
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.NotProfiled, 5, null, null, null));
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.TooFewAds, null, null, null, At));
    }

    [Fact]
    public void Divisions_ExistExactlyUnderProfiled()
    {
        // A list under a refusal is a distribution we just said the base does not carry.
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.TooFewAds, 3, OneDivision, null, At));
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.Profiled, 21, null, 0, At));
    }

    [Fact]
    public void WithoutDivisionAdCount_FollowsTheDivisions()
    {
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.TooFewAds, 3, null, 1, At));
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.Profiled, 21, OneDivision, null, At));
    }

    [Fact]
    public void ProfiledAt_ExistsExactlyWhenARunExists()
    {
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.NotProfiled, null, null, null, At));
        Should.Throw<ArgumentException>(() =>
            new OccupationDivisionProfile(OccupationDivisionProfileState.Profiled, 21, OneDivision, 0, null));
    }
}
