namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — deletes our own send counts that no period can read any more. Application defines the port;
/// Infrastructure implements it, because the table is Infrastructure's own. Its one consumer is the retention job.
/// </summary>
public interface IEmailSendCountRetention
{
    /// <summary>Deletes every count dated before <paramref name="firstKeptDay"/> and returns how many rows went.</summary>
    Task<int> DeleteBeforeAsync(DateOnly firstKeptDay, CancellationToken cancellationToken);
}
