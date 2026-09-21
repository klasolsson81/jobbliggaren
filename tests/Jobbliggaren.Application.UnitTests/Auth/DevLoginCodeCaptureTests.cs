using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// DEV-ONLY — the login-code capture (#1735, security-auditor Q15): reserved recipients only, the code only,
/// once, for at most the challenge's lifetime, and every mail forwarded unchanged. The mails are the
/// <see cref="LoginChallengeEmail"/> variants the issuer sends; the clock is the only time source.
/// </summary>
public sealed class DevLoginCodeCaptureTests
{
    private const string Reserved = "person@example.com";

    private readonly Clock _clock = new() { UtcNow = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero) };
    private readonly IEmailSender _inner = Substitute.For<IEmailSender>();
    private readonly DevLoginCodeCapture _capture;
    private readonly DevLoginCodeCapturingEmailSender _sender;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DevLoginCodeCaptureTests()
    {
        _capture = new DevLoginCodeCapture(_clock);
        _sender = new DevLoginCodeCapturingEmailSender(_inner, _capture);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private static LoginChallengeEmail.CodeAndLink WithCode(string code) =>
        new(LoginCode.FromRaw(code), LoginLinkToken.FromRaw("link-token-value"));

    [Fact]
    public async Task A_code_mailed_to_a_reserved_address_is_taken_once()
    {
        await _sender.SendLoginChallengeAsync(Reserved, WithCode("123456"), Ct);

        _capture.TakeCode(Reserved).ShouldBe("123456");
        _capture.TakeCode(Reserved).ShouldBeNull();
    }

    [Fact]
    public async Task A_code_mailed_to_a_real_domain_is_forwarded_and_never_held()
    {
        var mail = WithCode("123456");

        await _sender.SendLoginChallengeAsync("person@example.se", mail, Ct);

        await _inner.Received(1).SendLoginChallengeAsync("person@example.se", mail, Ct);
        _capture.TakeCode("person@example.se").ShouldBeNull();
    }

    [Fact]
    public async Task A_new_account_code_is_held_for_a_reserved_recipient_and_for_no_other()
    {
        // #1737 — this code leads to an account, so the gate is the same member and never a copy (ADR 0142 D10).
        var mail = new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("271828"));

        await _sender.SendLoginChallengeAsync(Reserved, mail, Ct);
        await _sender.SendLoginChallengeAsync("person@example.se", mail, Ct);

        await _inner.Received(1).SendLoginChallengeAsync(Reserved, mail, Ct);
        _capture.TakeCode(Reserved).ShouldBe("271828");
        _capture.TakeCode("person@example.se").ShouldBeNull();
    }

    [Fact]
    public async Task Mails_without_a_code_are_forwarded_and_hold_nothing()
    {
        LoginChallengeEmail[] mails =
        [
            new LoginChallengeEmail.LinkOnly(LoginLinkToken.FromRaw("link-token-value")),
            new LoginChallengeEmail.RegistrationClosed(),
            new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19)),
            new LoginChallengeEmail.NewAccountCodeLimitReached(),
        ];

        foreach (var mail in mails)
        {
            await _sender.SendLoginChallengeAsync(Reserved, mail, Ct);
            await _inner.Received(1).SendLoginChallengeAsync(Reserved, mail, Ct);
        }

        _capture.TakeCode(Reserved).ShouldBeNull();
    }

    [Fact]
    public async Task A_send_that_throws_holds_nothing()
    {
        _inner.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("login-challenge", nameof(HttpRequestException)));

        await Should.ThrowAsync<EmailDeliveryException>(
            () => _sender.SendLoginChallengeAsync(Reserved, WithCode("123456"), Ct));

        _capture.TakeCode(Reserved).ShouldBeNull();
    }

    [Fact]
    public async Task A_held_code_lapses_with_the_challenge()
    {
        await _sender.SendLoginChallengeAsync(Reserved, WithCode("123456"), Ct);

        _clock.UtcNow += LoginChallengePolicy.ChallengeTtl;

        _capture.TakeCode(Reserved).ShouldBeNull();
    }

    [Fact]
    public async Task A_newer_code_replaces_the_older_one()
    {
        await _sender.SendLoginChallengeAsync(Reserved, WithCode("111111"), Ct);
        await _sender.SendLoginChallengeAsync(Reserved, WithCode("222222"), Ct);

        _capture.TakeCode(Reserved).ShouldBe("222222");
    }

    [Fact]
    public async Task A_full_capture_refuses_new_addresses_until_old_codes_lapse()
    {
        for (var i = 0; i < DevLoginCodeCapture.Capacity; i++)
            await _sender.SendLoginChallengeAsync($"p{i}@example.com", WithCode("111111"), Ct);

        await _sender.SendLoginChallengeAsync("late@example.com", WithCode("222222"), Ct);
        _capture.TakeCode("late@example.com").ShouldBeNull();

        _clock.UtcNow += LoginChallengePolicy.ChallengeTtl;
        await _sender.SendLoginChallengeAsync("late@example.com", WithCode("333333"), Ct);
        _capture.TakeCode("late@example.com").ShouldBe("333333");
    }

    [Fact]
    public void Deliverability_is_the_wrapped_sender_s()
    {
        _inner.CanDeliver.Returns(false);
        _sender.CanDeliver.ShouldBeFalse();

        _inner.CanDeliver.Returns(true);
        _sender.CanDeliver.ShouldBeTrue();
    }
}
