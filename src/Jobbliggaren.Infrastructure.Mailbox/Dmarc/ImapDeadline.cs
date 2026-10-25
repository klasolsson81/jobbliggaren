namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a — one command's deadline, enforced here as well as through the client's own timeout so it holds over
/// whatever stream the client reads. It ends in an <see cref="OperationCanceledException"/> the caller's token did not
/// cause, which <see cref="ImapFaults"/> reads as a timeout.
/// </summary>
internal static class ImapDeadline
{
    public static async Task<T> RunAsync<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> command, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return await command(deadline.Token);
    }

    public static async Task RunAsync(TimeSpan timeout, Func<CancellationToken, Task> command, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await command(deadline.Token);
    }
}
