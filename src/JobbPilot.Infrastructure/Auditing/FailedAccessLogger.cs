using JobbPilot.Application.Common.Auditing;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// Strukturerad implementation av <see cref="IFailedAccessLogger"/>.
/// Använder <see cref="LoggerMessage"/>-source-generator för hög-performance
/// strukturerad logging. Property-namn (event_name, aggregate_type,
/// requested_aggregate_id, requesting_user_id, operation) är fasta så
/// CloudWatch metric filter kan parsa via <c>{ $.event_name = "failed_access_attempt" }</c>
/// och gruppera på <c>requesting_user_id</c> för anomaly-detection
/// (TD-68 — Terraform-leverans).
/// </summary>
public sealed partial class FailedAccessLogger(ILogger<FailedAccessLogger> logger)
    : IFailedAccessLogger
{
    public void LogCrossUserAttempt(
        string aggregateType,
        Guid requestedAggregateId,
        Guid requestingUserId,
        string operation)
    {
        LogFailedAccessAttempt(
            logger,
            aggregateType,
            requestedAggregateId,
            requestingUserId,
            operation);
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Failed cross-user access attempt: " +
                  "event_name=failed_access_attempt " +
                  "aggregate_type={AggregateType} " +
                  "requested_aggregate_id={RequestedAggregateId} " +
                  "requesting_user_id={RequestingUserId} " +
                  "operation={Operation}")]
    private static partial void LogFailedAccessAttempt(
        ILogger logger,
        string aggregateType,
        Guid requestedAggregateId,
        Guid requestingUserId,
        string operation);
}
