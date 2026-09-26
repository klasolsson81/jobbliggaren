using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — the orchestrator behind <see cref="IOccupationDivisionProfileBuilder"/>, Hangfire-agnostic
/// (ADR 0023: the Worker wrapper carries the attributes). One statement, one transaction, one
/// <c>ANALYZE</c>: there is no per-item loop here and therefore no <c>ThrowIfWhollyFailed</c> — the
/// materialiser needs that guard because it swallows individual criterion failures, while a failure
/// here propagates on its own and Hangfire's default retry fires. Do not port the guard; the property
/// it protects is already held.
///
/// <para>
/// The timestamp is taken BEFORE the corpus is read, so it can only ever age the run out of
/// <c>MaxReadAgeHours</c> sooner, never later (the <c>ReplaceStampedAt</c> direction).
/// </para>
/// </summary>
internal sealed partial class OccupationDivisionProfileBuilder(
    OccupationDivisionProfileStore store,
    IDateTimeProvider clock,
    IOptions<OccupationDivisionProfileOptions> options,
    ILogger<OccupationDivisionProfileBuilder> logger) : IOccupationDivisionProfileBuilder
{
    public async Task<OccupationDivisionProfileResult> BuildAsync(CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;

        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return new OccupationDivisionProfileResult(0, 0, 0, 0, 0, startedAt, clock.UtcNow);
        }

        var outcome = await store.RebuildAsync(startedAt, cancellationToken).ConfigureAwait(false);

        // §3.6: once per completed run, and only when the run wrote something — a rebuild over an
        // empty corpus is not a bulk-load path, and the statistics it would refresh describe nothing.
        if (outcome.RowsWritten > 0)
            await store.AnalyzeAsync(cancellationToken).ConfigureAwait(false);

        var result = new OccupationDivisionProfileResult(
            outcome.OccupationGroupsProfiled,
            outcome.RowsWritten,
            outcome.AdsCounted,
            outcome.AdsNotInRegister,
            outcome.AdsInRegisterWithoutSni,
            startedAt,
            clock.UtcNow);

        LogCompleted(
            logger,
            result.OccupationGroupsProfiled,
            result.RowsWritten,
            result.AdsCounted,
            result.AdsNotInRegister,
            result.AdsInRegisterWithoutSni,
            (long)(result.CompletedAt - result.StartedAt).TotalMilliseconds);
        return result;
    }

    [LoggerMessage(
        EventId = 6801,
        Level = LogLevel.Information,
        Message = "Occupation-division profile: disabled (OccupationDivisionProfile:Enabled=false); nothing rebuilt")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 6802,
        Level = LogLevel.Information,
        Message = "Occupation-division profile rebuilt: {OccupationGroups} occupation groups, {Rows} rows, "
            + "{Ads} ads counted ({NotInRegister} not in register, {WithoutSni} in register without SNI) "
            + "in {ElapsedMs} ms")]
    private static partial void LogCompleted(
        ILogger logger,
        int occupationGroups,
        int rows,
        int ads,
        int notInRegister,
        int withoutSni,
        long elapsedMs);
}
