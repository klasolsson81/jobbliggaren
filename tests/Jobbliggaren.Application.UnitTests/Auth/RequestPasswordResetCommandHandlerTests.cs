using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1171 — the forgot-password request handler. What these pin, beyond the happy path, is that the
/// anti-enumeration guarantee is a property of the CODE rather than of the comments beside it.
/// <para>
/// The handler's whole job is to do NOTHING that depends on whether the address resolves. Two
/// properties carry that, and each has its own test because they fail independently:
/// </para>
/// <list type="number">
/// <item><b>The capability check is first.</b> The endpoint may answer 503 on an unauthenticated
/// surface only because that answer is decided before the address is read; move the check below
/// anything that consults the account and the 503 becomes reachable only for existing accounts. A test
/// asserting merely that the failure is returned stays green through exactly that regression, so
/// <see cref="Handle_when_the_sender_cannot_deliver_reads_no_input_at_all"/> asserts the collaborators
/// received NOTHING.</item>
/// <item><b>No lookup, mint or send happens on the request path at all</b> (senior-cto-advisor
/// 2026-08-10). Those moved behind <see cref="IPasswordResetDispatcher"/> because an inline provider
/// round trip cost time only when the account existed — a single-sample-classifiable difference that
/// the per-address cooldown does not cap, since enumeration needs one measurement per candidate rather
/// than many per address. <see cref="Handle_never_touches_the_account_on_the_request_path"/> is the pin
/// for that, and it is the one that would go red if anyone "simplified" the queue away.</item>
/// </list>
/// </summary>
public sealed class RequestPasswordResetCommandHandlerTests
{
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ICooldownGate _cooldown = Substitute.For<ICooldownGate>();
    private readonly IPasswordResetDispatcher _dispatcher = Substitute.For<IPasswordResetDispatcher>();
    private readonly IRequestContextProvider _requestContext =
        Substitute.For<IRequestContextProvider>();

    private const string KnownEmail = "known@example.se";
    private const string UnknownEmail = "unknown@example.se";

    private RequestPasswordResetCommandHandler CreateSut()
    {
        var options = Options.Create(new AuthEmailCooldownOptions { PasswordResetWindowSeconds = 60 });
        return new RequestPasswordResetCommandHandler(
            _emailSender, _cooldown, options, _dispatcher, _requestContext);
    }

    private void ArrangeDeliverableAndNotCooled()
    {
        _emailSender.CanDeliver.Returns(true);
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _dispatcher.TryEnqueue(Arg.Any<PasswordResetDispatch>()).Returns(true);
    }

    [Fact]
    public async Task Handle_when_the_sender_cannot_deliver_returns_EmailDeliveryUnavailable()
    {
        _emailSender.CanDeliver.Returns(false);

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
    }

    [Fact]
    public async Task Handle_when_the_sender_cannot_deliver_reads_no_input_at_all()
    {
        // THE ordering pin. The refusal must be decided from the server's configuration alone, so
        // neither the cooldown (keyed on the address) nor the dispatch may run first. If either does, a
        // 503 can only be produced for a request that got that far, and the status becomes an existence
        // oracle on a public endpoint.
        _emailSender.CanDeliver.Returns(false);

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        await _cooldown.DidNotReceiveWithAnyArgs()
            .TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        _dispatcher.DidNotReceiveWithAnyArgs().TryEnqueue(Arg.Any<PasswordResetDispatch>());
    }

    [Fact]
    public async Task Handle_when_the_sender_cannot_deliver_answers_identically_for_known_and_unknown()
    {
        // The refusal must not vary with the address even in its payload. Only meaningful together with
        // the test above: that one pins that nothing runs, this one pins that the ANSWER is uniform.
        _emailSender.CanDeliver.Returns(false);
        var sut = CreateSut();
        var ct = TestContext.Current.CancellationToken;

        var known = await sut.Handle(new RequestPasswordResetCommand(KnownEmail), ct);
        var unknown = await sut.Handle(new RequestPasswordResetCommand(UnknownEmail), ct);

        known.Error.Code.ShouldBe(unknown.Error.Code);
        known.Error.Message.ShouldBe(unknown.Error.Message);
    }

    [Fact]
    public async Task Handle_never_touches_the_account_on_the_request_path()
    {
        // The timing pin. The handler takes no IUserAccountService and no send path at all — the lookup,
        // the mint and the provider round trip live in the dispatch consumer — so this asserts the only
        // thing the request path may do with the address: hand it over unread.
        //
        // Asserted over the EMAIL SENDER because that is the collaborator an inline implementation would
        // have to reach for. A handler that regressed to sending inline would fail here even if it kept
        // the dispatcher call.
        ArrangeDeliverableAndNotCooled();

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        await _emailSender.DidNotReceiveWithAnyArgs()
            .SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>());
        _dispatcher.Received(1).TryEnqueue(Arg.Is<PasswordResetDispatch>(d => d.Email == KnownEmail));
    }

    [Fact]
    public async Task Handle_checks_the_cooldown_before_handing_off()
    {
        // Cooldown state in Redis is keyed on the address; enqueuing first would let a request that the
        // throttle should have swallowed still reach the consumer and send.
        ArrangeDeliverableAndNotCooled();

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _cooldown.TryBeginAsync(
                CooldownScopes.PasswordReset, KnownEmail, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
            _dispatcher.TryEnqueue(Arg.Any<PasswordResetDispatch>());
        });
    }

    [Fact]
    public async Task Handle_within_the_cooldown_succeeds_silently_and_hands_off_nothing()
    {
        // Silent, never a 409 or 429: a visible throttle would answer differently for an address someone
        // had recently requested, which is an oracle assembled out of the anti-abuse control.
        _emailSender.CanDeliver.Returns(true);
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DidNotReceiveWithAnyArgs().TryEnqueue(Arg.Any<PasswordResetDispatch>());
    }

    [Fact]
    public async Task Handle_carries_the_anonymised_client_context_so_the_audit_line_keeps_it()
    {
        // The consumer runs outside a request scope, where AuthAuditLogger's HttpContext read yields
        // nothing. Without this capture the IP and User-Agent on the auth event most closely tied to
        // account takeover would silently degrade to "unknown" — a regression invisible until someone
        // needed those fields (ADR 0024 D7).
        ArrangeDeliverableAndNotCooled();
        _requestContext.IpAddress.Returns("203.0.113.0");
        _requestContext.UserAgent.Returns("probe/1.0");

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        _dispatcher.Received(1).TryEnqueue(Arg.Is<PasswordResetDispatch>(
            d => d.IpAddress == "203.0.113.0" && d.UserAgent == "probe/1.0"));
    }

    [Fact]
    public async Task Handle_still_succeeds_when_the_dispatcher_refuses()
    {
        // The response may not vary with server load any more than with account existence: a caller
        // able to tell "accepted" from "refused" could saturate the queue and build a side channel out
        // of the difference. So the handler must not branch on the return at all.
        //
        // A refusal is the SHUTDOWN case in the shipped implementation — a saturated queue accepts and
        // then discards, and reports that through its own log rather than through this value (see
        // PasswordResetDispatchChannelTests). Stubbed false here because the handler must be correct
        // for any implementation of the port, not only for the one it ships with.
        ArrangeDeliverableAndNotCooled();
        _dispatcher.TryEnqueue(Arg.Any<PasswordResetDispatch>()).Returns(false);

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_answers_identically_for_known_and_unknown_on_the_happy_path()
    {
        // The counterfactual to the 503 parity test above, on the other branch: both addresses produce
        // the same success AND the same single hand-off, so nothing downstream of the response can vary.
        ArrangeDeliverableAndNotCooled();
        var sut = CreateSut();
        var ct = TestContext.Current.CancellationToken;

        var known = await sut.Handle(new RequestPasswordResetCommand(KnownEmail), ct);
        var unknown = await sut.Handle(new RequestPasswordResetCommand(UnknownEmail), ct);

        known.IsSuccess.ShouldBeTrue();
        unknown.IsSuccess.ShouldBeTrue();
        _dispatcher.Received(2).TryEnqueue(Arg.Any<PasswordResetDispatch>());
    }
}
