using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1171 (dotnet-architect 2026-08-10) — the shutdown drain, pinned against a sender that HONOURS its
/// cancellation token.
/// <para>
/// <b>This test exists because the bug it pins was invisible in every environment we run tests in.</b>
/// The override completes the writer first, and only then calls <c>base.StopAsync</c> — which cancels its
/// own token source BEFORE awaiting the execute task. The cancellation therefore lands while the drain is
/// still running. An earlier version
/// passed that token down into the per-item work; <c>SesEmailSender</c> awaits the SDK call with it, and
/// both catch filters exclude <c>OperationCanceledException</c> — so the OCE unwound straight out of the
/// loop and took the rest of the queue with it.
/// </para>
/// <para>
/// It failed ONLY under <c>Email:Provider=Ses</c>. <c>NullEmailSender</c> and <c>ConsoleEmailSender</c>
/// ignore the token entirely, so the drain looked healthy in Development and in the Testcontainers
/// suites while production dropped everything queued. A fake that ignores cancellation reproduces that
/// blindness exactly, which is why the one below observes the token instead.
/// </para>
/// </summary>
public sealed class PasswordResetDispatchServiceShutdownTests
{
    /// <summary>
    /// An <see cref="IEmailSender"/> that behaves like the real SES adapter with respect to
    /// cancellation: it AWAITS on the token it is given, and it lets an
    /// <see cref="OperationCanceledException"/> escape rather than swallowing it.
    /// </summary>
    private sealed class TokenHonouringSender : IEmailSender
    {
        public List<string> Sent { get; } = [];

        public bool CanDeliver => true;

        public async Task SendPasswordResetAsync(
            string toEmail, PasswordResetEmail content, CancellationToken cancellationToken)
        {
            // The awaited call the real adapter makes. If the consumer hands down a cancelled token,
            // this throws before recording anything — which is the defect.
            await Task.Delay(20, cancellationToken);
            lock (Sent) Sent.Add(toEmail);
        }

        public Task SendMatchNotificationEmailAsync(string t, MatchNotificationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendFollowedCompanyNotificationEmailAsync(string t, FollowedCompanyNotificationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailChangeConfirmationAsync(string t, EmailChangeConfirmationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailChangedNotificationAsync(string t, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailConfirmationAsync(string t, EmailConfirmationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendAccountExistsNoticeAsync(string t, CancellationToken ct) => Task.CompletedTask;
        public Task SendPasswordChangedNoticeAsync(string t, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task StopAsync_drains_the_queue_even_when_the_sender_honours_cancellation()
    {
        var ct = TestContext.Current.CancellationToken;

        var accounts = Substitute.For<IUserAccountService>();
        accounts.TryPreparePasswordResetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<PasswordResetDelivery?>(
                new PasswordResetDelivery(Guid.NewGuid(), callInfo.ArgAt<string>(0), "tok")));

        var sender = new TokenHonouringSender();

        var services = new ServiceCollection();
        services.AddSingleton(accounts);
        services.AddSingleton<IEmailSender>(sender);
        services.AddSingleton(Substitute.For<IAuthAuditLogger>());
        await using var provider = services.BuildServiceProvider();

        var channel = new PasswordResetDispatchChannel(
            Options.Create(new PasswordResetDispatchOptions { Capacity = 16 }),
            NullLogger<PasswordResetDispatchChannel>.Instance);

        var sut = new PasswordResetDispatchService(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PasswordResetDispatchService>.Instance);

        // Queue more than one item, so a drain that dies on the FIRST awaited send is distinguishable
        // from one that completes: the defect delivered zero, not two of three.
        channel.TryEnqueue(new PasswordResetDispatch("a@example.se", null, null));
        channel.TryEnqueue(new PasswordResetDispatch("b@example.se", null, null));
        channel.TryEnqueue(new PasswordResetDispatch("c@example.se", null, null));

        await sut.StartAsync(ct);
        await sut.StopAsync(ct);

        // THE threshold. Under the previous shape this was zero: StopAsync cancelled the stopping token
        // before the loop had processed anything, and the first Task.Delay(_, token) threw straight
        // through both catch filters.
        sender.Sent.Count.ShouldBe(
            3,
            "StopAsync completes the writer to END the loop; it must not also cancel the work the loop "
            + "is draining, or shutdown discards exactly what the drain exists to deliver");
        sender.Sent.ShouldBe(["a@example.se", "b@example.se", "c@example.se"], ignoreOrder: false);
    }

    [Fact]
    public async Task The_sender_fake_really_does_honour_cancellation()
    {
        // The counterfactual for the fake itself. Without it the test above is satisfied by a fake that
        // ignores its token — which is precisely the blindness that let the real defect ship, since
        // NullEmailSender and ConsoleEmailSender both ignore theirs.
        var sender = new TokenHonouringSender();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await sender.SendPasswordResetAsync(
                "x@example.se", new PasswordResetEmail(Guid.NewGuid(), "tok"), cts.Token));

        sender.Sent.ShouldBeEmpty();
    }
}
