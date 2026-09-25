using System.Runtime.CompilerServices;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1744, 6a PR S (security-auditor S4, condition 2) — PR S ships the Google adapter and the endpoints, and NO source
/// file under <c>src/</c> registers an identity provider in any environment. The provider list is therefore empty on
/// every host, whatever keys a configuration carries, until the PR that also carries the privacy copy, Chapter V and
/// lapse trigger 4 (6a PR G). PR G deletes this test with the registration it adds, and its gate tests take over.
/// </summary>
public sealed class NoExternalProviderRegisteredTests
{
    [Fact]
    public void No_source_file_registers_an_external_identity_provider()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        var registrations = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => RegistersAProvider(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(srcRoot, path))
            .ToList();

        registrations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("services.AddSingleton<IExternalIdentityProvider>(sp => new GoogleIdentityProvider(...));")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalIdentityProvider, GoogleIdentityProvider>());")]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IExternalIdentityProvider), typeof(GoogleIdentityProvider), lifetime));")]
    [InlineData("services.AddSingleton<" + "\n" + "        IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("using Provider = Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    public void The_scan_recognises_every_registration_form(string line) => RegistersAProvider(line).ShouldBeTrue();

    [Theory]
    [InlineData("public sealed class RegisteredProviders(IEnumerable<IExternalIdentityProvider> providers)")]
    [InlineData("private readonly IReadOnlyList<IExternalIdentityProvider> _providers = providers.ToList();")]
    [InlineData("// No IExternalIdentityProvider is registered here: a provider without keys is not registered at all.")]
    public void The_scan_does_not_mistake_a_consumer_or_a_comment_for_a_registration(string line) =>
        RegistersAProvider(line).ShouldBeFalse();

    private static bool RegistersAProvider(string text)
    {
        var compact = string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
        return compact.Contains("<IExternalIdentityProvider,", StringComparison.Ordinal)
               || compact.Contains("<IExternalIdentityProvider>(", StringComparison.Ordinal)
               || compact.Contains("typeof(IExternalIdentityProvider)", StringComparison.Ordinal)
               || compact.Contains("=Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;", StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
