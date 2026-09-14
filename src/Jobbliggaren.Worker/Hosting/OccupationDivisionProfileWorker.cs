using Hangfire;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #1682 — the thin Hangfire wrapper around <see cref="IOccupationDivisionProfileBuilder"/> (ADR 0023:
/// attributes here, orchestration Hangfire-agnostic). Registered on
/// <c>RecurringJobIds.BuildOccupationDivisionProfile</c> with the config-driven cron in
/// <c>OccupationDivisionProfileOptions</c>.
///
/// <para>
/// <c>DisableConcurrentExecution</c> with the per-method <c>(int)</c> constructor and a fifteen-minute
/// wait, parity <c>CompanyWatchCriterionMaterialisationWorker.RunAsync</c>: the job arrives once a
/// day, and a duplicate waiting that long is evidence something is genuinely wrong. It does NOT share
/// ADR 0139's lock resource — that key exists because two writers touch the same two tables, and this
/// job shares no table with them; widening the key would serialise unrelated work.
/// </para>
/// <para>
/// Hangfire's default retry is kept, deliberately: no external call, ~163 ms, idempotent by
/// construction (a full replace), and the next scheduled attempt is 24 h away — suppressing the
/// retry would turn a transient database blip into a full day of stale profile.
/// </para>
/// </summary>
public sealed class OccupationDivisionProfileWorker(IOccupationDivisionProfileBuilder builder)
{
    [DisableConcurrentExecution(15 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        builder.BuildAsync(cancellationToken);
}
