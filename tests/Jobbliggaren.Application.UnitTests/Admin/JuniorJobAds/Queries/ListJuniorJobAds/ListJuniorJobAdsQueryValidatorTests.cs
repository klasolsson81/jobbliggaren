using Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.JuniorJobAds.Queries.ListJuniorJobAds;

/// <summary>
/// #2264 (ADR 0187) — the admin junior queue's filters: one of the three tabs, and a page from 1 to
/// <see cref="ListJuniorJobAdsQuery.MaxPage"/>.
/// </summary>
public class ListJuniorJobAdsQueryValidatorTests
{
    private readonly ListJuniorJobAdsQueryValidator _validator = new();

    [Theory]
    [InlineData(JuniorQueueView.Unclear)]
    [InlineData(JuniorQueueView.Shown)]
    [InlineData(JuniorQueueView.Decided)]
    public void Validate_ShouldPass_ForEveryTab(JuniorQueueView view)
    {
        var result = _validator.Validate(new ListJuniorJobAdsQuery(view, 1));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldPass_ForTheDefaults()
    {
        _validator.Validate(new ListJuniorJobAdsQuery()).IsValid.ShouldBeTrue();
    }

    // A number the endpoint's enum binder accepts ("?view=7" parses), so the validator is the gate.
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Validate_ShouldFailOnTheView_WhenItIsNoTab(int value)
    {
        var result = _validator.Validate(new ListJuniorJobAdsQuery((JuniorQueueView)value, 1));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ListJuniorJobAdsQuery.View));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(ListJuniorJobAdsQuery.MaxPage)]
    public void Validate_ShouldPass_WhenThePageIsAtABound(int page)
    {
        _validator.Validate(new ListJuniorJobAdsQuery(JuniorQueueView.Unclear, page)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ListJuniorJobAdsQuery.MaxPage + 1)]
    public void Validate_ShouldFailOnThePage_WhenItIsOutsideTheBounds(int page)
    {
        var result = _validator.Validate(new ListJuniorJobAdsQuery(JuniorQueueView.Unclear, page));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ListJuniorJobAdsQuery.Page));
    }

    [Fact]
    public void MaxPage_ShouldBeOneThousand()
    {
        ListJuniorJobAdsQuery.MaxPage.ShouldBe(1000);
    }
}
