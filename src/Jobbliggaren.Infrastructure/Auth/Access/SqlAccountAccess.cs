using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Auth.Access;

public sealed partial class SqlAccountAccess(
    AppDbContext app, AppIdentityDbContext identity, ILogger<SqlAccountAccess> logger,
    ProtectedAccountTransaction protectedTransaction)
    : IAccountAccessCoordinator, IAccountAccessReader, IAccountAccessWriter
{
    private const long LifecycleLock = 0x4A424C41444D494E;
    private readonly ProtectedAccountTransaction _protectedTransaction = protectedTransaction;
    private OwnedScope? _scope;

    public bool HasActiveScope => _scope is not null;
    public bool HasLifecycleScope => _scope?.Lifecycle == true;

    public bool Holds(Guid userId) => _scope?.UserIds.Contains(userId) == true;

    public async Task<IAccountAccessScope> BeginAsync(
        IReadOnlyCollection<Guid> userIds,
        bool lifecycle,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().Order().ToArray();
        if (_scope is not null)
        {
            if ((lifecycle && !_scope.Lifecycle) || ids.Any(id => !_scope.UserIds.Contains(id)))
                throw new InvalidOperationException("A nested access scope cannot acquire additional locks.");
            return new NestedScope(_scope);
        }
        if (app.Database.CurrentTransaction is not null || identity.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The access scope must own both contexts' transaction.");

        var transaction = await app.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var scope = new OwnedScope(this, app, identity, transaction, ids, lifecycle);
        _scope = scope;
        _protectedTransaction.Enter(app.Database.GetDbConnection(), transaction.GetDbTransaction(), scope.Poison);
        try
        {
            if (!ReferenceEquals(identity.Database.GetDbConnection(), app.Database.GetDbConnection()))
                identity.Database.SetDbConnection(app.Database.GetDbConnection(), contextOwnsConnection: false);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            if (lifecycle)
                await LockAsync(LifecycleLock, cancellationToken);
            foreach (var id in ids)
                await LockAsync(AccountLock(id), cancellationToken);
            // A UserManager lookup performed before admission must not become a later full-row write.
            identity.ChangeTracker.Clear();
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public async Task<long> ReadEpochAsync(CancellationToken cancellationToken)
    {
        // Flow creation must never publish an uncommitted epoch from an enclosing mutation.
        await using var connection = new NpgsqlConnection(app.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM identity.account_security_epoch WHERE id = 1", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long epoch && epoch >= 0
            ? epoch
            : throw new InvalidOperationException("The account security epoch is unavailable.");
    }

    public async Task<AccountAccessSnapshot?> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT u.id, u.email, u.is_suspended, u.access_revision, u.credential_cutoff,
                   js.id IS NOT NULL, js.deleted_at,
                   EXISTS (SELECT 1 FROM identity."AspNetUserRoles" ur
                     JOIN identity."AspNetRoles" r ON r.id = ur.role_id
                     WHERE ur.user_id = u.id AND r.normalized_name = @admin_role), u.email_confirmed
            FROM identity."AspNetUsers" u
            LEFT JOIN public.job_seekers js ON js.user_id = u.id
            WHERE u.id = @user_id
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("admin_role", Roles.Admin.ToUpperInvariant());
        var close = await OpenAsync(cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            return new AccountAccessSnapshot(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.GetBoolean(7))
            { InboxConfirmed = reader.GetBoolean(8) };
        }
        finally
        {
            if (close)
                await app.Database.CloseConnectionAsync();
        }
    }

    public async Task<Result<AccountAccessChanged>> ChangeAsync(
        Guid actorId, Guid targetId, bool suspended, CancellationToken cancellationToken)
    {
        RequireLifecycle(targetId);
        if (!Holds(actorId))
            throw new InvalidOperationException("The actor must be locked before an access transition.");
        var target = await ReadAsync(targetId, cancellationToken);
        if (target is null)
            return Result.Failure<AccountAccessChanged>(DomainError.NotFound(
                AccountAccessErrors.AccountNotFound, "Kontot hittades inte."));
        if (!target.HasProfile)
            return Result.Failure<AccountAccessChanged>(DomainError.Gone(
                AccountAccessErrors.ProfileUnavailable, "Kontots profil finns inte längre."));
        if (target.IsSuspended == suspended)
            return Result.Failure<AccountAccessChanged>(DomainError.Conflict(
                suspended ? AccountAccessErrors.AlreadySuspended : AccountAccessErrors.AlreadyReinstated,
                suspended ? "Kontot är redan avstängt." : "Kontots åtkomst är redan återaktiverad."));
        if (suspended && actorId == targetId)
            return Result.Failure<AccountAccessChanged>(DomainError.Conflict(
                AccountAccessErrors.SelfSuspension, "Du kan inte stänga av din egen åtkomst."));
        if (suspended && !await CanRemoveAccessAsync(targetId, cancellationToken))
            return Result.Failure<AccountAccessChanged>(DomainError.Conflict(
                AccountAccessErrors.LastAdministrator, "Den sista administratörens åtkomst kan inte tas bort."));

        var user = await identity.Users.SingleAsync(u => u.Id == targetId, cancellationToken);
        var epoch = await identity.AccountSecurityEpochs.SingleAsync(cancellationToken);
        user.ChangeAccess(suspended, epoch.Advance());
        await identity.SaveChangesAsync(cancellationToken);
        return Result.Success(new AccountAccessChanged(
            targetId, user.IsSuspended, user.AccessRevision, target.DeletedAt is not null));
    }

    public async Task<AccountAccessSnapshot> AdvanceCredentialsAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireLifecycle(userId);
        if ((await ReadAsync(userId, cancellationToken))?.CanAuthenticate != true)
            throw new InvalidOperationException("A credential transition requires fresh account admission.");
        var user = await identity.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        var epoch = await identity.AccountSecurityEpochs.SingleAsync(cancellationToken);
        user.AdvanceCredentials(epoch.Advance());
        await identity.SaveChangesAsync(cancellationToken);
        return await ReadAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("The transitioned account is unavailable.");
    }

    public async Task<bool> CanRemoveAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireLifecycle(userId);
        var account = await ReadAsync(userId, cancellationToken);
        if (account?.IsEffectiveAdmin != true)
            return true;
        await using var command = CreateCommand("""
              SELECT u.email FROM identity."AspNetUsers" u
              JOIN public.job_seekers js ON js.user_id = u.id AND js.deleted_at IS NULL
              WHERE u.id <> @user_id AND NOT u.is_suspended
                AND EXISTS (SELECT 1 FROM identity."AspNetUserRoles" ur
                  JOIN identity."AspNetRoles" r ON r.id = ur.role_id
                  WHERE ur.user_id = u.id AND r.normalized_name = @admin_role)
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("admin_role", Roles.Admin.ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0) && EmailAddressRules.IsUsableInboxAddress(reader.GetString(0)))
                return true;
        }
        return false;
    }

    private void RequireLifecycle(Guid userId)
    {
        if (_scope?.Lifecycle != true || !Holds(userId))
            throw new InvalidOperationException("An access lifecycle mutation requires its outer lifecycle scope.");
    }

    private async Task LockAsync(long key, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("SELECT pg_advisory_xact_lock(@key)");
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long AccountLock(Guid userId) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes($"jobbliggaren.account-access:{userId:D}")));

    private NpgsqlCommand CreateCommand(string sql) => new(
        sql, (NpgsqlConnection)app.Database.GetDbConnection(),
        app.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);

    private async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        if (_scope is not null && (app.Database.GetDbConnection().State != ConnectionState.Open
            || app.Database.CurrentTransaction is null))
        {
            _scope.Poison();
            throw new InvalidOperationException("The protected database connection was lost; it cannot be reopened.");
        }
        if (app.Database.GetDbConnection().State == ConnectionState.Open)
            return false;
        await app.Database.OpenConnectionAsync(cancellationToken);
        return true;
    }

    private sealed class NestedScope(OwnedScope owner) : IAccountAccessScope
    {
        private bool _completed;
        public bool OwnsCommit => false;
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            _completed = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (!_completed)
                owner.Poison();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OwnedScope(
        SqlAccountAccess owner,
        AppDbContext appDb,
        AppIdentityDbContext identityDb,
        IDbContextTransaction transaction,
        Guid[] userIds,
        bool lifecycle) : IAccountAccessScope
    {
        public IReadOnlyCollection<Guid> UserIds { get; } = userIds;
        public bool Lifecycle { get; } = lifecycle;
        public bool OwnsCommit => true;
        private bool _committed;
        private bool _disposed;
        private bool _poisoned;
        public void Poison() => _poisoned = true;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_poisoned)
                throw new InvalidOperationException("A failed nested access operation prevents commit.");
            if (_committed)
                throw new InvalidOperationException("An access transaction cannot be committed twice.");
            owner._protectedTransaction.AssertTransaction(appDb.Database.GetDbConnection(), transaction.GetDbTransaction());
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbException or OperationCanceledException)
            {
                throw new AccountAccessCommitUncertainException(exception);
            }
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                try { await identityDb.Database.UseTransactionAsync(null, CancellationToken.None); }
                catch (Exception exception) when (exception is DbException or InvalidOperationException)
                {
                    owner.LogScopeCleanupFailed(exception.GetType().Name);
                }
                finally
                {
                    try { await transaction.DisposeAsync(); }
                    catch (Exception exception) when (exception is DbException or InvalidOperationException)
                    {
                        owner.LogScopeCleanupFailed(exception.GetType().Name);
                    }
                }
            }
            finally
            {
                if (!_committed)
                    appDb.ChangeTracker.Clear();
                identityDb.ChangeTracker.Clear();
                owner._protectedTransaction.Exit();
                owner._scope = null;
            }
        }
    }

    [LoggerMessage(1031, LogLevel.Warning,
        "Account access transaction cleanup failed ({ErrorType}); the original outcome is retained")]
    private partial void LogScopeCleanupFailed(string errorType);
}
