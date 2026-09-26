using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Commands.SeedAccount;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1735 — each link of the passwordless chain has one consumer: the first-proof write
/// (<see cref="IInboxProofRecorder"/>) is reachable only from <see cref="PasswordlessSessionGrant"/>, the grant
/// only from <see cref="LoginProofOutcome"/>, and that only from the two proof handlers and <c>complete</c>
/// (#1737); the account lookup is reachable only from <see cref="LoginSubjectResolver"/>, and that only from
/// the consumer, the outcome function, <c>complete</c> and the Development seed seam, never from the request
/// path that mints a challenge (ADR 0142 D2); and an account is opened only through
/// <see cref="AccountRegistrar"/>, which only <c>complete</c> and the seed seam reach (ADR 0142 part 5a). The
/// scan covers every assembly that composes services, the Api's included, and every constructor and method
/// parameter of every type, compiler-generated ones included, so a minimal-API lambda asking for a link by parameter is seen too. A service-locator call is not
/// a parameter; the source scan covers the write's port by name.
/// </summary>
public sealed class LoginProofChainTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] Composing =
    [
        typeof(LoginProofOutcome).Assembly,
        typeof(IdentityInboxProofRecorder).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AuthEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
    ];

    private static string[] ConsumersOf(Type dependency) =>
        [.. Composing.SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors(Declared).Cast<MethodBase>().Concat(t.GetMethods(Declared))
                .Any(m => m.GetParameters().Any(p => p.ParameterType == dependency)))
            .Select(t => t.FullName!)
            .Order()];

    [Fact]
    public void Only_the_grant_can_record_an_inbox_proof()
    {
        ConsumersOf(typeof(IInboxProofRecorder)).ShouldBe([typeof(PasswordlessSessionGrant).FullName!]);
    }

    [Fact]
    public void Only_the_outcome_function_can_grant_and_only_the_proof_handlers_and_complete_can_reach_it()
    {
        ConsumersOf(typeof(PasswordlessSessionGrant)).ShouldBe([typeof(LoginProofOutcome).FullName!]);
        ConsumersOf(typeof(LoginProofOutcome)).ShouldBe(
        [
            typeof(CompleteExternalLoginCommandHandler).FullName!,
            typeof(CompleteLoginChallengeCommandHandler).FullName!,
            typeof(ConsumeLoginLinkCommandHandler).FullName!,
            typeof(VerifyLoginChallengeCommandHandler).FullName!,
        ]);
    }

    [Fact]
    public void Only_the_resolver_asks_who_a_provider_login_belongs_to_and_only_the_linker_writes_one()
    {
        // #1744 (senior-cto-advisor F2): the one classifier answers the link as well; the one writer adds it and
        // its audit row, and only the outcome function reaches the writer, after the address has matched.
        ConsumersOf(typeof(IExternalLoginLookup)).ShouldBe([typeof(LoginSubjectResolver).FullName!]);
        ConsumersOf(typeof(IExternalLoginWriter)).ShouldBe([typeof(ExternalLoginLinker).FullName!]);
        ConsumersOf(typeof(ExternalLoginLinker)).ShouldBe([typeof(LoginProofOutcome).FullName!]);
    }

    [Fact]
    public void Only_the_resolver_reads_the_account_and_never_on_the_path_that_mints_a_challenge()
    {
        ConsumersOf(typeof(ILoginAccountLookup)).ShouldBe([typeof(LoginSubjectResolver).FullName!]);
        ConsumersOf(typeof(LoginSubjectResolver)).ShouldBe(
        [
            typeof(CompleteLoginChallengeCommandHandler).FullName!,
            typeof(LoginChallengeIssuer).FullName!,
            typeof(LoginProofOutcome).FullName!,
            typeof(DevSeedAccountCommandHandler).FullName!,
        ]);
    }

    [Fact]
    public void Only_complete_and_the_development_seed_seam_open_an_account_and_only_through_the_registrar()
    {
        ConsumersOf(typeof(IPasswordlessAccountCreator)).ShouldBe([typeof(AccountRegistrar).FullName!]);
        ConsumersOf(typeof(AccountRegistrar)).ShouldBe(
        [
            typeof(CompleteLoginChallengeCommandHandler).FullName!,
            typeof(DevSeedAccountCommandHandler).FullName!,
        ]);
    }

    [Fact]
    public void The_proof_chain_cannot_reach_the_account_service()
    {
        // Every port the three handlers can reach, following concrete classes through their constructors. The
        // account is reached through ILoginAccountLookup, which offers a lookup and nothing else, and created
        // through IPasswordlessAccountCreator.
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>(
        [
            typeof(VerifyLoginChallengeCommandHandler),
            typeof(ConsumeLoginLinkCommandHandler),
            typeof(CompleteLoginChallengeCommandHandler),
            typeof(CompleteExternalLoginCommandHandler),
        ]);
        while (pending.TryPop(out var type))
        {
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                if (reached.Add(parameter.ParameterType) && parameter.ParameterType is { IsClass: true, IsAbstract: false })
                    pending.Push(parameter.ParameterType);
            }
        }

        reached.ShouldNotContain(typeof(IUserAccountService));
        reached.ShouldContain(typeof(ILoginAccountLookup));
    }

    [Fact]
    public void Only_the_port_the_grant_and_the_adapter_name_the_inbox_proof_port_in_source()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        var namers = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains(nameof(IInboxProofRecorder), StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(srcRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        namers.ShouldBe(
        [
            "Jobbliggaren.Application/Auth/LoginChallenges/IInboxProofRecorder.cs",
            "Jobbliggaren.Application/Auth/LoginChallenges/PasswordlessSessionGrant.cs",
            "Jobbliggaren.Infrastructure/Auth/LoginChallenges/IdentityInboxProofRecorder.cs",
            "Jobbliggaren.Infrastructure/DependencyInjection.cs",
        ]);
    }

    // #1744 (dotnet-architect, PR S): an address becomes a VerifiedEmail in the provider adapter, and again only where
    // the grant store reads back the purpose-4 payload that address was sealed into. Counted per file, so a second
    // maker inside either of the two is seen as well.
    [Fact]
    public void Only_the_provider_adapter_and_the_grant_store_make_a_verified_email_in_source()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        var makers = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => (
                Path: Path.GetRelativePath(srcRoot, path).Replace('\\', '/'),
                Count: VerifiedEmailMakers(File.ReadAllText(path))))
            .Where(file => file.Count > 0)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"{file.Path}: {file.Count}")
            .ToList();

        makers.ShouldBe(
        [
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/Grants/RedisGrantStore.cs: 1",
        ]);
    }

    [Theory]
    [InlineData("var email = VerifiedEmail.TryCreate(address);")]
    [InlineData("var email = VerifiedEmail\n    .TryCreate(address);")]
    [InlineData("var emails = addresses.Select(VerifiedEmail.TryCreate);")]
    [InlineData("using static Jobbliggaren.Application.Auth.ExternalLogins.VerifiedEmail;\nvar email = TryCreate(address);")]
    [InlineData("using Proof = Jobbliggaren.Application.Auth.ExternalLogins.VerifiedEmail;\nvar email = Proof.TryCreate(address);")]
    [InlineData("using Proof = global::Jobbliggaren.Application.Auth.ExternalLogins.VerifiedEmail;\nvar email = Proof.TryCreate(address);")]
    [InlineData("using static global::Jobbliggaren.Application.Auth.ExternalLogins.VerifiedEmail;\nvar email = TryCreate(address);")]
    [InlineData("using static Jobbliggaren.Application.Auth.ExternalLogins.VerifiedEmail;\nvar emails = addresses.Select(TryCreate);")]
    public void The_verified_email_scan_counts_every_way_of_naming_the_factory(string source) =>
        VerifiedEmailMakers(source).ShouldBe(1);

    private static int VerifiedEmailMakers(string source)
    {
        var joined = Regex.Replace(source, @"\s*\.\s*", ".").Replace("global::", string.Empty, StringComparison.Ordinal);
        var names = Regex.Matches(joined, @"\busing\s+(\w+)\s*=\s*[\w.]*\bVerifiedEmail\s*;")
            .Select(alias => alias.Groups[1].Value)
            .Append(nameof(VerifiedEmail));
        var qualified = names.Sum(name => Regex.Count(joined, $@"\b{name}\.TryCreate\b"));
        var imported = Regex.IsMatch(joined, @"\busing\s+static\s+[\w.]*\bVerifiedEmail\s*;")
            ? Regex.Count(joined, @"(?<![\w.])TryCreate\b")
            : 0;
        return qualified + imported;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/LoginProofChainTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
