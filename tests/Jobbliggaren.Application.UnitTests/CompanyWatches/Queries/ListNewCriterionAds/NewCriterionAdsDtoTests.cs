using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionIdentity;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.JobAds.Queries;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries.ListNewCriterionAds;

/// <summary>
/// ADR 0175 D7 (#1663 PR 2) — the constructor invariant of <see cref="NewCriterionAdsDto"/>: a closed state carries
/// nothing to show or acknowledge, and the two closed states exclude each other. The six rejected shapes are the ones
/// the web suites declare unreachable on the strength of this constructor.
/// </summary>
public class NewCriterionAdsDtoTests
{
    private static readonly DateTimeOffset Window = new(2026, 10, 1, 3, 55, 0, TimeSpan.Zero);

    private static readonly CriterionIdentityDto Criterion = new(Guid.NewGuid(), ["63910"], ["0180"], null);

    private static readonly NewCriterionAdRow Row = new(
        new JobAdDto(Guid.NewGuid(), "Roll", "Bolag AB", "https://example.com/jobs/1", "Platsbanken", "Active",
            Window, Window.AddDays(60), Window),
        MatchesYou: null);

    [Theory]
    [InlineData("rows beside TooBroad", false)]
    [InlineData("rows beside NotMaterialised", false)]
    [InlineData("a token beside TooBroad", false)]
    [InlineData("a token beside NotMaterialised", false)]
    [InlineData("truncation beside NotMaterialised", false)]
    [InlineData("both closed states at once", false)]
    [InlineData("Refused", true)]
    [InlineData("NotScanned", true)]
    [InlineData("Nothing", true)]
    public void Constructor_AdmitsAClosedStateOnlyWithNothingToShowOrAcknowledge(string shape, bool admitted)
    {
        Func<NewCriterionAdsDto> build = shape switch
        {
            "rows beside TooBroad" => () => Closed(tooBroad: true, rows: [Row]),
            "rows beside NotMaterialised" => () => Closed(notMaterialised: true, rows: [Row]),
            "a token beside TooBroad" => () => Closed(tooBroad: true, acknowledgedThrough: Window),
            "a token beside NotMaterialised" => () => Closed(notMaterialised: true, acknowledgedThrough: Window),
            "truncation beside NotMaterialised" => () => Closed(notMaterialised: true, truncated: true),
            "both closed states at once" => () => Closed(tooBroad: true, notMaterialised: true),
            "Refused" => () => NewCriterionAdsDto.Refused(Criterion),
            "NotScanned" => () => NewCriterionAdsDto.NotScanned(Criterion),
            "Nothing" => () => NewCriterionAdsDto.Nothing(Criterion),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        if (admitted)
            build().Rows.ShouldBeEmpty();
        else
            Should.Throw<ArgumentException>(build).ParamName.ShouldBe(nameof(NewCriterionAdsDto.Rows));
    }

    private static NewCriterionAdsDto Closed(
        bool tooBroad = false,
        bool notMaterialised = false,
        IReadOnlyList<NewCriterionAdRow>? rows = null,
        DateTimeOffset? acknowledgedThrough = null,
        bool truncated = false) =>
        new(Criterion, rows ?? [], MatchingAssessed: false, acknowledgedThrough, truncated, tooBroad, notMaterialised);
}
