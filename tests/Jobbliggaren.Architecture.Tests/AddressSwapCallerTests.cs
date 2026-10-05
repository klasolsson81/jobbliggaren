using System.Reflection;
using System.Runtime.CompilerServices;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Infrastructure.Auth;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1739 — who may move an account's address. <c>SwapConfirmedAddressAsync</c> is a MEMBER of
/// <c>IUserAccountService</c>, not a port of its own, so a constructor scan cannot see its callers. So the pin is a
/// source sweep: the files under <c>src/</c> that name the member are the port, the adapter and
/// <see cref="ConfirmedAddressSwap"/>, the one caller every completed address change goes through (#1975). Who may use
/// that caller is a constructor question, and the second fact answers it. Another name in either list is a new way to
/// move an address and is decided here.
/// </summary>
public sealed class AddressSwapCallerTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] Composing =
    [
        typeof(ConfirmedAddressSwap).Assembly,
        typeof(UserAccountService).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AuthEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
    ];

    [Fact]
    public void Only_the_port_the_adapter_and_the_shared_caller_name_the_address_swap_in_source()
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
            "Jobbliggaren.Application/Auth/ConfirmedAddressSwap.cs",
            "Jobbliggaren.Application/Common/Abstractions/IUserAccountService.cs",
            "Jobbliggaren.Infrastructure/Auth/UserAccountService.cs",
        ]);
    }

    [Fact]
    public void Only_the_handlers_that_complete_an_address_change_take_the_shared_caller()
    {
        var consumers = Composing
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(Declared).Cast<MethodBase>().Concat(type.GetMethods(Declared))
                .Any(member => member.GetParameters().Any(p => p.ParameterType == typeof(ConfirmedAddressSwap))))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe(
        [
            typeof(ConfirmEmailChangeCommandHandler).FullName!,
        ]);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/AddressSwapCallerTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
