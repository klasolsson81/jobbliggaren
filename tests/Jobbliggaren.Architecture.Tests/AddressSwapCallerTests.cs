using System.Runtime.CompilerServices;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1739 — who may move an account's address. <c>SwapConfirmedAddressAsync</c> is a MEMBER of
/// <c>IUserAccountService</c>, not a port of its own, so a constructor scan cannot see its callers, and the command
/// that calls it stands outside the re-authentication marker by design (its proof is the change-email grant). So
/// the pin is a source sweep: the files under <c>src/</c> that name the member are the port, the adapter and the
/// confirm handler, and a fourth is a new way to move an address and is decided here.
/// </summary>
public sealed class AddressSwapCallerTests
{
    [Fact]
    public void Only_the_port_the_adapter_and_the_confirm_handler_name_the_address_swap_in_source()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        var namers = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("SwapConfirmedAddressAsync(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(srcRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        namers.ShouldBe(
        [
            "Jobbliggaren.Application/Auth/Commands/ConfirmEmailChange/ConfirmEmailChangeCommandHandler.cs",
            "Jobbliggaren.Application/Common/Abstractions/IUserAccountService.cs",
            "Jobbliggaren.Infrastructure/Auth/UserAccountService.cs",
        ]);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/AddressSwapCallerTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
