using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackPageTests
{
    // The keys are a wire contract with the web's route map and the admin's statistics, so the set
    // is pinned exactly: adding, renaming or dropping a key must fail here first.
    private static readonly string[] ExpectedKeys =
    [
        "overview", "jobs", "job-ad", "matches", "saved-ads", "saved-searches",
        "applications", "application", "new-application", "statistics", "activity-report",
        "followed-companies", "company-search", "industry-watches", "application-history",
        "cv", "cv-import", "cv-review", "my-pages", "general",
    ];

    [Fact]
    public void List_IsExactlyThePinnedKeys()
        => FeedbackPage.List.Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(ExpectedKeys.Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("job-ad")]
    [InlineData("my-pages")]
    public void TryFromKey_KnownKey_ReturnsThePage(string key)
    {
        FeedbackPage.TryFromKey(key, out var page).ShouldBeTrue();
        page!.Name.ShouldBe(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Job-Ad")]
    [InlineData("/jobb/123")]
    [InlineData("jobs?q=backend")]
    [InlineData("settings")]
    public void TryFromKey_AnythingElse_IsRejected(string? key)
        => FeedbackPage.TryFromKey(key, out _).ShouldBeFalse();
}
