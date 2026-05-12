using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth.Jobs.HardDeleteAccounts;

/// <summary>
/// Bevakar TD-25 resilient-loop-pattern: per-konto try/catch fångar transient
/// fel utan att blockera efterföljande konton. Idempotenta retries plockas
/// upp av nästa cron-körning.
/// </summary>
public class HardDeleteAccountsJobTests
{
    private static readonly FakeDateTimeProvider NowClock =
        new(new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero));

    private static HardDeleteAccountsJob CreateJob(IAccountHardDeleter hardDeleter) =>
        new(hardDeleter, NowClock, NullLogger<HardDeleteAccountsJob>.Instance);

    [Fact]
    public async Task RunAsync_WhenSingleAccountFails_ContinuesWithNextAccounts()
    {
        // TD-25 — en exception i konto N får inte blockera N+1..M.
        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();
        var account3 = Guid.NewGuid();

        var hardDeleter = Substitute.For<IAccountHardDeleter>();
        hardDeleter.CleanupIdentityOrphansAsync(Arg.Any<CancellationToken>()).Returns(0);
        hardDeleter.GetAccountsReadyForHardDeleteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([account1, account2, account3]);

        hardDeleter.HardDeleteAccountAsync(account2, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Simulerad transient DB-fel för konto 2"));

        var job = CreateJob(hardDeleter);

        await job.RunAsync(CancellationToken.None);

        await hardDeleter.Received(1).HardDeleteAccountAsync(account1, Arg.Any<CancellationToken>());
        await hardDeleter.Received(1).HardDeleteAccountAsync(account2, Arg.Any<CancellationToken>());
        await hardDeleter.Received(1).HardDeleteAccountAsync(account3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenAccountFails_DoesNotThrow()
    {
        // Idempotens-invariant: jobbet ska köra färdigt även om enstaka konto
        // failar — nästa cron-körning plockar upp dem.
        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();

        var hardDeleter = Substitute.For<IAccountHardDeleter>();
        hardDeleter.CleanupIdentityOrphansAsync(Arg.Any<CancellationToken>()).Returns(0);
        hardDeleter.GetAccountsReadyForHardDeleteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([account1, account2]);

        hardDeleter.HardDeleteAccountAsync(account1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fail"));

        var job = CreateJob(hardDeleter);

        await Should.NotThrowAsync(() => job.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_WhenOperationCancelled_PropagatesCancellation()
    {
        // Cancel-disciplin: shutdown-cancel ska INTE sväljas av try/catch.
        // OperationCanceledException re-throws.
        var account1 = Guid.NewGuid();

        var hardDeleter = Substitute.For<IAccountHardDeleter>();
        hardDeleter.CleanupIdentityOrphansAsync(Arg.Any<CancellationToken>()).Returns(0);
        hardDeleter.GetAccountsReadyForHardDeleteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([account1]);

        using var cts = new CancellationTokenSource();
        hardDeleter.HardDeleteAccountAsync(account1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var job = CreateJob(hardDeleter);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));
    }

    [Fact]
    public async Task RunAsync_WhenAllAccountsFail_StillCompletes()
    {
        // Extremfall: alla konton failar. Jobbet ska ändå köra färdigt och
        // logga slutresultat — nästa cron retry:ar.
        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();

        var hardDeleter = Substitute.For<IAccountHardDeleter>();
        hardDeleter.CleanupIdentityOrphansAsync(Arg.Any<CancellationToken>()).Returns(0);
        hardDeleter.GetAccountsReadyForHardDeleteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([account1, account2]);

        hardDeleter.HardDeleteAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fail"));

        var job = CreateJob(hardDeleter);

        await Should.NotThrowAsync(() => job.RunAsync(CancellationToken.None));

        await hardDeleter.Received(1).HardDeleteAccountAsync(account1, Arg.Any<CancellationToken>());
        await hardDeleter.Received(1).HardDeleteAccountAsync(account2, Arg.Any<CancellationToken>());
    }
}
