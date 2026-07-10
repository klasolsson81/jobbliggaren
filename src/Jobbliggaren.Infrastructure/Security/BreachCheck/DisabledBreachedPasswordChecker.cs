using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Security.BreachCheck;

/// <summary>
/// Registered instead of <see cref="HibpPasswordBreachClient"/> when <c>BreachCheck:Enabled=false</c>
/// (offline dev boxes, HIBP emergency kill switch). Always reports NotBreached so the Identity
/// validator chain — including <c>PwnedPasswordValidator</c> — stays identical in both modes.
/// </summary>
internal sealed class DisabledBreachedPasswordChecker : IBreachedPasswordChecker
{
    public Task<BreachCheckVerdict> CheckAsync(string password, CancellationToken cancellationToken)
        => Task.FromResult(BreachCheckVerdict.NotBreached);
}
