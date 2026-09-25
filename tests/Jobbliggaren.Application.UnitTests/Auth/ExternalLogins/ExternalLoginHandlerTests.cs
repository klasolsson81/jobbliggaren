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
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static IExternalIdentityProvider Google()
    {
        var provider = Substitute.For<IExternalIdentityProvider>();
        provider.Key.Returns(ExternalProviderKey.Google);
        return provider;
    }

    [Fact]
    public void Keys_ShouldBeEmpty_WhenNoProviderIsRegistered() => new RegisteredProviders([]).Keys.ShouldBeEmpty();

    [Fact]
    public void Keys_ShouldNameGoogle_WhenGoogleIsRegistered() =>
        new RegisteredProviders([Google()]).Keys.ShouldBe([ExternalProviderKey.Google]);

    [Theory]
    [InlineData("github")]
    [InlineData("GOOGLE")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_ShouldAnswerNull_WhenTheKeyIsNotAKnownKeySpelledExactly(string? raw) =>
        new RegisteredProviders([Google()]).Find(raw).ShouldBeNull();

    [Fact]
    public void Find_ShouldAnswerNull_WhenTheKeyIsKnownButNotRegisteredOnThisHost() =>
        new RegisteredProviders([]).Find("google").ShouldBeNull();
}

/// <summary>#1744 — the providers list: the registered keys, empty where no keys are set.</summary>
public class GetExternalLoginProvidersQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldAnswerAnEmptyList_WhenNoProviderIsRegistered() =>
        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders([]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();

    [Fact]
    public async Task Handle_ShouldAnswerGoogle_WhenGoogleIsRegistered()
    {
        var google = Substitute.For<IExternalIdentityProvider>();
        google.Key.Returns(ExternalProviderKey.Google);

        (await new GetExternalLoginProvidersQueryHandler(new RegisteredProviders([google]))
            .Handle(new GetExternalLoginProvidersQuery(), TestContext.Current.CancellationToken)).ShouldBe(["google"]);
    }
}

/// <summary>#1744 — start: the flow is minted and the provider's URL carries the stored verifier's challenge.</summary>
public class StartExternalLoginCommandHandlerTests
{
    private readonly IOAuthStateStore _states = Substitute.For<IOAuthStateStore>();
    private readonly IExternalIdentityProvider _google = Substitute.For<IExternalIdentityProvider>();
    private readonly OAuthState _state = OAuthState.Generate();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public StartExternalLoginCommandHandlerTests()
    {
        _google.Key.Returns(ExternalProviderKey.Google);
        _states.PutAsync(Arg.Any<OAuthFlow>(), Arg.Any<CancellationToken>()).Returns(_state);
        _google.BuildAuthorizeUrl(Arg.Any<OAuthState>(), Arg.Any<PkceChallenge>())
            .Returns(new Uri("https://accounts.google.com/o/oauth2/v2/auth?scripted"));
    }

    private StartExternalLoginCommandHandler Handler(params IExternalIdentityProvider[] providers) =>
        new(new RegisteredProviders(providers), _states);

    [Fact]
    public async Task Handle_ShouldBeNotFoundAndMintNothing_WhenTheProviderIsNotRegistered()
    {
        var result = await Handler().Handle(new StartExternalLoginCommand("google", "/oversikt"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalProviderUnknown);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await _states.DidNotReceiveWithAnyArgs().PutAsync(default!, Ct);
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
    public async Task Handle_ShouldCarryAnEmptyPath_WhenTheWebSendsNone()
    {
        await Handler(_google).Handle(new StartExternalLoginCommand("google", null), Ct);

        ((OAuthFlow)_states.ReceivedCalls().Single().GetArguments()[0]!).Next.ShouldBe(string.Empty);
    }
}

/// <summary>
/// #1744 — the callback's order: registered provider, then the flow taken for THAT provider, then the exchange, and
/// only a verified identity reaches the outcome. Identities come from the production adapter (GoogleIdentities).
/// </summary>
public class CompleteExternalLoginCommandHandlerTests
{
    private readonly IOAuthStateStore _states = Substitute.For<IOAuthStateStore>();
    private readonly IExternalIdentityProvider _google = Substitute.For<IExternalIdentityProvider>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly ILoginAccountLookup _lookup = Substitute.For<ILoginAccountLookup>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly CapturingLogger<CompleteExternalLoginCommandHandler> _log = new();
    private readonly OAuthState _state = OAuthState.Generate();
    private readonly PkceVerifier _verifier = PkceVerifier.Generate();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CompleteExternalLoginCommandHandlerTests()
    {
        _google.Key.Returns(ExternalProviderKey.Google);
        _states.TakeAsync(_state, ExternalProviderKey.Google, Arg.Any<CancellationToken>())
            .Returns(new OAuthFlow(ExternalProviderKey.Google, _verifier, "/ansokningar/abc"));
        _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>())
            .Returns(GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw"));
    }

    private CompleteExternalLoginCommandHandler Handler(params IExternalIdentityProvider[] providers)
    {
        var correlation = Substitute.For<ICorrelationIdProvider>();
        var request = Substitute.For<IRequestContextProvider>();
        var outcome = new LoginProofOutcome(
            new LoginSubjectResolver(_lookup, Substitute.For<IExternalLoginLookup>(), _db),
            new PasswordlessSessionGrant(
                Substitute.For<IInboxProofRecorder>(), _sessions, Substitute.For<IAuthAuditLogger>(), _db,
                FakeDateTimeProvider.Default, correlation, request),
            _grants,
            new ExternalLoginLinker(
                Substitute.For<IExternalLoginWriter>(), _db, FakeDateTimeProvider.Default, correlation, request),
            Options.Create(new AuthOptions { RegistrationsOpen = true }),
            NullLogger<LoginProofOutcome>.Instance);
        return new CompleteExternalLoginCommandHandler(new RegisteredProviders(providers), _states, outcome, _log);
    }

    private CompleteExternalLoginCommand Command() => new("google", "4/0AVGzR1code", _state.Reveal());

    private async Task GoogleAnswersAsync(string userInfoJson) =>
        _google.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>())
            .Returns(await GoogleIdentities.ReadAsync(userInfoJson));

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
    public async Task Handle_ShouldBeTheSameGone_WhenTheProviderRefusesTheCode()
    {
        _google.ExchangeAsync(Arg.Any<AuthorizationCode>(), _verifier, Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var result = await Handler(_google).Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ExternalLoginUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }

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
        result.Value.Next.ShouldBe("/ansokningar/abc");
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
        "/" + new string('a', ExternalLoginPolicy.MaxNextLength),
    };

    [Theory]
    [MemberData(nameof(RefusedNext))]
    public void Start_ShouldRefuse_WhenTheNextIsNotABoundedSameSitePath(string next) =>
        _start.TestValidate(new StartExternalLoginCommand("google", next)).ShouldHaveValidationErrorFor(c => c.Next);

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
