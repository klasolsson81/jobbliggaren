using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1744, 6a PR G (senior-cto-advisor F11c) — an identity provider exists on a host only through its gate, which
/// registers nothing without a client id. So each registered adapter is registered in exactly its own gate file, and no
/// file hands one to <c>RegisteredProviders</c> directly or builds an adapter by hand (security-auditor Minor 4 on
/// #1861). Each file is read with its whitespace removed, so a registration broken over lines is still one text.
/// <para>
/// #1745 (dotnet-architect V5, test-writer Major 2): nothing here names an adapter. The adapters are the build's
/// <see cref="IExternalIdentityProvider"/> implementations, by reflection, and the registered ones are the type
/// arguments the registrations name, so a third provider is covered without a new literal. In 6b PR 1 the GitHub
/// adapter exists and is registered nowhere; PR 2's gate makes the registered set equal the adapter set.
/// </para>
/// </summary>
public sealed class ExternalProvidersRegisterOnlyThroughTheirGateTests
{
    private const string GateDirectory = "Jobbliggaren.Infrastructure/Auth/ExternalLogins";

    /// <summary>Every adapter this build has: the concrete <see cref="IExternalIdentityProvider"/> implementations.</summary>
    private static readonly string[] Adapters =
    [
        .. typeof(GoogleIdentityProvider).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && typeof(IExternalIdentityProvider).IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal),
    ];

    // The control: an empty set would make BuildsByHand scan for nothing.
    [Fact]
    public void The_adapters_are_found_by_reflection_so_the_scans_below_are_not_empty() =>
        Adapters.ShouldBe([nameof(GitHubIdentityProvider), nameof(GoogleIdentityProvider)]);

    [Fact]
    public void Each_registered_adapter_is_registered_only_in_its_own_gate_file()
    {
        var registered = SourceFilesWhere(RegistersAProvider)
            .SelectMany(file => RegisteredAdapters(File.ReadAllText(Path.Combine(SrcRoot(), file))))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // A registration whose adapter cannot be read out of it (a using alias, say) names no gate, so it fails here.
        SourceFilesWhere(RegistersAProvider).ShouldBe(
            [.. registered.Select(adapter => $"{GateDirectory}/{adapter}Registration.cs")]);
        registered.ShouldAllBe(adapter => Adapters.Contains(adapter));
    }

    [Fact]
    public void Only_google_is_registered_while_github_ships_inert()
    {
        // 6b PR 1's inertness pin, in source (dotnet-architect K1, R7): the GitHub adapter exists, and no gate
        // registers it. PR 2 adds GitHubIdentityProviderRegistration and turns this into "registered == Adapters".
        var registered = SourceFilesWhere(RegistersAProvider)
            .SelectMany(file => RegisteredAdapters(File.ReadAllText(Path.Combine(SrcRoot(), file))))
            .Distinct()
            .ToList();

        registered.ShouldBe([nameof(GoogleIdentityProvider)]);
        Adapters.Except(registered).ShouldBe([nameof(GitHubIdentityProvider)]);
    }

    [Fact]
    public void Nothing_in_source_binds_the_github_options_or_calls_a_github_gate()
    {
        // The same pin from the configuration side: the options class is named by itself and by the adapter that
        // reads it, and by nothing that binds a section to it.
        SourceFilesWhere(text => text.Contains(nameof(GitHubOAuthOptions), StringComparison.Ordinal)).ShouldBe(
        [
            $"{GateDirectory}/GitHubIdentityProvider.cs",
            $"{GateDirectory}/GitHubOAuthOptions.cs",
        ]);
        SourceFilesWhere(text => text.Contains("AddGitHubIdentityProvider", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void No_source_file_builds_the_provider_list_or_an_adapter_by_hand() =>
        SourceFilesWhere(BuildsByHand).ShouldBeEmpty();

    [Theory]
    [InlineData("services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("services.AddSingleton<IExternalIdentityProvider>(sp => new GoogleIdentityProvider(...));")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalIdentityProvider, GoogleIdentityProvider>());")]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IExternalIdentityProvider), typeof(GoogleIdentityProvider), lifetime));")]
    [InlineData("services.AddSingleton<" + "\n" + "        IExternalIdentityProvider, GoogleIdentityProvider>();")]
    [InlineData("services.AddSingleton<IExternalIdentityProvider, GitHubIdentityProvider>();")]
    [InlineData("using Provider = Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    [InlineData("using Provider = global::Jobbliggaren.Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    [InlineData("using Provider = Application.Auth.ExternalLogins.IExternalIdentityProvider;")]
    public void The_scan_recognises_every_registration_form(string text) => RegistersAProvider(text).ShouldBeTrue();

    [Theory]
    [InlineData("services.AddSingleton<IExternalIdentityProvider, GitHubIdentityProvider>();", "GitHubIdentityProvider")]
    [InlineData("services.AddSingleton<IExternalIdentityProvider>(sp => new GitHubIdentityProvider(a, b, c, d));", "GitHubIdentityProvider")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalIdentityProvider, GitHubIdentityProvider>());", "GitHubIdentityProvider")]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IExternalIdentityProvider), typeof(GitHubIdentityProvider), lifetime));", "GitHubIdentityProvider")]
    [InlineData("services.AddSingleton<" + "\n" + "        IExternalIdentityProvider, GoogleIdentityProvider>();", "GoogleIdentityProvider")]
    public void The_scan_reads_the_adapter_a_registration_names(string text, string adapter) =>
        RegisteredAdapters(text).ShouldBe([adapter]);

    [Theory]
    [InlineData("services.AddSingleton(sp => new RegisteredProviders([google]));")]
    [InlineData("var google = new GoogleIdentityProvider(factory, options, callbacks, logger);")]
    [InlineData("new" + "\n" + "    RegisteredProviders(providers)")]
    [InlineData("RegisteredProviders providers = new([google]);")]
    [InlineData("services.AddSingleton<RegisteredProviders>(_ => new([google]));")]
    [InlineData("services.AddSingleton<RegisteredProviders>(sp => new([ActivatorUtilities.CreateInstance<GoogleIdentityProvider>(sp)]));")]
    [InlineData("GoogleIdentityProvider google = new(factory, options, callbacks, logger);")]
    [InlineData("var google = ActivatorUtilities.CreateInstance<GoogleIdentityProvider>(sp);")]
    [InlineData("var github = new GitHubIdentityProvider(factory, options, callbacks, logger);")]
    [InlineData("GitHubIdentityProvider github = new(factory, options, callbacks, logger);")]
    [InlineData("var github = ActivatorUtilities.CreateInstance<GitHubIdentityProvider>(sp);")]
    [InlineData("new" + "\n" + "    GitHubIdentityProvider(factory, options, callbacks, logger)")]
    public void The_scan_recognises_a_list_or_an_adapter_built_by_hand(string text) => BuildsByHand(text).ShouldBeTrue();

    [Theory]
    [InlineData("public sealed class RegisteredProviders(IEnumerable<IExternalIdentityProvider> providers)")]
    [InlineData("private readonly IReadOnlyList<IExternalIdentityProvider> _providers = providers.ToList();")]
    [InlineData("services.AddSingleton<RegisteredProviders>();")]
    [InlineData("// A provider without keys is not registered at all.")]
    [InlineData("internal sealed partial class GitHubIdentityProvider(IHttpClientFactory httpClientFactory)")]
    [InlineData("var client = httpClientFactory.CreateClient(GitHubIdentityProvider.HttpClientName);")]
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

    // The adapter a registration names, in each form RegistersAProvider recognises that names one.
    private static readonly Regex RegisteredAdapter = new(
        @"<IExternalIdentityProvider,(?<a>\w+)>|typeof\(IExternalIdentityProvider\),typeof\((?<a>\w+)\)"
        + @"|<IExternalIdentityProvider>\([^)]*=>new(?<a>\w+)\(",
        RegexOptions.CultureInvariant);

    private static IEnumerable<string> RegisteredAdapters(string text) =>
        RegisteredAdapter.Matches(Compact(text)).Select(match => match.Groups["a"].Value).Distinct();

    private static bool BuildsByHand(string text)
    {
        var compact = Compact(text);
        return compact.Contains("newRegisteredProviders(", StringComparison.Ordinal)
               || compact.Contains("CreateInstance<RegisteredProviders>", StringComparison.Ordinal)
               || Adapters.Any(adapter => compact.Contains($"new{adapter}(", StringComparison.Ordinal)
                                          || compact.Contains($"CreateInstance<{adapter}>", StringComparison.Ordinal))
               || TargetTypedByHand.IsMatch(compact);
    }

    // A target-typed new: a variable or field of the list or of any adapter, or a factory registration of the list.
    private static readonly Regex TargetTypedByHand = new(
        $@"(RegisteredProviders|{string.Join('|', Adapters)})\??\w+=new\(|<RegisteredProviders>\([\w()]*=>",
        RegexOptions.CultureInvariant);

    private static string Compact(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));

    private static List<string> SourceFilesWhere(Func<string, bool> predicate)
    {
        var srcRoot = SrcRoot();
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

    private static string SrcRoot() => Path.Combine(RepoRoot(), "src");

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
