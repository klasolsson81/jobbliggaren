using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — the prod-dark / CI backstop source, registered when <c>ScbRegister:Enabled</c>
/// is false. It yields nothing (and never touches the certificate), so <c>AddScbCompanyRegister</c>
/// can register the refresher's dependency graph unconditionally while the real cert-based client is
/// wired ONLY when the population is deliberately enabled. The orchestrator also short-circuits on
/// <c>!Enabled</c>, so this is a construction-time placeholder more than a runtime path.
/// </summary>
internal sealed class NullScbCompanyRegisterSource : IScbCompanyRegisterSource
{
    public async IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> StreamLegalEntitiesAsync(
        ScbSyncOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        // A clean, empty run (NOT truncated): 0 rows fetched → the orchestrator's absolute floor skips
        // the deregister sweep anyway, so no company is ever wrongly flagged.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
