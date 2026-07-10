using Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #733 — UNIT cover for the resend-confirmation handler (inline Api-side mint+send; CTO 2026-07-10 /
/// ADR 0102). Pins the anti-enumeration invariants: uniform Result.Success (202) across cooled /
/// not-eligible / eligible AND across a send FAILURE (a transport fault must not surface as a differential
/// 500 that a non-existent address never sees); the cooldown is attempted for EVERY request before
/// eligibility; a send + audit happen ONLY for an eligible account; the audit is written ONLY after a link
/// was actually sent; and a cancellation propagates (never swallowed).
/// </summary>
public class ResendEmailConfirmationCommandHandlerTests
{
    private const string Email = "klas@example.com";

    private readonly IResendCooldown _cooldown = Substitute.For<IResendCooldown>();
    private readonly IUserAccountService _userAccountService = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IAuthAuditLogger _auditLogger = Substitute.For<IAuthAuditLogger>();

    private ResendEmailConfirmationCommandHandler CreateHandler() =>
        new(_cooldown, _userAccountService, _emailSender, _auditLogger,
            NullLogger<ResendEmailConfirmationCommandHandler>.Instance);

    private ValueTask<Result> Handle() =>
        CreateHandler().Handle(new ResendEmailConfirmationCommand(Email), CancellationToken.None);

    private void NotCooled() =>
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

    private void Cooled() =>
        _cooldown.TryBeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

    private void Eligible(Guid userId, string token = "url-safe-token") =>
        _userAccountService
            .TryPrepareEmailConfirmationResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmailConfirmationResend(userId, Email, token));

    private void NotEligible() =>
        _userAccountService
            .TryPrepareEmailConfirmationResendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((EmailConfirmationResend?)null);

    private void SendThrows(Exception ex) =>
        _emailSender.SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
                Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);

    [Fact]
    public async Task Handle_Eligible_SendsConfirmationThenAudits()
    {
        var userId = Guid.NewGuid();
        NotCooled();
        Eligible(userId);

        (await Handle()).IsSuccess.ShouldBeTrue();

        await _emailSender.Received(1).SendEmailConfirmationAsync(
            Email,
            Arg.Is<EmailConfirmationEmail>(c => c.UserId == userId && c.UrlSafeToken == "url-safe-token"),
            Arg.Any<EmailConfirmationIdempotencyKey>(),
            Arg.Any<CancellationToken>());
        _auditLogger.Received(1).EmailConfirmationResent(userId);
    }

    [Fact]
    public async Task Handle_NotEligible_ReturnsSuccess_NoSendNoAudit()
    {
        // null covers flag-OFF / non-existent / already-confirmed — all indistinguishable to the handler.
        NotCooled();
        NotEligible();

        (await Handle()).IsSuccess.ShouldBeTrue();

        await _emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
        _auditLogger.DidNotReceive().EmailConfirmationResent(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_WhenCooled_ReturnsSuccess_ShortCircuitsBeforeEligibility()
    {
        Cooled();

        (await Handle()).IsSuccess.ShouldBeTrue();

        await _userAccountService.DidNotReceive().TryPrepareEmailConfirmationResendAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
        _auditLogger.DidNotReceive().EmailConfirmationResent(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_AlwaysBeginsCooldownOnce_ForEligibleAndNonEligibleAlike()
    {
        // The cooldown runs before the existence check for both an eligible and a non-eligible address, so
        // cooldown state never correlates with account existence (CTO-bind FORK 1).
        NotCooled();
        Eligible(Guid.NewGuid());
        await Handle();
        await _cooldown.Received(1).TryBeginAsync(Email, Arg.Any<CancellationToken>());

        _cooldown.ClearReceivedCalls();
        NotEligible();
        await Handle();
        await _cooldown.Received(1).TryBeginAsync(Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSendThrows_ReturnsUniformSuccess_AndDoesNotAudit()
    {
        // Anti-enumeration under failure: a transport fault for an eligible account must yield the SAME
        // uniform 202 a non-existent address gets — never a differential 500. And no audit-log line is written
        // (EmailConfirmationResent means "a link was actually sent").
        NotCooled();
        Eligible(Guid.NewGuid());
        SendThrows(new InvalidOperationException("resend transport down"));

        (await Handle()).IsSuccess.ShouldBeTrue();

        _auditLogger.DidNotReceive().EmailConfirmationResent(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_WhenSendCancelled_Propagates()
    {
        // Cancellation is NOT swallowed by the uniform-202 catch (the `is not OperationCanceledException`
        // filter): host shutdown / client abort surfaces normally.
        NotCooled();
        Eligible(Guid.NewGuid());
        SendThrows(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Handle().AsTask());
    }
}
