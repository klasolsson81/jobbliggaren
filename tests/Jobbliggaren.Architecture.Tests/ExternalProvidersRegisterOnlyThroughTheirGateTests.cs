using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1744, 6a PR G (senior-cto-advisor F11c) — an identity provider exists on a host only through its gate, which
/// registers nothing without a client id. So exactly one file under <c>src/</c> registers a provider, and no file hands
/// one to <c>RegisteredProviders</c> directly or builds the adapter by hand (security-auditor Minor 4 on #1861). Each
/// file is read with its whitespace removed, so a registration broken over lines is still one text.
/// </summary>
public sealed class ExternalProvidersRegisterOnlyThroughTheirGateTests
{
    [Fact]
    public void Only_the_google_gate_registers_an_external_identity_provider()
    {
        SourceFilesWhere(RegistersAProvider).ShouldBe(
            ["Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProviderRegistration.cs"]);
    }

    [Fact]
    public void No_source_file_builds_the_provider_list_or_the_adapter_by_hand() =>
        SourceFilesWhere(BuildsByHand).ShouldBeEmpty();

    [Theory]
    [InlineData("services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("services.AddSingleton<IExternalIdentityProvider>(sp => new GoogleIdentityProvider(...));")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalIdentityProvider, GoogleIdentityProvider>());")]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IExternalIdentityProvider), typeof(GoogleIdentityProvider), lifetime));")]
    [InlineData("services.AddSingleton<" + "\n" + "        IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("using Provider = Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    [InlineData("using Provider = global::Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    [InlineData("using Provider = Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    public void The_scan_recognises_every_registration_form(string text) => RegistersAProvider(text).ShouldBeTrue();

    [Theory]
    [InlineData("services.AddSingleton(sp => new RegisteredProviders([google]));")]
    [InlineData("var google = new GoogleIdentityProvider(factory, options, callbacks, logger);")]
    [InlineData("new" + "\n" + "    RegisteredProviders(providers)")]
    public void The_scan_recognises_a_list_or_an_adapter_built_by_hand(string text) => BuildsByHand(text).ShouldBeTrue();

    [Theory]
    [InlineData("public sealed class RegisteredProviders(IEnumerable<IExternalIdentityProvider> providers)")]
    [InlineData("private readonly IReadOnlyList<IExternalIdentityProvider> _providers = providers.ToList();")]
    [InlineData("services.AddSingleton<RegisteredProviders>();")]
    [InlineData("// A provider without keys is not registered at all.")]
    public void The_scan_does_not_mistake_a_consumer_or_a_comment_for_either(string text)
    {
        RegistersAProvider(text).ShouldBeFalse();
        BuildsByHand(text).ShouldBeFalse();
    }

    private static bool RegistersAProvider(string text)
    {
        var compact = Compact(text);
        return compact.Contains("<IExternalIdentityProvider,", StringComparison.Ordinal)
               || compact.Contains("<IExternalIdentityProvider>(", StringComparison.Ordinal)
               || compact.Contains("typeof(IExternalIdentityProvider)", StringComparison.Ordinal)
               || AliasOfThePort.IsMatch(compact);
    }

    // A using alias for the port, in any qualification: a registration through the alias names the port nowhere else.
    private static readonly Regex AliasOfThePort = new(
        @"using[A-Za-z_]\w*=(global::)?(\w+\.)*IExternalIdentityProvider;", RegexOptions.CultureInvariant);

    private static bool BuildsByHand(string text)
    {
        var compact = Compact(text);
        return compact.Contains("newRegisteredProviders(", StringComparison.Ordinal)
               || compact.Contains("newGoogleIdentityProvider(", StringComparison.Ordinal);
    }

    private static string Compact(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));

    private static List<string> SourceFilesWhere(Func<string, bool> predicate)
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => predicate(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(srcRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
