using System.Text.Json;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Jobbliggaren.Application.UnitTests.Auth;

internal static class EmailChangeRequestTestKit
{
    public static EmailChangeRequestProof Original(ChallengeId id, DateTimeOffset issuedAt) =>
        new(id.Reveal(), issuedAt, issuedAt + LoginChallengePolicy.ChallengeTtl);

    // The request handler's post-transport commit produces this witness. Unit fixtures model
    // that earlier request; the real transport/SQL activation tests own the durable boundary.
    public static void AddCommittedRequest(IAppDbContext db, Guid userId, EmailChangeRequestProof request)
    {
        db.AuditLogEntries.Add(AuditLogEntry.Create(request.IssuedAt, userId, Guid.NewGuid(),
            ChangeEmailCommand.RequestedEventType, "User", userId, null, null,
            payload: JsonSerializer.Serialize(new { requestId = request.RequestId })));
    }

    public static IAccountEmailChangeRequests Reader(IAppDbContext db)
    {
        var reader = Substitute.For<IAccountEmailChangeRequests>();
        reader.HasCommittedSelfRequestAsync(Arg.Any<Guid>(), Arg.Any<EmailChangeRequestProof>(),
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            var userId = call.Arg<Guid>();
            var original = call.Arg<EmailChangeRequestProof>();
            var expectedPayload = JsonSerializer.Serialize(new { requestId = original.RequestId });
            return original.IsValid && db.AuditLogEntries.AsNoTracking().Any(row =>
                row.EventType == ChangeEmailCommand.RequestedEventType && row.AggregateType == "User"
                && row.AggregateId == userId && row.Payload == expectedPayload
                && row.OccurredAt >= original.IssuedAt && row.OccurredAt < original.ExpiresAt);
        });
        return reader;
    }
}
