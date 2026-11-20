using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0188 (#2265, security-auditor m3) — the handler behind the /jobb hero's junior count logs nothing: no
/// <c>ILogger</c>, no <c>Log*(</c> call, no <c>[LoggerMessage]</c> partial and no <c>_logger</c> member. Fail-closed, with
/// the scan <c>OrganizationNumberSurfacingGuardTests</c> holds the browse read-path to: an allow-list of nothing, so any
/// logging surface added to the file fails the build whatever it would carry.
/// </summary>
public sealed class JuniorItJobAdCountLoggingSurfaceTests
{
    private const string HandlerPath =
        "src/Jobbliggaren.Application/JobAds/Queries/GetJuniorItJobAdCount/GetJuniorItJobAdCountQueryHandler.cs";

    [Fact]
    public void GetJuniorItJobAdCountQueryHandler_ShouldHaveNoLoggingSurface()
    {
        var source = File.ReadAllText(SourceAbsolutePath(HandlerPath));

        OrgNrSurfaceScan.FindLoggingSurface(source, allowed: []).ShouldBeEmpty(
            $"{HandlerPath} has gained a logging surface. The count handler logs nothing (security-auditor m3, ADR 0188).");
    }

    private static string SourceAbsolutePath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("the repository root (CLAUDE.md) was not found above the test binaries");
        var absolute = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(absolute).ShouldBeTrue($"the scanned source is missing: {relativePath}; a moved file makes the guard vacuous");
        return absolute;
    }
}
