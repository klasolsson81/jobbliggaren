using System.Text.Json;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reads Scaleway Transactional Email's <c>statistics</c> for the delivery totals (#1981 PR 2): the six counts of mail
/// created since the window's start, which carry no recipient. Under the read key only: the sending key reads zeros
/// here (measured 2026-10-19), which is why the evaluator's canary exists.
/// <para>
/// <b>What it sends.</b> <c>project_id</c> (from the transport) and <see cref="ScalewaySince"/> only.
/// <b>What it reads.</b> The six named counts at the top level, each a uint32; every other value is skipped without
/// being materialised. A missing or unreadable count, or sent and failed above the total, is unreadable, never zero.
/// Only the total, sent and failed cross the port.
/// </para>
/// </summary>
internal sealed class ScalewayDeliveryStatisticsSource(
    ScalewayReadTransport transport,
    IDateTimeProvider clock) : IDeliveryStatisticsAttemptSource
{
    internal const int MaxBodyBytes = 64 * 1024;

    private const string StatisticsPath = "statistics";
    private const string TotalCountField = "total_count";
    private const string NewCountField = "new_count";
    private const string SendingCountField = "sending_count";
    private const string SentCountField = "sent_count";
    private const string FailedCountField = "failed_count";
    private const string CanceledCountField = "canceled_count";

    /// <summary>The six counts, in the order <see cref="CountIndex"/> numbers them.</summary>
    private static readonly string[] CountFields =
        [TotalCountField, NewCountField, SendingCountField, SentCountField, FailedCountField, CanceledCountField];

    public ValueTask<ProviderAttempt<EmailDeliveryStatisticsRead>> AttemptAsync(
        StatisticsWindow window, CancellationToken cancellationToken)
    {
        var since = ScalewaySince.Of(clock.UtcNow, StatisticsWindows.LengthOf(window));
        return transport.GetAsync<EmailDeliveryStatisticsRead>(
            StatisticsPath,
            [new(ScalewaySince.Parameter, ScalewaySince.Format(since))],
            MaxBodyBytes,
            body => TryReadCounts(body, out var total, out var sent, out var failed)
                ? new EmailDeliveryStatisticsRead.Observed(total, sent, failed, clock.UtcNow)
                : null,
            reason => new EmailDeliveryStatisticsRead.Failed(reason),
            cancellationToken);
    }

    private static bool TryReadCounts(byte[] body, out long total, out long sent, out long failed)
    {
        total = sent = failed = 0;
        var counts = new uint?[CountFields.Length];

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var index = CountIndex(ref reader);
                reader.Read();
                if (index >= 0)
                {
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt32(out var value))
                    {
                        return false;
                    }

                    counts[index] = value;
                }

                // A no-op on a number; on an object or array it moves past the whole value.
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (Array.Exists(counts, count => count is null))
        {
            return false;
        }

        total = counts[Array.IndexOf(CountFields, TotalCountField)]!.Value;
        sent = counts[Array.IndexOf(CountFields, SentCountField)]!.Value;
        failed = counts[Array.IndexOf(CountFields, FailedCountField)]!.Value;
        return sent + failed <= total;
    }

    /// <summary>Which of <see cref="CountFields"/> the current property names, or -1 for any other field.</summary>
    private static int CountIndex(ref Utf8JsonReader reader)
    {
        for (var i = 0; i < CountFields.Length; i++)
        {
            if (reader.ValueTextEquals(CountFields[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
