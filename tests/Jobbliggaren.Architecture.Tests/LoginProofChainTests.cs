using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
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
    public void Only_the_protected_account_deletion_handler_erases_a_login()
    {
        // #1976: provider erasure joins the protected deletion transaction before audit/save/commit.
        // Its command refuses an xmin conflict instead of replaying any part of that transaction.
        ConsumersOf(typeof(IExternalLoginEraser)).ShouldBe([typeof(DeleteAccountCommandHandler).FullName!]);
    }

    [Fact]
    public void The_erasure_and_its_backstop_take_no_provider()
    {
        // dotnet-architect, a FORM pin and said to be one: with a provider parameter a caller would pass the registered
        // providers, and a provider whose keys were removed would keep its rows.
        typeof(IExternalLoginEraser).GetMethod(nameof(IExternalLoginEraser.EraseAllAsync))!.GetParameters()
            .Select(parameter => parameter.ParameterType).ShouldBe([typeof(Guid), typeof(CancellationToken)]);
        typeof(IAccountHardDeleter)
            .GetMethod(nameof(IAccountHardDeleter.EraseExternalLoginsOfAccountsPendingDeletionAsync))!.GetParameters()
            .Select(parameter => parameter.ParameterType).ShouldBe([typeof(CancellationToken)]);
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

    // Every type a root can reach, following concrete classes through their constructors. A service locator inside an
    // async body would hide in a compiler-generated state machine, so the constructors are what is walked, and
    // IServiceProvider is itself forbidden below.
    private static HashSet<Type> ReachedFrom(params Type[] roots)
    {
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>(roots);
        while (pending.TryPop(out var type))
        {
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                if (reached.Add(parameter.ParameterType) && parameter.ParameterType is { IsClass: true, IsAbstract: false })
                    pending.Push(parameter.ParameterType);
            }
        }

        return reached;
    }

    [Fact]
    public void The_path_that_sends_a_login_code_can_reach_no_account()
    {
        // #1745 (dotnet-architect R6.1, V3), ADR 0142 D2's "the path that mints a challenge never reads the account".
        // Replaces the request handler's constructor pin, which went green by construction once the gates moved to
        // LoginChallengeAdmission.
        var reached = ReachedFrom(typeof(RequestLoginChallengeCommandHandler));

        Type[] forbidden =
        [
            typeof(IUserAccountService), typeof(ILoginAccountLookup), typeof(IAppDbContext),
            typeof(LoginSubjectResolver), typeof(IExternalLoginLookup), typeof(ILoginChallengeStore),
            typeof(ISessionStore), typeof(IServiceProvider),
        ];
        reached.Intersect(forbidden).ShouldBeEmpty();

        // The control: the walk reaches the gates and the hand-off, so the absences above are not an empty walk's.
        reached.ShouldContain(typeof(LoginChallengeAdmission));
        reached.ShouldContain(typeof(ILoginChallengeDispatcher));
        reached.ShouldContain(typeof(IRateBudget));
    }

    [Fact]
    public void Only_the_code_request_reaches_the_login_code_gates() =>
        ConsumersOf(typeof(LoginChallengeAdmission)).ShouldBe([typeof(RequestLoginChallengeCommandHandler).FullName!]);

    [Fact]
    public void A_provider_login_can_reach_no_login_code()
    {
        // #1745 (ADR 0142 Amendment (18)): a provider login sends no code. Its handler reaches nothing that admits,
        // mints, stores or mails one, and only the gates hand a code to the dispatch consumer.
        var reached = ReachedFrom(typeof(CompleteExternalLoginCommandHandler));

        Type[] forbidden =
        [
            typeof(LoginChallengeAdmission), typeof(ILoginChallengeDispatcher), typeof(LoginChallengeIssuer),
            typeof(ILoginChallengeStore), typeof(IEmailSender), typeof(IServiceProvider),
            typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory), typeof(Mediator.IMediator),
            typeof(Mediator.ISender),
        ];
        reached.Intersect(forbidden).ShouldBeEmpty();

        // The control: the walk reaches the outcome function, so the absences above are not an empty walk's.
        reached.ShouldContain(typeof(LoginProofOutcome));
        ConsumersOf(typeof(ILoginChallengeDispatcher)).ShouldBe([typeof(LoginChallengeAdmission).FullName!]);
        ConsumersOf(typeof(LoginChallengeIssuer)).ShouldBeEmpty();
    }

    [Fact]
    public void A_code_verification_takes_no_address_so_the_cookies_echo_is_never_input()
    {
        // security-auditor V-1: the flow cookie is unsigned, so the address it echoes must have no way in.
        typeof(VerifyLoginChallengeCommand).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["ChallengeId", "Code"]);
        typeof(AuthEndpoints.LoginChallengeVerifyRequest).GetProperties().Select(p => p.Name)
            .Order(StringComparer.Ordinal).ShouldBe(["ChallengeId", "Code"]);
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

    // #1744 (dotnet-architect, PR S): an address becomes a VerifiedEmail in a provider adapter, and again only where
    // the grant store reads back the purpose-4 payload that address was sealed into. Counted per file, so a second
    // maker inside any of them is seen as well. #1745 (ADR 0142 Amendment (18)): GitHub's adapter is one.
    [Fact]
    public void Only_the_provider_adapters_and_the_grant_store_make_a_verified_email_in_source() =>
        MakersInSource(nameof(VerifiedEmail)).ShouldBe(
        [
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GitHubIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/LinkedInIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/Grants/RedisGrantStore.cs: 1",
        ]);

    // #1745 (ADR 0142 Amendment (18), dotnet-architect): which address qualifies is each adapter's own rule, so a
    // provider is named only by its adapter, and its login method only by the key type. A handler branch on the
    // provider would name either elsewhere.
    [Fact]
    public void Only_the_provider_adapters_name_a_provider_in_source()
    {
        var providers = string.Join("|", ProviderMembers());
        var methods = string.Join("|", ExternalProviderKey.Known.Select(key => key.LoginMethod.ToString()));

        FilesMatching(new Regex(@"\bExternalProviderKey\s*\.\s*(?:" + providers + @")\b")).ShouldBe(
        [
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GitHubIdentityProvider.cs",
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProvider.cs",
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/LinkedInIdentityProvider.cs",
        ]);
        FilesMatching(new Regex(@"\bLoginMethod\s*\.\s*(?:" + methods + @")\b"))
            .ShouldBe(["Jobbliggaren.Application/Auth/ExternalLogins/ExternalProviderKey.cs"]);
        FilesMatching(new Regex(@"\busing\s+static\s+[\w.:]*\bExternalProviderKey\s*;")).ShouldBeEmpty();
    }

    private static List<string> FilesMatching(Regex pattern)
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => pattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(srcRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // Every provider the key type declares, read from the type, so a new provider is scanned without editing this.
    private static IEnumerable<string> ProviderMembers() =>
        typeof(ExternalProviderKey).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(ExternalProviderKey))
            .Select(field => field.Name);

    private static List<string> MakersInSource(string typeName)
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => (
                Path: Path.GetRelativePath(srcRoot, path).Replace('\\', '/'),
                Count: Makers(File.ReadAllText(path), typeName)))
            .Where(file => file.Count > 0)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"{file.Path}: {file.Count}")
            .ToList();
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
    public void The_verified_email_scan_counts_every_way_of_naming_the_factory(string source)
    {
        Makers(source, nameof(VerifiedEmail)).ShouldBe(1);
    }

    private static int Makers(string source, string typeName)
    {
        var joined = Regex.Replace(source, @"\s*\.\s*", ".").Replace("global::", string.Empty, StringComparison.Ordinal);
        var names = Regex.Matches(joined, $@"\busing\s+(\w+)\s*=\s*[\w.]*\b{typeName}\s*;")
            .Select(alias => alias.Groups[1].Value)
            .Append(typeName);
        var qualified = names.Sum(name => Regex.Count(joined, $@"\b{name}\.TryCreate\b"));
        var imported = Regex.IsMatch(joined, $@"\busing\s+static\s+[\w.]*\b{typeName}\s*;")
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
