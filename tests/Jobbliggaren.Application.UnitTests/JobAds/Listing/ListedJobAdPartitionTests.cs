using Jobbliggaren.Application.JobAds.Listing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Listing;

/// <summary>
/// An ad's sitemap part follows the first hex digit of its id's canonical form (ADR 0174), so membership never moves
/// when other ads come and go.
/// </summary>
public class ListedJobAdPartitionTests
{
    private const string HexDigits = "0123456789abcdef";

    private static Guid IdStartingWith(char digit) => Guid.Parse($"{digit}fffffff-ffff-4fff-bfff-ffffffffffff");

    [Fact]
    public void PartOf_ReadsTheFirstCharacterOfTheCanonicalString()
    {
        foreach (var digit in HexDigits)
        {
            var id = IdStartingWith(digit);
            id.ToString("D")[0].ShouldBe(digit);
            ListedJobAdPartition.PartOf(id, 16).ShouldBe(HexDigits.IndexOf(digit) + 1, $"digit {digit}");
        }
    }

    [Fact]
    public void PartOf_TwoParts_PutsZeroToSevenInTheFirstAndEightToFInTheSecond()
    {
        foreach (var digit in "01234567")
            ListedJobAdPartition.PartOf(IdStartingWith(digit), 2).ShouldBe(1, $"digit {digit}");
        foreach (var digit in "89abcdef")
            ListedJobAdPartition.PartOf(IdStartingWith(digit), 2).ShouldBe(2, $"digit {digit}");
    }

    [Fact]
    public void PartOf_FourParts_SplitsTheDigitsIntoFourRuns()
    {
        string[] runs = ["0123", "4567", "89ab", "cdef"];
        for (var part = 1; part <= runs.Length; part++)
            foreach (var digit in runs[part - 1])
                ListedJobAdPartition.PartOf(IdStartingWith(digit), 4).ShouldBe(part, $"digit {digit}");
    }

    [Fact]
    public void PartOf_OnePart_HoldsEveryId()
    {
        foreach (var digit in HexDigits)
            ListedJobAdPartition.PartOf(IdStartingWith(digit), 1).ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void IsSupported_APartCountThatDividesTheDigitsEvenly_IsSupported(int parts) =>
        ListedJobAdPartition.IsSupported(parts).ShouldBeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(32)]
    public void PartOf_AnUnsupportedPartCount_Throws(int parts)
    {
        ListedJobAdPartition.IsSupported(parts).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => ListedJobAdPartition.PartOf(IdStartingWith('a'), parts));
    }
}
