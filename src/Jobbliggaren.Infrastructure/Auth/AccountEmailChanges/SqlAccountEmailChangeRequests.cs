using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;

public sealed class SqlAccountEmailChangeRequests(AppDbContext app, IAccountAccessCoordinator coordinator)
    : IAccountEmailChangeRequests
{
    public async Task<bool> HasCommittedRequestAsync(
        Guid userId, Guid requestId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken ct)
        => await HasCommittedAsync(userId, requestId.ToString("D"), issuedAt, expiresAt,
            RequestAccountEmailChangeCommand.RequestedEventType, ct);

    public Task<bool> HasCommittedSelfRequestAsync(Guid userId, EmailChangeRequestProof request, CancellationToken ct) =>
        request.IsValid
            ? HasCommittedAsync(userId, request.RequestId, request.IssuedAt, request.ExpiresAt,
                ChangeEmailCommand.RequestedEventType, ct)
            : Task.FromResult(false);

    private async Task<bool> HasCommittedAsync(
        Guid userId, string requestId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, string eventType, CancellationToken ct)
    {
        if (!coordinator.Holds(userId) || app.Database.CurrentTransaction is null)
            throw new InvalidOperationException("An address-change witness requires its protected transaction.");
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM public.audit_log
              WHERE occurred_at >= @issued_at AND occurred_at < @expires_at
                AND event_type = @event_type AND aggregate_type = @aggregate_type
                AND aggregate_id = @user_id AND payload ->> 'requestId' = @request_id)
            """, (NpgsqlConnection)app.Database.GetDbConnection(),
            (NpgsqlTransaction)app.Database.CurrentTransaction.GetDbTransaction());
        command.Parameters.AddWithValue("issued_at", issuedAt);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("aggregate_type", "User");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("request_id", requestId);
        return (bool)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("The committed address-change request is unavailable."));
    }
}
