using System.Data.Common;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

internal sealed class CommitAcknowledgementLoss : DbTransactionInterceptor
{
    private readonly Lock _gate = new();
    private Guid? _target;
    private string _eventType = "User.EmailChanged";
    private int _fired;
    internal int Fired => Volatile.Read(ref _fired);

    internal IDisposable AfterAddressChangeCommit(Guid target) => Arm(target, "User.EmailChanged");

    internal IDisposable AfterDeletionCommit(Guid target) => Arm(target, "Admin.AccountDeletionScheduled");

    internal IDisposable AfterFeedbackRequeueCommit(Guid submissionId) => Arm(submissionId, "Admin.FeedbackNotificationRequeued");

    private Scope Arm(Guid target, string eventType)
    {
        lock (_gate)
        {
            _target = target;
            _eventType = eventType;
            _fired = 0;
        }
        return new Scope(this);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_target is { } target && eventData.Context is AppDbContext app
                && app.ChangeTracker.Entries<AuditLogEntry>().Any(entry => entry.Entity.AggregateId == target
                    && entry.Entity.EventType == _eventType))
            {
                _target = null;
                Interlocked.Increment(ref _fired);
                // The real server commit has finished; its caller loses the acknowledgement before it can attest it.
                throw new NpgsqlException("The account lifecycle commit acknowledgement was lost.");
            }
        }
        return Task.CompletedTask;
    }

    private sealed class Scope(CommitAcknowledgementLoss owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
                owner._target = null;
        }
    }
}
