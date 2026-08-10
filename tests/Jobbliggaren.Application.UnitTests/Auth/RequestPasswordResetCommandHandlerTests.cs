using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1171 — the forgot-password request handler. What these pin, beyond the happy path, is that the
/// anti-enumeration guarantee is a property of the CODE rather than of the comments beside it.
/// <para>
/// The load-bearing one is <see cref="Handle_when_the_sender_cannot_deliver_reads_no_input_at_all"/>.
/// The handler is allowed to answer 503 on an unauthenticated surface only because that answer is
/// decided before the submitted address is looked at; move the capability check below the account
/// lookup and the 503 becomes reachable only for existing accounts, i.e. an existence oracle. A test
/// asserting merely that the failure is returned would stay green through exactly that regression.
/// </para>
/// </summary>
public sealed class RequestPasswordResetCommandHandlerTests
{
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ICooldownGate _cooldown = Substitute.For<ICooldownGate>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();

    private const string KnownEmail = "known@example.se";
    private const string UnknownEmail = "unknown@example.se";
    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private RequestPasswordResetCommandHandler CreateSut()
    {
        var options = Options.Create(new AuthEmailCooldownOptions { PasswordResetWindowSeconds = 60 });
        return new RequestPasswordResetCommandHandler(
            _emailSender, _cooldown, options, _accounts, _audit,
            NullLogger<RequestPasswordResetCommandHandler>.Instance);
    }

    private void ArrangeDeliverableAndNotCooled()
    {
        _emailSender.CanDeliver.Returns(true);
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
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
        // THE ordering pin, and the reason this file exists. The refusal must be decided from the
        // server's configuration alone — so neither the cooldown (which is keyed on the address) nor
        // the account lookup may run first. If either does, a 503 can only be produced for an address
        // that got that far, and the status becomes an existence oracle on a public endpoint.
        _emailSender.CanDeliver.Returns(false);

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        await _cooldown.DidNotReceiveWithAnyArgs()
            .TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().TryPreparePasswordResetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceiveWithAnyArgs()
            .SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_when_the_sender_cannot_deliver_answers_identically_for_known_and_unknown()
    {
        // The refusal must not vary with the address even in its payload. Only meaningful together with
        // the test above: that one pins that no lookup runs, this one pins that the ANSWER is uniform.
        _emailSender.CanDeliver.Returns(false);
        var sut = CreateSut();
        var ct = TestContext.Current.CancellationToken;

        var known = await sut.Handle(new RequestPasswordResetCommand(KnownEmail), ct);
        var unknown = await sut.Handle(new RequestPasswordResetCommand(UnknownEmail), ct);

        known.Error.Code.ShouldBe(unknown.Error.Code);
        known.Error.Message.ShouldBe(unknown.Error.Message);
    }

    [Fact]
    public async Task Handle_checks_the_cooldown_before_the_account_lookup()
    {
        // Cooldown state in Redis is keyed on the address; if the lookup ran first, the window would be
        // begun only for addresses that reached it and the cooldown itself would encode existence.
        ArrangeDeliverableAndNotCooled();
        _accounts.TryPreparePasswordResetAsync(KnownEmail, Arg.Any<CancellationToken>())
            .Returns((PasswordResetDelivery?)null);

        await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _cooldown.TryBeginAsync(
                CooldownScopes.PasswordReset, KnownEmail, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
            _accounts.TryPreparePasswordResetAsync(KnownEmail, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_within_the_cooldown_succeeds_silently_and_sends_nothing()
    {
        // Silent, never a 409 or 429: a visible throttle would answer differently for an address someone
        // had recently requested, which is an oracle assembled out of the anti-abuse control.
        _emailSender.CanDeliver.Returns(true);
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _accounts.DidNotReceiveWithAnyArgs().TryPreparePasswordResetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceiveWithAnyArgs()
            .SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_for_an_unknown_address_succeeds_and_writes_no_audit_line()
    {
        // The audit half of the same guarantee: a row for existing addresses and none for unknown ones
        // would make audit_log an existence oracle for anyone who can read it.
        ArrangeDeliverableAndNotCooled();
        _accounts.TryPreparePasswordResetAsync(UnknownEmail, Arg.Any<CancellationToken>())
            .Returns((PasswordResetDelivery?)null);

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(UnknownEmail), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.DidNotReceiveWithAnyArgs()
            .SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceiveWithAnyArgs().PasswordResetRequested(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_for_a_known_address_sends_to_the_stored_address_and_audits_once()
    {
        // The send goes to the address the SERVICE returned, not the one submitted: Identity's lookup is
        // case-insensitive, so echoing the request's spelling would mail a form the account does not have.
        ArrangeDeliverableAndNotCooled();
        _accounts.TryPreparePasswordResetAsync("KNOWN@example.se", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetDelivery(UserId, KnownEmail, "url-safe-token"));

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand("KNOWN@example.se"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendPasswordResetAsync(
            KnownEmail,
            Arg.Is<PasswordResetEmail>(e => e.UserId == UserId && e.UrlSafeToken == "url-safe-token"),
            Arg.Any<CancellationToken>());
        _audit.Received(1).PasswordResetRequested(UserId);
    }

    [Fact]
    public async Task Handle_when_the_send_throws_still_succeeds_and_writes_no_audit_line()
    {
        // A transport fault for an existing account must not surface as a differential 500 that an
        // unknown address (a clean success) never produces — that re-opens the oracle from the other side.
        ArrangeDeliverableAndNotCooled();
        _accounts.TryPreparePasswordResetAsync(KnownEmail, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetDelivery(UserId, KnownEmail, "url-safe-token"));
        _emailSender.SendPasswordResetAsync(
                Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("transport"));

        var result = await CreateSut().Handle(
            new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _audit.DidNotReceiveWithAnyArgs().PasswordResetRequested(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_does_not_swallow_cancellation()
    {
        // The catch that keeps the response uniform must not also swallow a host shutdown as one user's
        // send failure — the `when (ex is not OperationCanceledException)` filter is what separates them.
        ArrangeDeliverableAndNotCooled();
        _accounts.TryPreparePasswordResetAsync(KnownEmail, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetDelivery(UserId, KnownEmail, "url-safe-token"));
        _emailSender.SendPasswordResetAsync(
                Arg.Any<string>(), Arg.Any<PasswordResetEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await CreateSut().Handle(
                new RequestPasswordResetCommand(KnownEmail), TestContext.Current.CancellationToken));
    }
}
