using FluentValidation.TestHelper;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;
using Jobbliggaren.Application.Auth.Commands.StartExternalLogin;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Queries.GetExternalLoginProviders;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.ExternalLogins;

/// <summary>#1744 (ADR 0142 D8, senior-cto-advisor F3) — the external address match, one rule and one home.</summary>
public class ExternalAddressMatchTests
{
    [Theory]
    [InlineData("anna@firma.example", "anna@firma.example")]
    [InlineData("Anna@Firma.Example", "anna@firma.example")]
    [InlineData("ANNA@FIRMA.EXAMPLE", "anna@firma.example")]
    public void IsSameAddress_ShouldMatch_WhenBothAreAsciiAndDifferOnlyInCase(string account, string provider) =>
        ExternalAddressMatch.IsSameAddress(account, provider).ShouldBeTrue();

    [Fact]
    public void IsSameAddress_ShouldNotMatch_WhenTheLocalPartsDiffer() =>
        ExternalAddressMatch.IsSameAddress("anna@firma.example", "anna.b@firma.example").ShouldBeFalse();

    // The characters Unicode maps onto ASCII (#1779): never the case-blind branch, so never equal to their target.
    [Theory]
    [InlineData(0x017F, 's')]
    [InlineData(0x212A, 'k')]
    [InlineData(0x037E, ';')]
    [InlineData(0x1FEF, '`')]
    public void IsSameAddress_ShouldNotMatch_WhenOneSideCarriesACharacterUnicodeFoldsOntoAscii(int codePoint, char ascii)
    {
        var folded = $"a{(char)codePoint}b@firma.example";
        var plain = $"a{ascii}b@firma.example";

        ExternalAddressMatch.IsSameAddress(plain, folded).ShouldBeFalse();
        ExternalAddressMatch.IsSameAddress(folded, plain).ShouldBeFalse();
    }

    [Fact]
    public void IsSameAddress_ShouldMatchANonAsciiAddress_OnlyWhenItIsSpelledTheSame()
    {
        ExternalAddressMatch.IsSameAddress("björn@firma.example", "björn@firma.example").ShouldBeTrue();
        ExternalAddressMatch.IsSameAddress("Björn@firma.example", "björn@firma.example").ShouldBeFalse();
    }
}

/// <summary>#1744 — the registered set is what the login page may offer; the known order is the page's order.</summary>
public class RegisteredProvidersTests
{
    private static IExternalIdentityProvider Provider(ExternalProviderKey key)
    {
        var provider = Substitute.For<IExternalIdentityProvider>();
        provider.Key.Returns(key);
        return provider;
    }

    private static IExternalIdentityProvider Google() => Provider(ExternalProviderKey.Google);

    private static IExternalIdentityProvider GitHub() => Provider(ExternalProviderKey.GitHub);

    private static IExternalIdentityProvider LinkedIn() => Provider(ExternalProviderKey.LinkedIn);

    [Fact]
    public void Keys_ShouldBeEmpty_WhenNoProviderIsRegistered() => new RegisteredProviders([]).Keys.ShouldBeEmpty();

    [Fact]
    public void Keys_ShouldNameGoogle_WhenGoogleIsRegistered() =>
        new RegisteredProviders([Google()]).Keys.ShouldBe([ExternalProviderKey.Google]);

    // #1745: kills "the registration order".
    [Fact]
    public void Keys_ShouldBeInTheKnownOrder_WhenTheProvidersAreRegisteredInAnother() =>
        new RegisteredProviders([GitHub(), Google()]).Keys.ShouldBe([ExternalProviderKey.Google, ExternalProviderKey.GitHub]);

    [Fact]
    public void Keys_ShouldBeInTheKnownOrder_WhenAllThreeAreRegisteredInAnother() =>
        new RegisteredProviders([GitHub(), LinkedIn(), Google()]).Keys
            .ShouldBe([ExternalProviderKey.Google, ExternalProviderKey.LinkedIn, ExternalProviderKey.GitHub]);

    [Fact]
    public void Find_ShouldAnswerTheProviderUnderItsOwnKey_WhenSeveralAreRegistered()
    {
        // #1745: kills "the first registered provider".
        var google = Google();
        var github = GitHub();
        var linkedin = LinkedIn();
        var providers = new RegisteredProviders([google, github, linkedin]);

        providers.Find("github").ShouldBeSameAs(github);
        providers.Find("google").ShouldBeSameAs(google);
        providers.Find("linkedin").ShouldBeSameAs(linkedin);
    }

    // Unknown to this build, or known and misspelled.
    [Theory]
    [InlineData("GitHub")]
    [InlineData("LinkedIn")]
    [InlineData("LINKEDIN")]
    [InlineData("myspace")]
    [InlineData("GOOGLE")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_ShouldAnswerNull_WhenTheKeyIsNotAKnownKeySpelledExactly(string? raw) =>
        new RegisteredProviders([Google(), GitHub(), LinkedIn()]).Find(raw).ShouldBeNull();

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    [InlineData("linkedin")]
    public void Find_ShouldAnswerNull_WhenTheKeyIsKnownButNotRegisteredOnThisHost(string raw) =>
        new RegisteredProviders([]).Find(raw).ShouldBeNull();
}

/// <summary>#1744 — the providers list: the registered keys, empty where no keys are set.</summary>
public class GetExternalLoginProvidersQueryHandlerTests
{
    private static IExternalIdentityProvider Provider(ExternalProviderKey key)
    {
        var provider = Substitute.For<IExternalIdentityProvider>();
        provider.Key.Returns(key);
        return provider;
    }

    [Fact]
    public async Task Handle_ShouldAnswerAnEmptyList_WhenNoProviderIsRegistered() =>
        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders([]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();

    [Fact]
    public async Task Handle_ShouldAnswerGoogle_WhenGoogleIsRegistered() =>
        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders([Provider(ExternalProviderKey.Google)]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken)).ShouldBe(["google"]);

    [Fact]
    public async Task Handle_ShouldAnswerTheKnownOrder_WhenBothAreRegisteredInAnother() =>
        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders(
                [Provider(ExternalProviderKey.GitHub), Provider(ExternalProviderKey.Google)]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken))
        .ShouldBe(["google", "github"]);

    [Fact]
    public async Task Handle_ShouldAnswerTheKnownOrder_WhenAllThreeAreRegisteredInAnother() =>
        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders(
                [Provider(ExternalProviderKey.GitHub), Provider(ExternalProviderKey.LinkedIn), Provider(ExternalProviderKey.Google)]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken))
        .ShouldBe(["google", "linkedin", "github"]);
}

/// <summary>#1744 — start: the flow is minted and the provider's URL carries the stored verifier's challenge.</summary>
public class StartExternalLoginCommandHandlerTests
{
    private readonly IOAuthStateStore _states = Substitute.For<IOAuthStateStore>();
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();
    private readonly CapturingLogger<StartExternalLoginCommandHandler> _log = new();
    private readonly IExternalIdentityProvider _google = Substitute.For<IExternalIdentityProvider>();
    private readonly IExternalIdentityProvider _github = Substitute.For<IExternalIdentityProvider>();
    private readonly OAuthState _state = OAuthState.Generate();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public StartExternalLoginCommandHandlerTests()
    {
        _google.Key.Returns(ExternalProviderKey.Google);
        _github.Key.Returns(ExternalProviderKey.GitHub);
        _states.PutAsync(Arg.Any<OAuthFlow>(), Arg.Any<CancellationToken>()).Returns(_state);
        _budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _google.BuildAuthorizeUrl(Arg.Any<OAuthState>(), Arg.Any<PkceChallenge>())
            .Returns(new Uri("https://accounts.google.com/o/oauth2/v2/auth?scripted"));
        _github.BuildAuthorizeUrl(Arg.Any<OAuthState>(), Arg.Any<PkceChallenge>())
            .Returns(new Uri("https://github.com/login/oauth/authorize?scripted"));
    }

    private StartExternalLoginCommandHandler Handler(params IExternalIdentityProvider[] providers) =>
        new(new RegisteredProviders(providers), _states, _budget, _log);

    [Fact]
    public async Task Handle_ShouldBeNotFoundAndMintNothing_WhenTheProviderIsNotRegistered()
    {
        var result = await Handler().Handle(new StartExternalLoginCommand("google", "/oversikt"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalProviderUnknown);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await _states.DidNotReceiveWithAnyArgs().PutAsync(default!, Ct);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task Handle_ShouldCountEveryStartAgainstTheOneGlobalBudget()
    {
        await Handler(_google).Handle(new StartExternalLoginCommand("google", "/oversikt"), Ct);

        await _budget.Received(1).TryConsumeAsync(
            ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRefuseAndWriteNothing_WhenTheStartBudgetIsSpent()
    {
        _budget.TryConsumeAsync(ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await Handler(_google).Handle(new StartExternalLoginCommand("google", "/oversikt"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalLoginStartsExhausted);
        await _states.DidNotReceiveWithAnyArgs().PutAsync(default!, Ct);
        var (level, eventId, message) = _log.Records.ShouldHaveSingleItem();
        (level, eventId).ShouldBe((LogLevel.Warning, 1027));
        message.ShouldContain("Provider=google");
        message.ShouldNotContain("/oversikt");
        _google.DidNotReceiveWithAnyArgs().BuildAuthorizeUrl(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldStoreTheFlowAndSendItsChallenge_WhenTheProviderIsRegistered()
    {
        var result = await Handler(_google).Handle(new StartExternalLoginCommand("google", "/ansokningar/abc"), Ct);

        result.Value.State.ShouldBe(_state);
        result.Value.AuthorizeUrl.Host.ShouldBe("accounts.google.com");
        var flow = (OAuthFlow)_states.ReceivedCalls().Single().GetArguments()[0]!;
        flow.Provider.ShouldBe(ExternalProviderKey.Google);
        flow.Next.ShouldBe("/ansokningar/abc");
        _google.Received(1).BuildAuthorizeUrl(_state, flow.Verifier.ToChallenge());
    }

    [Fact]
    public async Task Handle_ShouldStartTheNamedProviderAndChargeTheSameBudget_WhenTwoAreRegistered()
    {
        // #1745: kills "the first registered adapter builds the URL" and "a budget per provider".
        var result = await Handler(_google, _github).Handle(new StartExternalLoginCommand("github", "/cv"), Ct);

        result.Value.AuthorizeUrl.Host.ShouldBe("github.com");
        var flow = (OAuthFlow)_states.ReceivedCalls().Single().GetArguments()[0]!;
        flow.Provider.ShouldBe(ExternalProviderKey.GitHub);
        _github.Received(1).BuildAuthorizeUrl(_state, flow.Verifier.ToChallenge());
        _google.DidNotReceiveWithAnyArgs().BuildAuthorizeUrl(default!, default!);
        await _budget.Received(1).TryConsumeAsync(
            ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCarryNoPath_WhenTheWebSendsNone()
    {
        await Handler(_google).Handle(new StartExternalLoginCommand("google", null), Ct);

        ((OAuthFlow)_states.ReceivedCalls().Single().GetArguments()[0]!).Next.ShouldBeNull();
    }
}

/// <summary>
/// #1744 — the callback's order: registered provider, then the flow taken for THAT provider, then the exchange, and
/// only an identified login reaches an outcome. #1745 (ADR 0142 Amendment (18)): GitHub's address reaches it as
/// Google's does. Identities come from the production adapters (GoogleIdentities, GitHubIdentities); the rest
/// of the chain is real down to its ports, so a read of the account table shows as a call on the lookup port.
/// </summary>
public class CompleteExternalLoginCommandHandlerTests
{
    private const long GitHubId = 58323117;
    private const string Next = "/ansokningar/abc";

    private static readonly GrantToken IssuedGrant = GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw");

    private readonly IOAuthStateStore _states = Substitute.For<IOAuthStateStore>();
    private readonly IExternalIdentityProvider _google = Substitute.For<IExternalIdentityProvider>();
    private readonly IExternalIdentityProvider _github = Substitute.For<IExternalIdentityProvider>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly ILoginAccountLookup _lookup = Substitute.For<ILoginAccountLookup>();
    private readonly IExternalLoginLookup _externalLookup = Substitute.For<IExternalLoginLookup>();
    private readonly IExternalLoginWriter _externalWriter = Substitute.For<IExternalLoginWriter>();
    private readonly IInboxProofRecorder _inbox = Substitute.For<IInboxProofRecorder>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly CapturingLogger<CompleteExternalLoginCommandHandler> _log = new();
    private readonly CapturingLogger<LoginProofOutcome> _outcomeLog = new();
    private readonly OAuthState _state = OAuthState.Generate();
    private readonly PkceVerifier _verifier = PkceVerifier.Generate();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CompleteExternalLoginCommandHandlerTests()
    {
        _google.Key.Returns(ExternalProviderKey.Google);
        _github.Key.Returns(ExternalProviderKey.GitHub);
        _states.TakeAsync(_state, ExternalProviderKey.Google, Arg.Any<CancellationToken>())
            .Returns(new OAuthFlow(ExternalProviderKey.Google, _verifier, Next));
        _states.TakeAsync(_state, ExternalProviderKey.GitHub, Arg.Any<CancellationToken>())
            .Returns(new OAuthFlow(ExternalProviderKey.GitHub, _verifier, Next));
        _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>()).Returns(IssuedGrant);
        _inbox.RecordAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(InboxProof.AlreadyConfirmed);
        _sessions.CreateAsync(Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(
                SessionId.FromRaw("granted-session-id"), call.Arg<Guid>(), FakeDateTimeProvider.Default.UtcNow,
                FakeDateTimeProvider.Default.UtcNow.AddDays(30), call.Arg<SessionLifetime>()));
    }

    private bool _registrationsOpen = true;

    private CompleteExternalLoginCommandHandler Handler(params IExternalIdentityProvider[] providers)
    {
        var correlation = Substitute.For<ICorrelationIdProvider>();
        var request = Substitute.For<IRequestContextProvider>();
        var outcome = new LoginProofOutcome(
            new LoginSubjectResolver(_lookup, _externalLookup, _db),
            new PasswordlessSessionGrant(
                _inbox, _sessions, _audit, _db, FakeDateTimeProvider.Default, correlation, request),
            _grants,
            new ExternalLoginLinker(_externalWriter, _db, FakeDateTimeProvider.Default, correlation, request),
            Options.Create(new AuthOptions { RegistrationsOpen = _registrationsOpen }),
            _outcomeLog);
        return new CompleteExternalLoginCommandHandler(new RegisteredProviders(providers), _states, outcome, _log);
    }

    private CompleteExternalLoginCommand Command(string provider = "google") =>
        new(provider, "scripted-code", _state.Reveal());

    private async Task GoogleAnswersAsync(string userInfoJson) =>
        _google.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>())
            .Returns(await GoogleIdentities.ExchangeAsync(userInfoJson));

    // What the production GitHub adapter answers for an account whose one address is its verified primary.
    private async Task<ExternalSubject> GitHubAnswersAsync(string primary, long id = GitHubId)
    {
        var exchange = await GitHubIdentities.ExchangeAsync(
            GitHubApiShapes.User(id, "anna-berg-gh"), GitHubApiShapes.Emails.PrimaryVerified(primary));
        _github.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>()).Returns(exchange);
        return exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity.Subject;
    }

    // An account that can be given a session: an Identity row the lookup finds, and a live profile.
    private async Task<Guid> ActiveAccountAsync(string address)
    {
        var userId = Guid.NewGuid();
        _lookup.FindAccountAsync(address, Arg.Any<CancellationToken>()).Returns(new LoginAccount(userId, address));
        _db.JobSeekers.Add(JobSeeker.Register(
            userId, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default).Value);
        await _db.SaveChangesAsync(Ct);
        return userId;
    }

    // The identifier is linked to this account. The actor is ExternalLoginLinker.LinkAsync on a first GitHub login
    // (Handle_ShouldLinkBeforeTheSessionAndSignInAsGitHub_WhenTheAddressIsAnActiveAccountsOwn below).
    private void TheGitHubLoginIsLinkedTo(ExternalSubject subject, Guid userId) =>
        _externalLookup.FindUserIdAsync(ExternalProviderKey.GitHub, subject, Arg.Any<CancellationToken>())
            .Returns(userId);

    // ── the order ──

    [Fact]
    public async Task Handle_ShouldBeNotFoundAndLeaveTheStateUntouched_WhenTheProviderIsNotRegistered()
    {
        var result = await Handler().Handle(Command(), Ct);

        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await _states.DidNotReceiveWithAnyArgs().TakeAsync(default, default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldBeGoneAndExchangeNothing_WhenTheStateNamesNoLiveFlow()
    {
        _states.TakeAsync(_state, ExternalProviderKey.Google, Arg.Any<CancellationToken>()).Returns((OAuthFlow?)null);

        var result = await Handler(_google).Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalLoginUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _google.DidNotReceiveWithAnyArgs().ExchangeAsync(default, default, Ct);
        var (_, eventId, message) = _log.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1021);
        message.ShouldNotContain(_state.Reveal());
    }

    [Fact]
    public async Task Handle_ShouldBeGoneAndExchangeWithNeither_WhenTheFlowWasStartedForTheOtherProvider()
    {
        // §3.6, the handler's layer: a Google flow presented at GitHub's callback. The store refuses the take for the
        // wrong provider; that refusal is RedisOAuthStateStore's compare, pinned on the real store by
        // RedisOAuthStateStoreTests.A_flow_started_for_one_provider_is_refused_at_the_others_callback_and_spent.
        // Kills "Find answers the first registered provider".
        _states.TakeAsync(_state, ExternalProviderKey.GitHub, Arg.Any<CancellationToken>()).Returns((OAuthFlow?)null);

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalLoginUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _states.Received(1).TakeAsync(_state, ExternalProviderKey.GitHub, Arg.Any<CancellationToken>());
        await _states.DidNotReceive().TakeAsync(_state, ExternalProviderKey.Google, Arg.Any<CancellationToken>());
        await _google.DidNotReceiveWithAnyArgs().ExchangeAsync(default, default, Ct);
        await _github.DidNotReceiveWithAnyArgs().ExchangeAsync(default, default, Ct);
        _log.Records.ShouldHaveSingleItem().Message.ShouldContain("Provider=github");
    }

    [Fact]
    public async Task Handle_ShouldBeTheSameGone_WhenTheProviderRefusesTheCode()
    {
        // The value the adapters answer a refused code with (GoogleIdentityProviderTests and
        // GitHubIdentityProviderTests, the refused-code rows); it carries nothing, so the stub is that answer.
        _google.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>())
            .Returns(new ExternalExchange.Failed());

        var result = await Handler(_google).Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalLoginUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }

    // ── Google ──

    [Fact]
    public async Task Handle_ShouldRefuseAndReachNoOutcome_WhenGoogleIsNotAuthoritativeForTheAddress()
    {
        await GoogleAnswersAsync(GoogleUserInfoShapes.ThirdPartyVerified("110248495921238986420", "anna@outlook.example"));

        var result = await Handler(_google).Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalEmailUnverified);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldAnswerTheOutcomeAndEchoThePath_WhenTheIdentityIsVerified()
    {
        await GoogleAnswersAsync(GoogleUserInfoShapes.Gmail("110248495921238986420", "ny.person"));

        var result = await Handler(_google).Handle(Command(), Ct);

        result.Value.Outcome.ShouldBeOfType<LoginOutcome.ConsentRequired>();
        result.Value.Next.ShouldBe(Next);
        await _grants.Received(1).IssueAsync(Arg.Any<GrantSubject.LoginCompleteExternal>(), Arg.Any<CancellationToken>());
    }

    // ── GitHub: its verified primary address takes Google's path (#1745, ADR 0142 Amendment (18)) ──

    [Fact]
    public async Task Handle_ShouldLinkBeforeTheSessionAndSignInAsGitHub_WhenTheAddressIsAnActiveAccountsOwn()
    {
        var userId = await ActiveAccountAsync("anna@firma.example");
        var subject = await GitHubAnswersAsync("anna@firma.example");
        _externalWriter.LinkAsync(userId, ExternalProviderKey.GitHub, subject, Arg.Any<CancellationToken>())
            .Returns(ExternalLinkResult.Linked);

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Value.Outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        result.Value.Next.ShouldBe(Next);
        Received.InOrder(() =>
        {
            _externalWriter.LinkAsync(userId, ExternalProviderKey.GitHub, subject, Arg.Any<CancellationToken>());
            _inbox.RecordAsync(userId, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(userId, Arg.Any<string>(), LoginMethod.GitHub);
        });
    }

    [Fact]
    public async Task Handle_ShouldAskForTheTermsWithAnExternalGrant_WhenGitHubsAddressHasNoAccount()
    {
        var subject = await GitHubAnswersAsync("ny@firma.example");

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Value.Outcome.ShouldBeOfType<LoginOutcome.ConsentRequired>();
        await _grants.Received(1).IssueAsync(
            Arg.Is<GrantSubject.LoginCompleteExternal>(g => g.Provider == ExternalProviderKey.GitHub
                                                             && g.Subject == subject
                                                             && g.ProvenEmail.Value == "ny@firma.example"),
            Arg.Any<CancellationToken>());
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldSignInAsGitHubWithoutWritingALink_WhenTheLinkIsFoundOnTheAccountTheAddressNames()
    {
        var userId = await ActiveAccountAsync("anna@firma.example");
        var subject = await GitHubAnswersAsync("anna@firma.example");
        TheGitHubLoginIsLinkedTo(subject, userId);

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Value.Outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        _audit.Received(1).LoginSucceeded(userId, Arg.Any<string>(), LoginMethod.GitHub);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, typeof(LoginOutcome.AccountUnavailable))]
    [InlineData(false, typeof(LoginOutcome.RegistrationClosed))]
    public async Task Handle_ShouldRefuseAndOpenNothing_WhenTheLinkedLoginsPrimaryNamesAnotherAccount(
        bool registrationsOpen, Type expected)
    {
        // The identifier is linked to A, and the GitHub user changed the primary to B's address: refused as Google's
        // is (1024), and the identifier is never moved.
        var holder = await ActiveAccountAsync("anna@firma.example");
        await ActiveAccountAsync("bertil@firma.example");
        var subject = await GitHubAnswersAsync("bertil@firma.example");
        TheGitHubLoginIsLinkedTo(subject, holder);
        _registrationsOpen = registrationsOpen;

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Value.Outcome.ShouldBeOfType(expected);
        _outcomeLog.Records.ShouldHaveSingleItem().EventId.ShouldBe(1024);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldRefuseWithTheActionableCodeAndReadNothing_WhenGitHubRefusesTheAddress()
    {
        // senior-cto-advisor point 2: 400, not 410. The value is the one GitHubIdentityProvider answers
        // unverified_user_email with (GitHubIdentityProviderTests.ExchangeAsync_ShouldRefuseTheAddressWithoutReadingTheUser_WhenGitHubSaysThePrimaryIsUnverified);
        // it carries nothing, so the stub is that answer.
        _github.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>())
            .Returns(new ExternalExchange.AddressRefused());

        var result = await Handler(_google, _github).Handle(Command("github"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalEmailUnverified);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        _externalLookup.ReceivedCalls().ShouldBeEmpty();
        _lookup.ReceivedCalls().ShouldBeEmpty();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }
}

/// <summary>#1744 — the inputs are bounded before anything reaches Redis or a provider.</summary>
public class ExternalLoginValidatorTests
{
    private readonly StartExternalLoginCommandValidator _start = new();
    private readonly CompleteExternalLoginCommandValidator _complete = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/oversikt")]
    [InlineData("/ansokningar/abc-123?flik=status")]
    public void Start_ShouldAdmit_WhenTheNextIsAbsentOrASameSitePath(string? next) =>
        _start.TestValidate(new StartExternalLoginCommand("google", next)).ShouldNotHaveAnyValidationErrors();

    public static TheoryData<string> RefusedNext => new()
    {
        "https://evil.example/",
        "//evil.example/",
        "oversikt",
        "/" + (char)9 + "/evil.example",
        "/" + (char)92 + "evil.example",
        "/cv" + (char)127,
        "/cv" + (char)0x85,
        "/cv" + (char)0x9F,
        "/" + new string('a', ExternalLoginPolicy.MaxNextLength),
    };

    [Theory]
    [MemberData(nameof(RefusedNext))]
    public void Start_ShouldRefuse_WhenTheNextIsNotABoundedSameSitePath(string next) =>
        _start.TestValidate(new StartExternalLoginCommand("google", next)).ShouldHaveValidationErrorFor(c => c.Next);

    [Fact]
    public void Start_ShouldAdmit_WhenTheNextIsExactlyTheBound() =>
        _start.TestValidate(new StartExternalLoginCommand("google", "/" + new string('a', ExternalLoginPolicy.MaxNextLength - 1)))
            .ShouldNotHaveAnyValidationErrors();

    public static TheoryData<string> RefusedProvider => new() { "", new string('g', ExternalProviderKey.MaximumLength + 1) };

    [Theory]
    [MemberData(nameof(RefusedProvider))]
    public void Both_ShouldRefuse_WhenTheProviderIsMissingOrLongerThanAnyKey(string provider)
    {
        _start.TestValidate(new StartExternalLoginCommand(provider, null)).ShouldHaveValidationErrorFor(c => c.Provider);
        _complete.TestValidate(new CompleteExternalLoginCommand(provider, "4/0AVGzR1code", OAuthState.Generate().Reveal()))
            .ShouldHaveValidationErrorFor(c => c.Provider);
    }

    [Fact]
    public void Both_ShouldAdmit_WhenTheProviderIsExactlyTheBound()
    {
        var provider = new string('g', ExternalProviderKey.MaximumLength);

        _start.TestValidate(new StartExternalLoginCommand(provider, null)).ShouldNotHaveValidationErrorFor(c => c.Provider);
        _complete.TestValidate(new CompleteExternalLoginCommand(provider, "4/0AVGzR1code", OAuthState.Generate().Reveal()))
            .ShouldNotHaveValidationErrorFor(c => c.Provider);
    }

    [Fact]
    public void Complete_ShouldRefuse_WhenTheCodeIsMissing() =>
        _complete.TestValidate(new CompleteExternalLoginCommand("google", "", OAuthState.Generate().Reveal()))
            .ShouldHaveValidationErrorFor(c => c.Code);

    [Fact]
    public void Complete_ShouldAdmit_WhenTheStateHasTheMintedShape() =>
        _complete.TestValidate(new CompleteExternalLoginCommand("google", "4/0AVGzR1code", OAuthState.Generate().Reveal()))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Complete_ShouldRefuse_WhenTheStateIsNotTheMintedShape(string state) =>
        _complete.TestValidate(new CompleteExternalLoginCommand("google", "4/0AVGzR1code", state))
            .ShouldHaveValidationErrorFor(c => c.State);

    [Fact]
    public void Complete_ShouldRefuse_WhenTheCodeIsLongerThanTheBound() =>
        _complete.TestValidate(new CompleteExternalLoginCommand(
                "google", new string('a', ExternalLoginPolicy.MaxCodeLength + 1), OAuthState.Generate().Reveal()))
            .ShouldHaveValidationErrorFor(c => c.Code);
}
