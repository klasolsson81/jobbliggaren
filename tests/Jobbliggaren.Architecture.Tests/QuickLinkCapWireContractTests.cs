using System.Text.RegularExpressions;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0188 (#2265) — the web mirrors the cap of quick links to decide whether to offer "Lägg till som snabblänk"
/// before the API refuses with 409. The two numbers are pinned equal, so a cap changed on one side cannot leave the
/// other offering an add the API refuses, or hiding one it accepts. The 409's code is pinned the same way: the web tells
/// "the three are taken" from a lost race by it, and a renamed code would turn every full answer into "try again".
/// </summary>
public class QuickLinkCapWireContractTests
{
    private const string WebModuleRelativePath = "web/jobbliggaren-web/src/lib/job-ads/quick-links.ts";

    [Fact]
    public void TheWebCapEqualsTheDomainCap()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoSourceText.Root(), WebModuleRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var match = Regex.Match(source, @"export const QUICK_LINK_MAX\s*=\s*(\d+)\s*;");

        match.Success.ShouldBeTrue(
            $"QUICK_LINK_MAX was not found in {WebModuleRelativePath}. It was renamed or reshaped - "
            + "re-make this join deliberately, do not delete it.");
        int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(QuickLinkCap.MaxTotal);
    }

    [Fact]
    public void TheWebFullCodeEqualsTheDomainCode()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoSourceText.Root(), WebModuleRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var match = Regex.Match(source, @"export const QUICK_LINK_FULL_CODE\s*=\s*""([^""]+)""\s*;");

        match.Success.ShouldBeTrue(
            $"QUICK_LINK_FULL_CODE was not found in {WebModuleRelativePath}. It was renamed or reshaped - "
            + "re-make this join deliberately, do not delete it.");
        match.Groups[1].Value.ShouldBe(QuickLinkCap.MaxTotalCode);
    }
}
