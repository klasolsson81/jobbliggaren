using Jobbliggaren.Infrastructure.JobAds;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 — the LIKE pattern the substring arms use: its metacharacters escaped, and the trigram gate
/// (security-auditor round 1) — a "contains" pattern without a run of three letters or digits cannot
/// be served by pg_trgm and scans the whole index.
/// </summary>
public sealed class LikePatternsTests
{
    [Theory]
    [InlineData("deltid", true)]
    [InlineData("e-handel", true)]
    [InlineData("100%", true)]
    [InlineData("göt", true)]
    [InlineData("åäö", true)]
    [InlineData("c++", false)]
    [InlineData("!!!", false)]
    [InlineData("ab", false)]
    [InlineData("a-b-c", false)]
    public void HasIndexableTrigram_NeedsARunOfThreeLettersOrDigits(string term, bool expected)
    {
        LikePatterns.HasIndexableTrigram(term).ShouldBe(expected);
    }

    [Theory]
    [InlineData("100%", @"%100\%%")]
    [InlineData("c_sharp", @"%c\_sharp%")]
    [InlineData(@"a\b", @"%a\\b%")]
    [InlineData(@"\%", @"%\\\%%")]
    [InlineData("Göteborg", "%göteborg%")]
    [InlineData("A_b%c\\", "%a\\_b\\%c\\\\%")]
    public void Contains_EscapesEveryLikeMetacharacter_AndLowerCases(string term, string expected)
    {
        LikePatterns.Contains(term).ShouldBe(expected);
    }
}
