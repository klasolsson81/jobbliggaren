using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Resend;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0080 Vag 4 PR-4a/4b — locks <see cref="ResendEmailSender"/>'s message composition + PII
/// discipline against a faked <see cref="IResend"/> (NSubstitute). Not RED-first; the impl
/// already compiles. The invariants pinned:
///   - From is composed from <see cref="EmailOptions"/> ("{FromName} &lt;{FromAddress}&gt;");
///   - the recipient is added to the message;
///   - Subject/TextBody come from the RIGHT template per method;
///   - the SDK send is called EXACTLY once;
///   - the match-notification path uses the IDEMPOTENCY overload
///     (<see cref="IResend.EmailSendAsync(string, EmailMessage, System.Threading.CancellationToken)"/>)
///     and forwards the key verbatim (#187); invitation/waitlist stay on the plain overload;
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

    // The EmailMessage is argument 0 of the plain overload and argument 1 of the idempotency
    // overload — selecting it by type keeps this helper overload-agnostic.
    private EmailMessage CapturedMessage()
    {
        var calls = _resend.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IResend.EmailSendAsync))
            .ToList();
        calls.Count.ShouldBe(1);
        return calls[0].GetArguments().OfType<EmailMessage>().Single();
    }

    private static MatchNotificationEmail SampleMatchContent() =>
        new(
            MatchNotificationKind.Direct,
            Cadence: null,
            Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
            TotalCount: 1);

    private static MatchNotificationIdempotencyKey SampleKey() =>
        MatchNotificationIdempotencyKey.ForDirect(Guid.NewGuid(), Guid.NewGuid());

    // --- Invitation (plain overload — no idempotency key) ---

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

    // --- Waitlist confirmation (plain overload) ---

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

    // --- Match notification (idempotency overload) ---

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldComposeMatchNotificationSubjectAndBody_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), SampleKey(), CancellationToken.None);

        var message = CapturedMessage();
        message.From.ShouldBe("Jobbliggaren <no-reply@jobbliggaren.se>");
        message.To.ShouldContain(Recipient);
        message.Subject.ShouldBe("Ny toppmatchning på Jobbliggaren");
        // Body comes from the MatchNotification template → carries the mandatory settings link.
        message.TextBody.ShouldNotBeNull().ShouldContain($"{_options.BaseUrl}/installningar");
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldUseIdempotencyOverload_AndForwardKeyVerbatim()
    {
        var sut = CreateSut();
        var key = SampleKey();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), key, CancellationToken.None);

        // The match path MUST use the idempotency overload (string, EmailMessage, ct) with the key's
        // wire value — never the plain overload (which would not dedupe a transport retry, #187).
        await _resend.Received(1).EmailSendAsync(
            key.Value, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _resend.DidNotReceive().EmailSendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldThrow_WhenIdempotencyKeyIsDefault()
    {
        // A default-constructed key (record-struct default → Value is null) must fail loud rather
        // than silently fall back to the non-idempotent overload (#187). Neither SDK overload is hit.
        var sut = CreateSut();

        var act = async () => await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), default, CancellationToken.None);

        await act.ShouldThrowAsync<ArgumentException>();
        await _resend.DidNotReceiveWithAnyArgs().EmailSendAsync(
            Arg.Any<string>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _resend.DidNotReceiveWithAnyArgs().EmailSendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldNotPlaceRecipientInBodyOrSubject_WhenInvoked()
    {
        var sut = CreateSut();

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), SampleKey(), CancellationToken.None);

        var message = CapturedMessage();
        message.TextBody.ShouldNotBeNull().ShouldNotContain(Recipient);
        message.Subject.ShouldNotContain(Recipient);
    }

    // --- Failure path: rethrow + no PII fan-out ---

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldRethrow_WhenResendThrows()
    {
        _resend
            .EmailSendAsync(Arg.Any<string>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));
        var sut = CreateSut();

        var act = async () => await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), SampleKey(), CancellationToken.None);

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendMatchNotificationEmailAsync_ShouldCallResendExactlyOnce_WhenResendThrows()
    {
        // No retry loop that could re-emit the recipient/body to the SDK on failure.
        _resend
            .EmailSendAsync(Arg.Any<string>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));
        var sut = CreateSut();

        try
        {
            await sut.SendMatchNotificationEmailAsync(
                Recipient, SampleMatchContent(), SampleKey(), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // expected — assertion below proves single attempt
        }

        await _resend.Received(1).EmailSendAsync(
            Arg.Any<string>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
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

        await sut.SendMatchNotificationEmailAsync(
            Recipient, SampleMatchContent(), SampleKey(), cts.Token);

        await _resend.Received(1).EmailSendAsync(
            Arg.Any<string>(), Arg.Any<EmailMessage>(), cts.Token);
    }
}
