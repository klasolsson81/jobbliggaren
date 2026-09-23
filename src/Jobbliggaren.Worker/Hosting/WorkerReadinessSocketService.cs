using System.Net.Sockets;
using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;

namespace Jobbliggaren.Worker.Hosting;

internal sealed class WorkerReadinessSocketService(
    PersistentRedisReadinessProbe redis, IHostApplicationLifetime lifetime, string? socketPath = null) : BackgroundService
{
    internal static string SocketPath => Path.Combine(Path.GetTempPath(), "jobbliggaren-worker", "ready.sock");
    private readonly string _socketPath = socketPath ?? SocketPath;
    private static readonly byte[] Request = [1];
    private static readonly byte[] Healthy = [1];
    private static readonly byte[] Unhealthy = [0];
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.GetDirectoryName(_socketPath)!;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.Delete(_socketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(4);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptAsync(stoppingToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(ProbeTimeout);
                try
                {
                    var request = new byte[2];
                    var count = await client.ReceiveAsync(request, SocketFlags.None, deadline.Token);
                    // The sender half-closes, so EOF is part of the complete request.
                    var valid = count == 1 && request[0] == Request[0]
                        && await client.ReceiveAsync(request, SocketFlags.None, deadline.Token) == 0;
                    var ready = valid && lifetime.ApplicationStarted.IsCancellationRequested
                        && !lifetime.ApplicationStopping.IsCancellationRequested
                        && await redis.CheckAsync(deadline.Token)
                        && !lifetime.ApplicationStopping.IsCancellationRequested;
                    await client.SendAsync(ready ? Healthy : Unhealthy, SocketFlags.None, deadline.Token);
                    client.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException) { }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { File.Delete(_socketPath); }
    }

    internal static async Task<int> ProbeAsync(string? socketPath = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ProbeTimeout);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath ?? SocketPath), deadline.Token);
            await client.SendAsync(Request, SocketFlags.None, deadline.Token);
            client.Shutdown(SocketShutdown.Send);
            var response = new byte[2];
            var count = await client.ReceiveAsync(response, SocketFlags.None, deadline.Token);
            return count == 1 && response[0] == Healthy[0]
                && await client.ReceiveAsync(response, SocketFlags.None, deadline.Token) == 0 ? 0 : 1;
        }
        catch (SocketException) { return 1; }
        catch (OperationCanceledException) { return 1; }
        catch (IOException) { return 1; }
    }
}
