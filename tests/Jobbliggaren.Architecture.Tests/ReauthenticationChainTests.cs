using System.Reflection;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1739 — the re-authentication chain (ADR 0142 D5) has the same one-consumer-per-link shape as the login
/// chain (<see cref="LoginProofChainTests"/>): the grant store is reached by exactly the four types that
/// issue or redeem a grant, and the re-authentication binding is constructed in exactly the two handlers of
/// the re-auth arm and the service that redeems it. The reach test is the load-bearing one: the two re-auth
/// handlers take neither the outcome function that mints a session, nor the session store, nor a
/// password check, so a bound proof can never become a login (D5's "never a session"). Same scan as the
/// login chain's: every composing assembly, every constructor and method parameter, compiler-generated ones
/// included.
/// </summary>
public sealed class ReauthenticationChainTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] Composing =
    [
        typeof(LoginProofOutcome).Assembly,
        typeof(UserAccountService).Assembly,
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
    public void Exactly_the_issuers_and_the_redeemers_take_the_grant_store()
    {
        // The outcome function issues the LoginComplete grant, complete redeems it; the re-auth verify handler
        // issues the Reauthentication grant, the service redeems it. A fifth consumer is a new grant path and
        // is decided here, not discovered in production.
        ConsumersOf(typeof(IGrantStore)).ShouldBe(
        [
            typeof(CompleteLoginChallengeCommandHandler).FullName!,
            typeof(VerifyReauthenticationChallengeCommandHandler).FullName!,
            typeof(LoginProofOutcome).FullName!,
            typeof(ReauthenticationService).FullName!,
        ]);
    }

    [Fact]
    public void Exactly_the_login_arm_and_the_re_auth_arm_take_the_challenge_store()
    {
        // The bound members live on the login challenge's port (ADR 0142 Amendment (4): a third kind of challenge
        // is the signal to split it). Until then, the port's consumers are the login arm's three and the re-auth
        // arm's two; a sixth is a new challenge path and is decided here.
        ConsumersOf(typeof(ILoginChallengeStore)).ShouldBe(
        [
            typeof(ConsumeLoginLinkCommandHandler).FullName!,
            typeof(RequestReauthenticationChallengeCommandHandler).FullName!,
            typeof(VerifyLoginChallengeCommandHandler).FullName!,
            typeof(VerifyReauthenticationChallengeCommandHandler).FullName!,
            typeof(LoginChallengeIssuer).FullName!,
        ]);
    }

    [Fact]
    public void The_re_auth_arm_can_reach_neither_a_session_nor_a_password_check()
    {
        // The two re-auth handlers' constructor dependencies, and those of any concrete class among them. A
        // verified re-auth code must be a grant and nothing else (D5): no outcome function, no session grant,
        // no session store; and the arm never reads a password or touches lockout.
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>(
        [
            typeof(RequestReauthenticationChallengeCommandHandler),
            typeof(VerifyReauthenticationChallengeCommandHandler),
        ]);
        while (pending.TryPop(out var type))
        {
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                if (reached.Add(parameter.ParameterType) && parameter.ParameterType is { IsClass: true, IsAbstract: false })
                    pending.Push(parameter.ParameterType);
            }
        }

        reached.ShouldNotContain(typeof(LoginProofOutcome));
        reached.ShouldNotContain(typeof(PasswordlessSessionGrant));
        reached.ShouldNotContain(typeof(ISessionStore));
        reached.ShouldNotContain(typeof(LoginSubjectResolver));
        reached.ShouldNotContain(typeof(ILoginTimingEqualizer));
        reached.ShouldNotContain(typeof(ILoginChallengeDispatcher));
        reached.ShouldContain(typeof(IGrantStore));
        reached.ShouldContain(typeof(ILoginChallengeStore));
    }

    [Fact]
    public void The_re_auth_service_takes_the_grant_store_and_not_the_account_service()
    {
        // D5's member swap, pinned on the constructor: the password check left with IUserAccountService and the
        // grant arrived with IGrantStore. A service that took both would have a password path back.
        var parameters = typeof(ReauthenticationService)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        parameters.ShouldContain(typeof(IGrantStore));
        parameters.ShouldNotContain(typeof(IUserAccountService));
    }
}
