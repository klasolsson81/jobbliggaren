using Jobbliggaren.Infrastructure.Configuration;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Application.UnitTests.Common.Configuration;

public sealed class PersistentRedisReadinessProbeTests
{
    [Fact]
    public async Task CanceledWait_DoesNotQueueAnotherPing_AndLateSuccessCannotAnswerTheNextProbe()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        var delayed = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        database.PingAsync().Returns(delayed.Task);
        var probe = new PersistentRedisReadinessProbe(connection);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        (await probe.CheckAsync(canceled.Token)).ShouldBeFalse();
        (await probe.CheckAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        _ = database.Received(1).PingAsync();
        delayed.SetResult(TimeSpan.Zero);
        await delayed.Task;
        database.PingAsync().Returns(Task.FromException<TimeSpan>(new TimeoutException("Synthetic transport timeout")));
        // Wait for the first asynchronous continuation to release its outstanding task.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            (await probe.CheckAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
            if (database.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IDatabase.PingAsync)) > 1)
                return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new InvalidOperationException("The completed probe never released its outstanding operation.");
    }
}
