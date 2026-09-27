using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
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
    public void Neither_path_that_sends_a_login_code_can_reach_an_account()
    {
        // #1745 (dotnet-architect R6.1, V3), ADR 0142 D2's "the path that mints a challenge never reads the account",
        // now for both of its entries: the typed address, and a provider login with no link. Replaces the request
        // handler's constructor pin, which went green by construction once the gates moved to LoginChallengeAdmission.
        var reached = ReachedFrom(typeof(RequestLoginChallengeCommandHandler), typeof(PendingLinkChallenge));

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
        reached.ShouldContain(typeof(IGrantStore));
    }

    [Fact]
    public void Only_the_two_paths_that_send_a_login_code_reach_its_gates()
    {
        ConsumersOf(typeof(LoginChallengeAdmission)).ShouldBe(
            [.. new[] { typeof(PendingLinkChallenge).FullName!, typeof(RequestLoginChallengeCommandHandler).FullName! }.Order()]);
        ConsumersOf(typeof(PendingLinkChallenge)).ShouldBe([typeof(CompleteExternalLoginCommandHandler).FullName!]);
    }

    [Fact]
    public void The_link_in_the_mail_can_never_bind_a_pending_provider_login()
    {
        // #1745 row 2 (senior-cto-advisor 1a, security-auditor V-3): only the code binds. The pending link reaches the
        // outcome through one parameter, which the link's handler cannot supply, and neither the link's command nor
        // its request body has a member to carry the grant in.
        // The record itself is left out: its compiler-written Equals and copy constructor take one.
        var consumers = ConsumersOf(typeof(GrantSubject.PendingExternalLink))
            .Where(name => name != typeof(GrantSubject.PendingExternalLink).FullName)
            .ToList();

        consumers.ShouldBe([typeof(LoginProofOutcome).FullName!]);
        consumers.ShouldNotContain(typeof(ConsumeLoginLinkCommandHandler).FullName!);
        typeof(ConsumeLoginLinkCommand).GetProperties().Select(p => p.Name).ShouldBe(["Token"]);
        typeof(AuthEndpoints.LoginLinkRequest).GetProperties().Select(p => p.Name).ShouldBe(["Token"]);
    }

    [Fact]
    public void A_code_verification_takes_no_address_so_the_cookies_echo_is_never_input()
    {
        // security-auditor V-1: the binding compares the grant's server-held address with the address the code
        // proved. The flow cookie is unsigned, so the address it echoes must have no way in.
        typeof(VerifyLoginChallengeCommand).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["ChallengeId", "Code", "LinkGrant"]);
        typeof(AuthEndpoints.LoginChallengeVerifyRequest).GetProperties().Select(p => p.Name)
            .Order(StringComparer.Ordinal).ShouldBe(["ChallengeId", "Code", "LinkGrant"]);
    }

    [Fact]
    public void Only_the_code_verification_redeems_a_pending_link()
    {
        // #1745 (test-writer Major A): complete must never redeem purpose 5, whose address is only asserted. The
        // purpose is named by its own subject, by the store that seals and opens it, and by verify alone.
        var srcRoot = Path.Combine(RepoRoot(), "src");

        Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("GrantPurpose.PendingExternalLink", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(srcRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ShouldBe(
            [
                "Jobbliggaren.Application/Auth/Commands/VerifyLoginChallenge/VerifyLoginChallengeCommandHandler.cs",
                "Jobbliggaren.Application/Auth/Grants/GrantSubject.cs",
                "Jobbliggaren.Infrastructure/Auth/Grants/RedisGrantStore.cs",
            ]);
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
    // #1745 (senior-cto-advisor 1c point 2): GitHub's adapter makes no VerifiedEmail at all.
    [Fact]
    public void Only_the_provider_adapter_and_the_grant_store_make_a_verified_email_in_source() =>
        MakersInSource(nameof(VerifiedEmail)).ShouldBe(
        [
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GoogleIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/Grants/RedisGrantStore.cs: 1",
        ]);

    // #1745 (dotnet-architect R1): an address becomes an AssertedEmail in the GitHub adapter, and again only where the
    // grant store reads back the purpose-5 payload it was sealed into. Grant 6 carries the code-proven address as a
    // string, never an asserted one, so the store has one maker, not two.
    [Fact]
    public void Only_the_github_adapter_and_the_grant_store_make_an_asserted_email_in_source() =>
        MakersInSource(nameof(AssertedEmail)).ShouldBe(
        [
            "Jobbliggaren.Infrastructure/Auth/ExternalLogins/GitHubIdentityProvider.cs: 1",
            "Jobbliggaren.Infrastructure/Auth/Grants/RedisGrantStore.cs: 1",
        ]);

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

        // The same scan counts the asserted type's factory by the same forms, and never the other type's.
        var asserted = source.Replace(nameof(VerifiedEmail), nameof(AssertedEmail), StringComparison.Ordinal);
        Makers(asserted, nameof(AssertedEmail)).ShouldBe(1);
        Makers(asserted, nameof(VerifiedEmail)).ShouldBe(0);
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
