using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The one writer of an external login and of its <see cref="ExternalLoginLinkedAuditEventType"/> row (ADR 0142 D8).
/// A credential added to a known account is an <c>audit_log</c> row, as the first inbox proof is. The row names the
/// provider and never the provider's identifier for the person.
/// </summary>
public sealed class ExternalLoginLinker(
    IExternalLoginWriter writer,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext)
{
    public const string ExternalLoginLinkedAuditEventType = "User.ExternalLoginLinked";

    /// <summary>
    /// True when the login belongs to <paramref name="userId"/> afterwards; false when another account holds it,
    /// which the caller refuses. The row is committed before this returns, so a session can follow it.
    /// </summary>
    public async Task<bool> LinkAsync(
        Guid userId, ExternalProviderKey provider, ExternalSubject subject, CancellationToken ct)
    {
        switch (await writer.LinkAsync(userId, provider, subject, ct))
        {
            case ExternalLinkResult.Linked:
                db.AuditLogEntries.Add(AuditLogEntry.Create(
                    occurredAt: clock.UtcNow,
                    correlationId: correlationId.Current,
                    userId: userId,
                    eventType: ExternalLoginLinkedAuditEventType,
                    aggregateType: "User",
                    aggregateId: userId,
                    ipAddress: requestContext.IpAddress,
                    userAgent: requestContext.UserAgent,
                    payload: JsonSerializer.Serialize(new { provider = provider.Value })));

                // The Identity write has committed, so from here on CancellationToken.None.
                await db.SaveChangesAsync(CancellationToken.None);
                return true;
            case ExternalLinkResult.AlreadyLinkedToThisUser:
                return true;
            default:
                return false;
        }
    }
}
