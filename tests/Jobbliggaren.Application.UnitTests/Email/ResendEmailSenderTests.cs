using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Resend;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0080 Vag 4 PR-4a — locks <see cref="ResendEmailSender"/>'s message composition + PII
/// discipline against a faked <see cref="IResend"/> (NSubstitute). Not RED-first; the impl
/// already compiles. The invariants pinned:
///   - From is composed from <see cref="EmailOptions"/> ("{FromName} &lt;{FromAddress}&gt;");
///   - the recipient is added to the message;
///   - Subject/TextBody come from the RIGHT template per method;
///   - <see cref="IResend.EmailSendAsync(EmailMessage, CancellationToken)"/> is called EXACTLY once;
///   - on an <see cref="IResend"/> throw the exception PROPAGATES (rethrow) and the recipient/body
///     never leak (the SDK is the only sink, and we assert it was hit exactly once with no retry
///     that could fan out PII).
///
/// The production code ignores the EmailSendAsync return value (it only awaits), so the success
/// path leaves the call unconfigured — NSubstitute returns a completed Task, which await handles.
/// This keeps the test decoupled from Resend's response-construction internals.
/// </summary>
public class ResendEmailSenderTests
{
    private readonly IResend _resend = Substitute.For<IResend>();
    private readonly ILogger<ResendEmailSender> _logger = Substitute.For<ILogger<ResendEmailSender>>();
    private readonly EmailOptions _options = new()
    {
        Provider = "Resend",
        ApiKey = "re_test_key",
        FromName = "Jobbliggaren",
        FromAddress = "no-reply@jobbliggaren.se",
        BaseUrl = "https://jobbliggaren.se",
    };

    private const string Recipient = "user@example.com";

    private ResendEmailSender CreateSut() =>
        new(_resend, Options.Create(_options), _logger);

    private EmailMessage CapturedMessage()
    {
        var calls = _resend.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IResend.EmailSendAsync))
            .ToList();
        calls.Count.ShouldBe(1);
        return (EmailMessage)calls[0].GetArguments()[0]!;
    }

    private static MatchNotificationEmail SampleMatchContent() =>
        new(
            MatchNotificationKind.Direct,
            Cadence: null,
            Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
            TotalCount: 1);

    // --- Invitation ---

    [Fact]
    public async Task SendInvitationEmailAsync_ShouldCallEmailSendAsyncOnce_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendInvitationEmailAsync(
            Recipient, "plaintext-token", DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);

        await _resend.Received(1).EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendInvitationEmailAsync_ShouldComposeFromAndRecipientAndInvitationSubject_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendInvitationEmailAsync(
            Recipient, "plaintext-token", DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);

        var message = CapturedMessage();
        message.From.ShouldBe("Jobbliggaren <no-reply@jobbliggaren.se>");
        message.To.ShouldContain(Recipient);
        message.Subject.ShouldBe("Inbjudan till Jobbliggaren");
        message.TextBody.ShouldNotBeNullOrWhiteSpace();
    }

    // --- Waitlist confirmation ---

    [Fact]
    public async Task SendWaitlistConfirmationAsync_ShouldComposeWaitlistSubject_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendWaitlistConfirmationAsync(Recipient, CancellationToken.None);

        var message = CapturedMessage();
        message.From.ShouldBe("Jobbliggaren <no-reply@jobbliggaren.se>");
        message.To.ShouldContain(Recipient);
        message.Subject.ShouldBe("Tack för din anmälan till Jobbliggaren");
    }

    // --- Match notification ---

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldComposeMatchNotificationSubjectAndBody_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), CancellationToken.None);

        var message = CapturedMessage();
        message.From.ShouldBe("Jobbliggaren <no-reply@jobbliggaren.se>");
        message.To.ShouldContain(Recipient);
        message.Subject.ShouldBe("Ny toppmatchning på Jobbliggaren");
        // Body comes from the MatchNotification template → carries the mandatory settings link.
        message.TextBody.ShouldNotBeNull().ShouldContain($"{_options.BaseUrl}/installningar");
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldCallEmailSendAsyncExactlyOnce_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), CancellationToken.None);

        await _resend.Received(1).EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldNotPlaceRecipientInBodyOrSubject_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), CancellationToken.None);

        var message = CapturedMessage();
        message.TextBody.ShouldNotBeNull().ShouldNotContain(Recipient);
        message.Subject.ShouldNotContain(Recipient);
    }

    // --- Failure path: rethrow + no PII fan-out ---

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldRethrow_WhenResendThrows()
    {
        _resend
            .EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));
        var sut = CreateSut();

        var act = async () => await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), CancellationToken.None);

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldCallResendExactlyOnce_WhenResendThrows()
    {
        // No retry loop that could re-emit the recipient/body to the SDK on failure.
        _resend
            .EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));
        var sut = CreateSut();

        try
        {
            await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // expected — assertion below proves single attempt
        }

        await _resend.Received(1).EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendInvitationEmailAsync_ShouldRethrow_WhenResendThrows()
    {
        _resend
            .EmailSendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));
        var sut = CreateSut();

        var act = async () => await sut.SendInvitationEmailAsync(
            Recipient, "token", DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    // --- CancellationToken is propagated to the SDK ---

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldForwardCancellationToken_WhenInvoked()
    {
        using var cts = new CancellationTokenSource();
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), cts.Token);

        await _resend.Received(1).EmailSendAsync(Arg.Any<EmailMessage>(), cts.Token);
    }
}
