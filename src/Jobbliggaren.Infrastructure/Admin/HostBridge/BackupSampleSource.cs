using System.Text.Json;
using Jobbliggaren.Application.Admin.Backup;

namespace Jobbliggaren.Infrastructure.Admin.HostBridge;

/// <summary>
/// Reads <c>backup.json</c>, which <c>jobbliggaren-observe-backup.sh</c> publishes (#1982, ADR 0157), and
/// turns the validated envelope into a <see cref="BackupSample"/>. The wire tokens are spelled out here and
/// mapped by an explicit switch: <c>Enum.TryParse</c> would accept a number, a list and any casing, and the
/// shell and the C# would then agree only by luck. The golden files under
/// <c>deploy/systemd/fixtures/observations</c> are what both sides are tested against.
/// </summary>
internal sealed class BackupSampleSource(HostBridgeFileReader reader) : IBackupSampleSource
{
    internal const string FileName = "backup.json";
    internal const string SourceName = "backup";
    private const string SamplerErrorToken = "collector-failed";

    public async ValueTask<BackupSampleRead> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(FileName, SourceName, cancellationToken);
        if (read.Envelope is not { } envelope)
        {
            return FromFailure(read.Failure ?? HostBridgeFailure.InvalidFormat);
        }

        using (envelope)
        {
            if (envelope.ErrorToken is { } token)
            {
                return string.Equals(token, SamplerErrorToken, StringComparison.Ordinal)
                    ? BackupSampleRead.Failed(BackupStatusReason.SamplerError, envelope.SampledAt)
                    : BackupSampleRead.Failed(BackupStatusReason.InvalidFormat);
            }

            return TryReadData(envelope.SampledAt, envelope.Data) is { } sample
                ? BackupSampleRead.Sampled(sample)
                : BackupSampleRead.Failed(BackupStatusReason.InvalidFormat);
        }
    }

    private static BackupSampleRead FromFailure(HostBridgeFailure failure) => failure switch
    {
        HostBridgeFailure.NotConfigured => BackupSampleRead.NotObserved(BackupStatusReason.NotConfigured),
        HostBridgeFailure.NotSampledYet => BackupSampleRead.NotObserved(BackupStatusReason.NotSampledYet),
        HostBridgeFailure.Unreadable => BackupSampleRead.Failed(BackupStatusReason.Unreadable),
        HostBridgeFailure.NotARegularFile => BackupSampleRead.Failed(BackupStatusReason.NotARegularFile),
        HostBridgeFailure.TooLarge => BackupSampleRead.Failed(BackupStatusReason.TooLarge),
        _ => BackupSampleRead.Failed(BackupStatusReason.InvalidFormat),
    };

    private static BackupSample? TryReadData(DateTimeOffset sampledAt, JsonElement data)
    {
        if (!HostBridgeFileReader.TryProperties(data, out var props)
            || props.Count != 2
            || !props.TryGetValue("lastSuccess", out var lastSuccess)
            || !props.TryGetValue("timer", out var timer))
        {
            return null;
        }

        var stamp = TryReadLastSuccess(lastSuccess);
        var schedule = TryReadTimer(timer);
        return stamp is null || schedule is null ? null : new BackupSample(sampledAt, stamp, schedule);
    }

    private static BackupStampSample? TryReadLastSuccess(JsonElement element)
    {
        if (!HostBridgeFileReader.TryProperties(element, out var props) || !TryState(props, out var state))
        {
            return null;
        }

        switch (state)
        {
            case "recorded":
                return props.Count == 3
                    && props.TryGetValue("completedAt", out var completed) && HostBridgeFileReader.TryReadInstant(completed, out var completedAt)
                    && props.TryGetValue("startedAt", out var started) && HostBridgeFileReader.TryReadInstant(started, out var startedAt)
                    ? new BackupStampSample.Recorded(completedAt, startedAt)
                    : null;
            case "missing":
                return props.Count == 1 ? new BackupStampSample.NotRecorded(BackupLastSuccessState.Missing) : null;
            case "unreadable":
                return props.Count == 1 ? new BackupStampSample.NotRecorded(BackupLastSuccessState.Unreadable) : null;
            case "invalid":
                return props.Count == 1 ? new BackupStampSample.NotRecorded(BackupLastSuccessState.Invalid) : null;
            default:
                return null;
        }
    }

    private static BackupTimerSample? TryReadTimer(JsonElement element)
    {
        if (!HostBridgeFileReader.TryProperties(element, out var props) || !TryState(props, out var state))
        {
            return null;
        }

        switch (state)
        {
            case "scheduled":
                return props.Count == 2
                    && props.TryGetValue("nextRunAt", out var next) && HostBridgeFileReader.TryReadInstant(next, out var nextRunAt)
                    ? new BackupTimerSample.Scheduled(nextRunAt)
                    : null;
            case "inactive":
                return props.Count == 1 ? new BackupTimerSample.NotScheduled(BackupTimerState.Inactive) : null;
            case "notInstalled":
                return props.Count == 1 ? new BackupTimerSample.NotScheduled(BackupTimerState.NotInstalled) : null;
            case "unknown":
                return props.Count == 1 ? new BackupTimerSample.NotScheduled(BackupTimerState.Unknown) : null;
            default:
                return null;
        }
    }

    private static bool TryState(Dictionary<string, JsonElement> props, out string state)
    {
        state = string.Empty;
        if (!props.TryGetValue("state", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        state = element.GetString() ?? string.Empty;
        return true;
    }
}
