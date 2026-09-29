namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// Erases every external login an account holds, whichever provider wrote it, for the account-deletion endpoint
/// alone and after the soft delete has committed (ADR 0142 Amendment (20), ADR 0146 D3). No provider parameter: a
/// provider whose keys were removed keeps its rows, and they go too.
/// </summary>
public interface IExternalLoginEraser
{
    Task EraseAllAsync(Guid userId, CancellationToken ct);
}
