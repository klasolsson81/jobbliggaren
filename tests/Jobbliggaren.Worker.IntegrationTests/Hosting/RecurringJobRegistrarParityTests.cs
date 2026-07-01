using Hangfire;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Worker.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// #204 / TD-83 PR2 — drift-lock between the Worker's <see cref="RecurringJobRegistrar"/> and the
/// closed trigger allowlist <see cref="RecurringJobIds.All"/>. The admin "trigger now" surface
/// validates against <c>RecurringJobIds.All</c>; if a registered job were missing from the allowlist
/// it would be untriggerable, and an allowlisted-but-unregistered id would validate then no-op. This
/// test proves the registrar registers EXACTLY the ids in <c>RecurringJobIds.All</c> — no more, no
/// fewer (security-auditor T7 parity invariant).
///
/// <para>
/// Pure unit test (NSubstitute <see cref="IRecurringJobManager"/>, no DB). Placed in
/// <c>Worker.IntegrationTests</c> because Jobbliggaren has no <c>Worker.UnitTests</c> project yet —
/// same pragmatic placement note as <see cref="HangfireWorkerOptionsTests"/>.
/// </para>
///
/// <para>
/// Capture mechanism: <c>RecurringJobRegistrar</c> calls the generic Hangfire EXTENSION method
/// <c>AddOrUpdate&lt;T&gt;(id, expr, cron)</c>, which builds a <c>Job</c> and delegates to the
/// INTERFACE method <c>AddOrUpdate(string recurringJobId, Job, string, RecurringJobOptions)</c> — the
/// only call NSubstitute records on the substitute. We read every such interface call from
/// <c>ReceivedCalls()</c> and take the first argument (the id) to avoid coupling the assertion to the
/// <c>Job</c>/<c>RecurringJobOptions</c> argument shapes.
/// </para>
/// </summary>
public class RecurringJobRegistrarParityTests
{
    private static async Task<IReadOnlyList<string>> CapturedRegisteredIdsAsync()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(manager);

        await registrar.StartAsync(CancellationToken.None);

        // Read the recurringJobId (first arg) of every interface AddOrUpdate(string, Job, string,
        // RecurringJobOptions) call recorded on the substitute. The generic AddOrUpdate<T> extension
        // funnels here, so this captures all 16 registrations regardless of the worker type.
        return manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();
    }

    [Fact]
    public async Task StartAsync_RegistersExactlyTheAllowlistIds_NoDrift()
    {
        var registered = await CapturedRegisteredIdsAsync();

        // Set equality both directions: every registered id is allowlisted AND every allowlisted id
        // is registered. A drift in either direction fails.
        registered.ToHashSet(StringComparer.Ordinal)
            .SetEquals(RecurringJobIds.All)
            .ShouldBeTrue(
                $"registered=[{string.Join(", ", registered.OrderBy(x => x, StringComparer.Ordinal))}] " +
                $"allowlist=[{string.Join(", ", RecurringJobIds.All.OrderBy(x => x, StringComparer.Ordinal))}]");
    }

    [Fact]
    public async Task StartAsync_RegistersEachIdExactlyOnce()
    {
        var registered = await CapturedRegisteredIdsAsync();

        // 16 calls, all distinct — guards a copy-paste double-registration (which a set comparison
        // alone would silently absorb).
        registered.Count.ShouldBe(RecurringJobIds.All.Count);
        registered.Distinct(StringComparer.Ordinal).Count().ShouldBe(registered.Count);
    }
}
