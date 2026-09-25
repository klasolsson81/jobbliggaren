using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The shutdown drain of <c>BoundedDispatchService&lt;T&gt;</c>, pinned through the login-challenge
/// dispatcher against a sender that HONOURS its cancellation token.
/// <para>
/// The defect this pins was invisible in every environment tests run in. The base's
/// <c>StopAsync</c> completes the writer first, and only then does <c>BackgroundService.StopAsync</c> cancel
/// its token source, BEFORE it awaits the execute task, so the cancellation lands while the drain is still
/// running. A drain that passed that token down unwound on the first awaited send under
/// <c>Email:Provider=Scaleway</c> (HttpClient honours it) and nowhere else, because <c>NullEmailSender</c>
/// and <c>ConsoleEmailSender</c> ignore the token. A fake that ignores cancellation reproduces that
/// blindness, which is why the one below observes it.
/// </para>
/// </summary>
public sealed class LoginChallengeDispatchServiceShutdownTests
{
    /// <summary>
    /// Behaves like the transactional adapter with respect to the CALLER's token: it awaits on it and lets
    /// the <see cref="OperationCanceledException"/> escape. <c>ScalewayEmailSenderTests</c> owns the other
    /// branch (a provider timeout is contained as a send failure).
    /// </summary>
    private sealed class TokenHonouringSender : IEmailSender
    {
        public List<string> Sent { get; } = [];

        /// <summary>
        /// Set when the drain has STARTED, not when the first send finished: the latter would be satisfied
        /// by a loop that then dies. <c>RunContinuationsAsynchronously</c> keeps the waiting test's
        /// continuation from running inline on the pool thread inside the send and reordering the sequence.
        /// </summary>
        public TaskCompletionSource FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanDeliver => true;

        public async Task SendLoginChallengeAsync(
            string toEmail, LoginChallengeEmail content, CancellationToken cancellationToken)
        {
            FirstSendStarted.TrySetResult();

            // The awaited call the real adapter makes. A drain that hands down a cancelled token throws here
            // before anything is recorded.
            await Task.Delay(20, cancellationToken);
            lock (Sent) Sent.Add(toEmail);
        }

        public Task SendMatchNotificationEmailAsync(string t, MatchNotificationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendFollowedCompanyNotificationEmailAsync(string t, FollowedCompanyNotificationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailChangedNotificationAsync(string t, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailConfirmationAsync(string t, EmailConfirmationEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendAccountExistsNoticeAsync(string t, CancellationToken ct) => Task.CompletedTask;
        public Task SendPasswordResetAsync(string t, PasswordResetEmail c, CancellationToken ct) => Task.CompletedTask;
        public Task SendPasswordChangedNoticeAsync(string t, CancellationToken ct) => Task.CompletedTask;
    }

    private static LoginChallengeDispatch Item(string email) =>
        new(ChallengeId.Generate(), email, CodeBudgetState.Admitted, "203.0.113.0", "probe/1.0");

    [Fact]
    public async Task StopAsync_drains_the_queue_even_when_the_sender_honours_cancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = new TokenHonouringSender();

        // The real issuer, as LoginChallengeDispatchServiceTests composes it: an address without an account
        // and default AuthOptions (registration closed) is issued a record and one login-challenge mail each.
        var lookup = Substitute.For<ILoginAccountLookup>();
        lookup.FindAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        var store = Substitute.For<ILoginChallengeStore>();
        store.PutAsync(Arg.Any<NewLoginChallenge>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedCredentials(null, null));
        var budget = Substitute.For<IRateBudget>();
        budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddScoped(_ => new LoginSubjectResolver(lookup, TestAppDbContextFactory.Create()));
        services.AddScoped(sp => new LoginChallengeIssuer(
            sp.GetRequiredService<LoginSubjectResolver>(), store, budget, sender,
            Substitute.For<IAuthAuditLogger>(),
            Options.Create(new AuthOptions()),
            NullLogger<LoginChallengeIssuer>.Instance));
        await using var provider = services.BuildServiceProvider();

        var channel = new LoginChallengeDispatchChannel(
            Options.Create(new LoginChallengeDispatchOptions { Capacity = 16 }),
            NullLogger<LoginChallengeDispatchChannel>.Instance);
        var sut = new LoginChallengeDispatchService(
            channel, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LoginChallengeDispatchService>.Instance);

        // Three items, so a drain that dies on the FIRST awaited send (zero delivered) is distinguishable from
        // one that completes.
        channel.Enqueue(Item("a@example.se"));
        channel.Enqueue(Item("b@example.se"));
        channel.Enqueue(Item("c@example.se"));

        await sut.StartAsync(ct);

        // Stop only once the drain has started. .NET 10's BackgroundService.StartAsync is
        // `Task.Run(() => ExecuteAsync(token), token)` (dotnet/runtime #116283): a Cancel() that beats the
        // pool's dequeue means the delegate never runs, and the test would measure that race instead.
        await sender.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // Bounded, so a drain that hangs is red rather than a suite timeout.
        await sut.StopAsync(ct).WaitAsync(TimeSpan.FromSeconds(30), ct);

        sender.Sent.ShouldBe(
            ["a@example.se", "b@example.se", "c@example.se"],
            ignoreOrder: false,
            "StopAsync completes the writer to END the loop; it must not also cancel the work the loop "
            + "is draining, or shutdown discards exactly what the drain exists to deliver");
    }

    [Fact]
    public async Task The_sender_fake_really_does_honour_cancellation()
    {
        // The counterfactual for the fake: without it the test above is satisfied by a fake that ignores its
        // token, which is the blindness that let the real defect ship.
        var sender = new TokenHonouringSender();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await sender.SendLoginChallengeAsync(
                "x@example.se", new LoginChallengeEmail.RegistrationClosed(), cts.Token));

        sender.Sent.ShouldBeEmpty();
    }
}
