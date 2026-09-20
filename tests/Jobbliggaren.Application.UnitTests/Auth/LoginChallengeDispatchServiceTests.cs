using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The login consumer's drain survives a failed item. With no break-glass, a consumer that died on one bad
/// item would stop every later login for the life of the process. The failure is the adapter's own: on a
/// Redis outage <c>VolatileRedisConnection.ExecuteAsync</c> turns the fault into
/// <see cref="VolatileRedisUnavailableException"/>, which RedisLoginChallengeStoreTests measures
/// against a stopped container.
/// </summary>
public sealed class LoginChallengeDispatchServiceTests
{
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly CapturingLogger<LoginChallengeDispatchService> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LoginChallengeDispatch Item(string email) =>
        new(ChallengeId.Generate(), email, CodeBudgetState.Admitted, "203.0.113.0", "probe/1.0");

    private static IRateBudget AdmittingBudget()
    {
        var budget = Substitute.For<IRateBudget>();
        budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return budget;
    }

    private async Task DrainAsync(params LoginChallengeDispatch[] items)
    {
        var lookup = Substitute.For<ILoginAccountLookup>();
        lookup.FindUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var services = new ServiceCollection();
        services.AddScoped(_ => new LoginSubjectResolver(lookup, TestAppDbContextFactory.Create()));
        services.AddScoped(sp => new LoginChallengeIssuer(
            sp.GetRequiredService<LoginSubjectResolver>(), _store, AdmittingBudget(), _sender,
            Substitute.For<IAuthAuditLogger>(),
            NullLogger<LoginChallengeIssuer>.Instance));
        await using var provider = services.BuildServiceProvider();

        var channel = new LoginChallengeDispatchChannel(
            Options.Create(new LoginChallengeDispatchOptions { Capacity = 16 }),
            NullLogger<LoginChallengeDispatchChannel>.Instance);
        var service = new LoginChallengeDispatchService(
            channel, provider.GetRequiredService<IServiceScopeFactory>(), _logger);

        // Stop only once the last item's mail went out: a stop issued before the loop started would cancel
        // the loop itself, and the test would measure the race instead of the drain.
        var lastSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.SendLoginChallengeAsync(items[^1].Email, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lastSent.TrySetResult();
                return Task.CompletedTask;
            });

        foreach (var item in items)
            channel.Enqueue(item);
        await service.StartAsync(Ct);
        await lastSent.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task A_store_outage_on_one_item_is_logged_by_type_and_the_next_item_is_still_issued()
    {
        _store.PutAsync(Arg.Is<NewLoginChallenge>(c => c.Email == "first@example.com"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VolatileRedisUnavailableException("RedisConnectionException"));
        _store.PutAsync(Arg.Is<NewLoginChallenge>(c => c.Email == "second@example.com"), Arg.Any<CancellationToken>())
            .Returns(new IssuedCredentials(null, null));

        await DrainAsync(Item("first@example.com"), Item("second@example.com"));

        await _sender.Received(1).SendLoginChallengeAsync(
            "second@example.com", Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        var (level, eventId, message) = _logger.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1010);
        message.ShouldContain(nameof(VolatileRedisUnavailableException));
        message.ShouldNotContain("@");
    }

    [Fact]
    public async Task A_cancellation_inside_one_item_does_not_end_the_drain()
    {
        // Declared unreachable: every call in the drain runs on CancellationToken.None, and no dependency in
        // src/ throws an OperationCanceledException on it (the Scaleway sender turns its timeouts into
        // EmailDeliveryException). What this pins is only that the drain survives it if that ever changes.
        _store.PutAsync(Arg.Is<NewLoginChallenge>(c => c.Email == "first@example.com"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        _store.PutAsync(Arg.Is<NewLoginChallenge>(c => c.Email == "second@example.com"), Arg.Any<CancellationToken>())
            .Returns(new IssuedCredentials(null, null));

        await DrainAsync(Item("first@example.com"), Item("second@example.com"));

        await _sender.Received(1).SendLoginChallengeAsync(
            "second@example.com", Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
    }
}
