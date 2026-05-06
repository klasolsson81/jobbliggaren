using System.Text.RegularExpressions;
using JobbPilot.Application.Common.Abstractions;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Sessions;

public class SessionIdTests
{
    [Fact]
    public void Generate_ShouldProduceIdOf43Characters_WhenCalled()
    {
        var id = SessionId.Generate();
        id.Reveal().Length.ShouldBe(43);
    }

    [Fact]
    public void Generate_ShouldProduceUrlSafeCharactersOnly_WhenCalled()
    {
        var id = SessionId.Generate();
        Regex.IsMatch(id.Reveal(), @"^[A-Za-z0-9\-_]+$").ShouldBeTrue();
    }

    [Fact]
    public void Generate_ShouldNotContainPaddingCharacters_WhenCalled()
    {
        var id = SessionId.Generate();
        id.Reveal().ShouldNotContain("=");
    }

    [Fact]
    public void Generate_ShouldProduceZeroCollisions_When10000IdsAreGenerated()
    {
        var ids = new HashSet<string>(capacity: 10_000);
        for (var i = 0; i < 10_000; i++)
            ids.Add(SessionId.Generate().Reveal());

        ids.Count.ShouldBe(10_000);
    }

    [Fact]
    public void Generate_ShouldProduceNoPrefixCollisions_AcrossRandomSample()
    {
        var prefixes = Enumerable.Range(0, 100)
            .Select(_ => SessionId.Generate().Reveal()[..6])
            .ToList();

        var duplicatePrefixes = prefixes
            .GroupBy(p => p)
            .Count(g => g.Count() > 1);

        duplicatePrefixes.ShouldBe(0);
    }

    [Fact]
    public void ToString_ShouldReturnMaskedValue_WhenCalled()
    {
        var id = SessionId.Generate();
        var str = id.ToString();
        str.ShouldEndWith("…");
        str.Length.ShouldBe(7); // 6 chars + ellipsis
        str[..6].ShouldBe(id.Reveal()[..6]);
    }

    [Fact]
    public void FromRaw_ShouldRoundTrip_WhenRevealCalled()
    {
        var raw = SessionId.Generate().Reveal();
        var parsed = SessionId.FromRaw(raw);
        parsed.Reveal().ShouldBe(raw);
    }
}
