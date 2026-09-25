using System.Reflection;
using System.Runtime.CompilerServices;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
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
/// path that mints a challenge (ADR 0142 D2); and an account is opened only through <see cref="AccountRegistrar"/>,
/// which only <c>complete</c> and the seed seam reach (ADR 0142 part 5a). The scan covers every assembly that composes services,
/// the Api's included, and every constructor and method parameter of every type, compiler-generated ones
/// included, so a minimal-API lambda asking for a link by parameter is seen too. A service-locator call is not
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
            typeof(CompleteLoginChallengeCommandHandler).FullName!,
            typeof(ConsumeLoginLinkCommandHandler).FullName!,
            typeof(VerifyLoginChallengeCommandHandler).FullName!,
        ]);
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
    public void The_proof_chain_can_reach_neither_a_password_check_nor_lockout()
    {
        // Every port the three handlers can reach, following concrete classes through their constructors. The
        // account is reached through ILoginAccountLookup, which offers a lookup and nothing else, and created
        // through IPasswordlessAccountCreator, which offers no password check.
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>(
        [
            typeof(VerifyLoginChallengeCommandHandler),
            typeof(ConsumeLoginLinkCommandHandler),
            typeof(CompleteLoginChallengeCommandHandler),
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

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/LoginProofChainTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
