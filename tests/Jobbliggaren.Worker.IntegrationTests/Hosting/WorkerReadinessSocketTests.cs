using System.Net.Sockets;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Worker.Hosting;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

public sealed class WorkerReadinessSocketTests
{
    [Fact]
    public async Task RunningHost_RequiresStartedLifetimeFreshRedisAndValidRequest_AndRemovesSocketOnStop()
    {
        var socketPath = CreateSocketPath();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var started = new CancellationTokenSource();
        using var stopping = new CancellationTokenSource();
        lifetime.ApplicationStarted.Returns(started.Token);
        lifetime.ApplicationStopping.Returns(stopping.Token);
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase().Returns(database);
        database.PingAsync().Returns(Task.FromResult(TimeSpan.Zero));
        using var service = new WorkerReadinessSocketService(new PersistentRedisReadinessProbe(connection), lifetime, socketPath);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await service.StartAsync(ct);
            await WaitForSocketAsync(socketPath, ct);
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(1);
            _ = database.DidNotReceive().PingAsync();
            started.Cancel();
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(0);
            _ = database.Received(1).PingAsync();
            database.PingAsync().Returns(Task.FromException<TimeSpan>(new TimeoutException("Synthetic Redis outage")));
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(1);
            database.PingAsync().Returns(Task.FromResult(TimeSpan.Zero));
            using (var malformed = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                await malformed.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                await malformed.SendAsync(new byte[] { 1, 1 }, SocketFlags.None, ct);
                malformed.Shutdown(SocketShutdown.Send);
                var answer = new byte[1];
                (await malformed.ReceiveAsync(answer, SocketFlags.None, ct)).ShouldBe(1);
                answer[0].ShouldBe((byte)0);
            }
            _ = database.Received(2).PingAsync();
            stopping.Cancel();
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(1);
            await service.StopAsync(ct);
            File.Exists(socketPath).ShouldBeFalse();
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(1);
        }
        finally
        {
            try { await service.StopAsync(CancellationToken.None); }
            finally { DeleteOwnedSocketDirectory(socketPath); }
        }
    }

    [Fact]
    public async Task IndependentHosts_ShouldKeepSecondEndpointLive_WhenFirstStops()
    {
        var firstPath = CreateSocketPath();
        var secondPath = CreateSocketPath();
        firstPath.ShouldNotBe(secondPath);
        using var firstStarted = new CancellationTokenSource();
        using var secondStarted = new CancellationTokenSource();
        var firstLifetime = Substitute.For<IHostApplicationLifetime>();
        var secondLifetime = Substitute.For<IHostApplicationLifetime>();
        firstLifetime.ApplicationStarted.Returns(firstStarted.Token);
        secondLifetime.ApplicationStarted.Returns(secondStarted.Token);
        var firstConnection = Substitute.For<IConnectionMultiplexer>();
        var secondConnection = Substitute.For<IConnectionMultiplexer>();
        var firstDatabase = Substitute.For<IDatabase>();
        var secondDatabase = Substitute.For<IDatabase>();
        firstConnection.GetDatabase().Returns(firstDatabase);
        secondConnection.GetDatabase().Returns(secondDatabase);
        firstDatabase.PingAsync().Returns(Task.FromResult(TimeSpan.Zero));
        secondDatabase.PingAsync().Returns(Task.FromResult(TimeSpan.Zero));
        using var first = new WorkerReadinessSocketService(new PersistentRedisReadinessProbe(firstConnection), firstLifetime, firstPath);
        using var second = new WorkerReadinessSocketService(new PersistentRedisReadinessProbe(secondConnection), secondLifetime, secondPath);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await first.StartAsync(ct);
            await second.StartAsync(ct);
            await Task.WhenAll(WaitForSocketAsync(firstPath, ct), WaitForSocketAsync(secondPath, ct));
            firstStarted.Cancel();
            secondStarted.Cancel();
            (await WorkerReadinessSocketService.ProbeAsync(firstPath, ct)).ShouldBe(0);
            (await WorkerReadinessSocketService.ProbeAsync(secondPath, ct)).ShouldBe(0);
            _ = firstDatabase.Received(1).PingAsync();
            _ = secondDatabase.Received(1).PingAsync();

            firstDatabase.PingAsync().Returns(Task.FromException<TimeSpan>(new TimeoutException("First Redis unavailable")));
            (await WorkerReadinessSocketService.ProbeAsync(firstPath, ct)).ShouldBe(1);
            (await WorkerReadinessSocketService.ProbeAsync(secondPath, ct)).ShouldBe(0);
            _ = firstDatabase.Received(2).PingAsync();
            _ = secondDatabase.Received(2).PingAsync();

            await first.StopAsync(ct);
            File.Exists(firstPath).ShouldBeFalse();
            File.Exists(secondPath).ShouldBeTrue();
            (await WorkerReadinessSocketService.ProbeAsync(firstPath, ct)).ShouldBe(1);
            (await WorkerReadinessSocketService.ProbeAsync(secondPath, ct)).ShouldBe(0);
            _ = firstDatabase.Received(2).PingAsync();
            _ = secondDatabase.Received(3).PingAsync();

            secondDatabase.PingAsync().Returns(Task.FromException<TimeSpan>(new TimeoutException("Second Redis unavailable")));
            (await WorkerReadinessSocketService.ProbeAsync(secondPath, ct)).ShouldBe(1);
            secondDatabase.PingAsync().Returns(Task.FromResult(TimeSpan.Zero));
            (await WorkerReadinessSocketService.ProbeAsync(secondPath, ct)).ShouldBe(0);
            _ = firstDatabase.Received(2).PingAsync();
            _ = secondDatabase.Received(5).PingAsync();
        }
        finally
        {
            try { await Task.WhenAll(first.StopAsync(CancellationToken.None), second.StopAsync(CancellationToken.None)); }
            finally
            {
                DeleteOwnedSocketDirectory(firstPath);
                DeleteOwnedSocketDirectory(secondPath);
            }
        }
    }

    [Fact]
    public async Task ProbeAsync_ShouldRefuseSocketPath_WhenNoListenerAcceptsConnections()
    {
        var socketPath = CreateSocketPath();
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        try
        {
            // The socket fixture leaves the same bound-but-not-listening state as a
            // host interrupted between Bind and Listen; existence alone is not readiness.
            using var abandoned = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            abandoned.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.Exists(socketPath).ShouldBeTrue();
            (await WorkerReadinessSocketService.ProbeAsync(socketPath, ct)).ShouldBe(1);
        }
        finally { DeleteOwnedSocketDirectory(socketPath); }
    }

    private static string CreateSocketPath() =>
        Path.Combine(Path.GetTempPath(), "jbl-readiness-" + Guid.NewGuid().ToString("N"), "ready.sock");

    private static async Task WaitForSocketAsync(string socketPath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100 && !File.Exists(socketPath); attempt++)
            await Task.Delay(10, cancellationToken);
        File.Exists(socketPath).ShouldBeTrue();
    }

    private static void DeleteOwnedSocketDirectory(string socketPath)
    {
        File.Delete(socketPath);
        var directory = Path.GetDirectoryName(socketPath)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory);
    }
}
