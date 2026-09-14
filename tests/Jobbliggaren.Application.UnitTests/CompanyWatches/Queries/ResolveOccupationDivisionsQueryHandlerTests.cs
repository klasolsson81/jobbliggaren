using FluentValidation.TestHelper;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ResolveOccupationDivisions;
using Jobbliggaren.Application.JobAds.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1682 — the handler composes two ports and owns one number: the share percent. Both ports are
/// substituted; the deriver's candidates and the profile query's answers are the shapes those ports
/// produce in production (a candidate record, a profile in one of its three factory states).
/// </summary>
public class ResolveOccupationDivisionsQueryHandlerTests
{
    private static readonly DateTimeOffset ProfiledAt = new(2026, 9, 14, 3, 35, 0, TimeSpan.Zero);
    private static readonly string[] OnlySystemutvecklare = ["DJh5_yyF_hEM"];

    [Fact]
    public async Task Handle_WordWithOneProfiledGroup_ReturnsItsDivisionsWithSharesRoundedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var deriver = Substitute.For<IOccupationCodeDeriver>();
        deriver.DeriveAsync("systemutvecklare", ct).Returns(new OccupationDerivationResult(
            "systemutvecklare",
            [new OccupationCandidate("DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl.",
                OccupationMatchKind.StemmedTokenOverlap, "Systemutvecklare/Programmerare")]));
        var profiles = Substitute.For<IOccupationDivisionProfileQuery>();
        profiles.GetDivisionProfilesAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(OnlySystemutvecklare)), ct)
            .Returns(new Dictionary<string, OccupationDivisionProfile>
            {
                ["DJh5_yyF_hEM"] = OccupationDivisionProfile.Profiled(
                    totalAds: 2249,
                    divisions: [new OccupationDivisionShare("78", 615), new OccupationDivisionShare("62", 375)],
                    withoutDivisionAdCount: 395,
                    profiledAt: ProfiledAt),
            });

        var dto = await new ResolveOccupationDivisionsQueryHandler(deriver, profiles)
            .Handle(new ResolveOccupationDivisionsQuery(" systemutvecklare "), ct);

        dto.Word.ShouldBe("systemutvecklare");
        var one = dto.Occupations.ShouldHaveSingleItem();
        one.State.ShouldBe(OccupationDivisionCandidateDto.StateProfiled);
        one.Label.ShouldBe("Mjukvaru- och systemutvecklare m.fl.");
        one.MatchedOn.ShouldBe("Systemutvecklare/Programmerare");
        one.TotalAds.ShouldBe(2249);
        one.Divisions.ShouldBe([new DivisionShareDto("78", 615, 27), new DivisionShareDto("62", 375, 17)]);
        one.BelowThresholdAdCount.ShouldBe(864, "2249 - 615 - 375 - 395: every profiled ad is in exactly one of the three places");
        one.BelowThresholdSharePercent.ShouldBe(38, "864/2249 = 38.42 %");
        one.WithoutDivisionAdCount.ShouldBe(395);
        one.WithoutDivisionSharePercent.ShouldBe(18, "395/2249 = 17.56 %, rounded half away from zero");
        one.ProfiledAt.ShouldBe(ProfiledAt);
    }

    [Fact]
    public async Task Handle_WordWithSeveralGroups_KeepsTheDeriversOrder_AndEachGroupsOwnState()
    {
        var ct = TestContext.Current.CancellationToken;
        var deriver = Substitute.For<IOccupationCodeDeriver>();
        deriver.DeriveAsync("sjuksköterska", ct).Returns(new OccupationDerivationResult(
            "sjuksköterska",
            [
                new OccupationCandidate("Z8ci_bBE_tmx", "Grundutbildade sjuksköterskor",
                    OccupationMatchKind.StemmedTokenOverlap, "Sjuksköterska, grundutbildad"),
                new OccupationCandidate("6zAR_EHM_Kwj", "Övriga specialistsjuksköterskor",
                    OccupationMatchKind.StemmedTokenOverlap, "Medicinskt ansvarig sjuksköterska"),
            ]));
        var profiles = Substitute.For<IOccupationDivisionProfileQuery>();
        profiles.GetDivisionProfilesAsync(Arg.Any<IReadOnlyList<string>>(), ct)
            .Returns(new Dictionary<string, OccupationDivisionProfile>
            {
                ["Z8ci_bBE_tmx"] = OccupationDivisionProfile.Profiled(
                    3317, [new OccupationDivisionShare("86", 1626)], 10, ProfiledAt),
                ["6zAR_EHM_Kwj"] = OccupationDivisionProfile.TooFewAds(12, ProfiledAt),
            });

        var dto = await new ResolveOccupationDivisionsQueryHandler(deriver, profiles)
            .Handle(new ResolveOccupationDivisionsQuery("sjuksköterska"), ct);

        dto.Occupations.Select(o => o.OccupationGroupConceptId).ShouldBe(["Z8ci_bBE_tmx", "6zAR_EHM_Kwj"]);
        dto.Occupations[0].State.ShouldBe(OccupationDivisionCandidateDto.StateProfiled);
        dto.Occupations[1].State.ShouldBe(OccupationDivisionCandidateDto.StateTooFewAds);
        dto.Occupations[1].TotalAds.ShouldBe(12);
        dto.Occupations[1].Divisions.ShouldBeNull();
        dto.Occupations[1].WithoutDivisionSharePercent.ShouldBeNull();
        dto.Occupations[1].BelowThresholdAdCount.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NotProfiled_CarriesNoNumbers_NeverAZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var deriver = Substitute.For<IOccupationCodeDeriver>();
        deriver.DeriveAsync("revisor", ct).Returns(new OccupationDerivationResult(
            "revisor",
            [new OccupationCandidate("rev", "Revisorer m.fl.", OccupationMatchKind.ExactOccupationName, "Revisor")]));
        var profiles = Substitute.For<IOccupationDivisionProfileQuery>();
        profiles.GetDivisionProfilesAsync(Arg.Any<IReadOnlyList<string>>(), ct)
            .Returns(new Dictionary<string, OccupationDivisionProfile>
            {
                ["rev"] = OccupationDivisionProfile.NotProfiled,
            });

        var dto = await new ResolveOccupationDivisionsQueryHandler(deriver, profiles)
            .Handle(new ResolveOccupationDivisionsQuery("revisor"), ct);

        var one = dto.Occupations.ShouldHaveSingleItem();
        one.State.ShouldBe(OccupationDivisionCandidateDto.StateNotProfiled);
        one.TotalAds.ShouldBeNull();
        one.Divisions.ShouldBeNull();
        one.WithoutDivisionAdCount.ShouldBeNull();
        one.BelowThresholdAdCount.ShouldBeNull();
        one.ProfiledAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_SharePercent_RoundsAMidpointAwayFromZero_NeverToEven()
    {
        // 1/8 = 12.5 % is a true midpoint: half-away-from-zero says 13, banker's rounding says 12.
        // Every other fixture in this class sits off the midpoint, so this is the only case that
        // can tell the delivered MidpointRounding from the default.
        var ct = TestContext.Current.CancellationToken;
        var deriver = Substitute.For<IOccupationCodeDeriver>();
        deriver.DeriveAsync("kock", ct).Returns(new OccupationDerivationResult(
            "kock",
            [new OccupationCandidate("kock", "Kockar och kallskänkor", OccupationMatchKind.ExactOccupationName, "Kock")]));
        var profiles = Substitute.For<IOccupationDivisionProfileQuery>();
        profiles.GetDivisionProfilesAsync(Arg.Any<IReadOnlyList<string>>(), ct)
            .Returns(new Dictionary<string, OccupationDivisionProfile>
            {
                ["kock"] = OccupationDivisionProfile.Profiled(
                    8, [new OccupationDivisionShare("56", 6), new OccupationDivisionShare("78", 1)], 1, ProfiledAt),
            });

        var dto = await new ResolveOccupationDivisionsQueryHandler(deriver, profiles)
            .Handle(new ResolveOccupationDivisionsQuery("kock"), ct);

        var one = dto.Occupations.ShouldHaveSingleItem();
        one.Divisions.ShouldBe([new DivisionShareDto("56", 6, 75), new DivisionShareDto("78", 1, 13)]);
        one.WithoutDivisionSharePercent.ShouldBe(13);
        one.BelowThresholdAdCount.ShouldBe(0);
        one.BelowThresholdSharePercent.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WordThatIsNoOccupation_ReturnsAnEmptyList_AndNeverAsksTheProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var deriver = Substitute.For<IOccupationCodeDeriver>();
        deriver.DeriveAsync("chef", ct).Returns(new OccupationDerivationResult("chef", []));
        var profiles = Substitute.For<IOccupationDivisionProfileQuery>();

        var dto = await new ResolveOccupationDivisionsQueryHandler(deriver, profiles)
            .Handle(new ResolveOccupationDivisionsQuery("chef"), ct);

        dto.Occupations.ShouldBeEmpty();
        await profiles.DidNotReceive().GetDivisionProfilesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public void Validator_RejectsAWordUnderTheFloor(string word)
    {
        new ResolveOccupationDivisionsQueryValidator()
            .TestValidate(new ResolveOccupationDivisionsQuery(word))
            .ShouldHaveValidationErrorFor(q => q.Word);
    }

    [Fact]
    public void Validator_RejectsAWordOverTheCeiling_AndAcceptsOneInside()
    {
        var validator = new ResolveOccupationDivisionsQueryValidator();
        validator.TestValidate(new ResolveOccupationDivisionsQuery(new string('a', 101)))
            .ShouldHaveValidationErrorFor(q => q.Word);
        validator.TestValidate(new ResolveOccupationDivisionsQuery("sjuksköterska"))
            .ShouldNotHaveAnyValidationErrors();
    }
}
