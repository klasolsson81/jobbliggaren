using System.Collections.Concurrent;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #616 — last-wins <see cref="IBreachedPasswordChecker"/> override so the integration host NEVER
/// calls the real HIBP API (no network egress from tests, RecordingEmailSender parity). Defaults
/// to <see cref="BreachCheckVerdict.NotBreached"/> so every pre-#616 register/change-password test
/// is unaffected; a test opts a specific password into Breached/Unavailable via
/// <see cref="SetVerdict"/>. Keyed per password because the collection shares one factory —
/// use unique passwords per test.
/// </summary>
internal sealed class StubBreachedPasswordChecker : IBreachedPasswordChecker
{
    private readonly ConcurrentDictionary<string, BreachCheckVerdict> _verdicts = new();

    public void SetVerdict(string password, BreachCheckVerdict verdict)
        => _verdicts[password] = verdict;

    public Task<BreachCheckVerdict> CheckAsync(string password, CancellationToken cancellationToken)
        => Task.FromResult(_verdicts.GetValueOrDefault(password, BreachCheckVerdict.NotBreached));
}
