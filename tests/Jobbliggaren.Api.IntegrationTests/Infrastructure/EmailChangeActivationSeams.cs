using System.Collections.Concurrent;
using System.Data.Common;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

internal sealed class EmailChangeActivationFaults : IDisposable
{
    private readonly ConcurrentDictionary<string, NewBoundChallenge> _bound = new();
    private readonly ConcurrentDictionary<string, int> _consumptions = new();
    private TransportGate? _transport;
    private string? _failedRevocation;

    internal ActivationAuditSaveFailure AuditSaveFailure { get; } = new();
    internal ActivationCommitAcknowledgementLoss CommitAcknowledgementLoss { get; } = new();

    internal NewBoundChallenge BoundRequest(string address) => _bound[address];
    internal int Consumptions(ChallengeId id) => _consumptions.GetValueOrDefault(id.Reveal());

    internal void RecordBound(NewBoundChallenge request)
    {
        if (request.Binding.Purpose == ChallengePurpose.ChangeEmail)
            _bound[request.Recipient] = request;
    }

    internal void RecordConsumption(ChallengeId id, ChallengeBinding binding)
    {
        if (binding.Purpose == ChallengePurpose.ChangeEmail)
            _consumptions.AddOrUpdate(id.Reveal(), 1, (_, value) => value + 1);
    }

    internal IDisposable FailingBoundRevocation(ChallengeId id)
    {
        _failedRevocation = id.Reveal();
        return new RevocationScope(this);
    }

    internal void BeforeBoundRevocation(ChallengeId id)
    {
        if (_failedRevocation == id.Reveal())
            throw new VolatileRedisUnavailableException("RedisConnectionException");
    }

    internal TransportGate PauseTransport(string recipient, bool warning = false, bool throwAfterAccepted = false)
    {
        var gate = new TransportGate(this, recipient, warning, throwAfterAccepted);
        if (Interlocked.CompareExchange(ref _transport, gate, null) is not null)
            throw new InvalidOperationException("An address transport is already parked.");
        return gate;
    }

    internal async Task AfterAcceptedAsync(string recipient, bool warning, Func<IAccountAccessCoordinator> requestCoordinator,
        CancellationToken ct)
    {
        var gate = Volatile.Read(ref _transport);
        if (gate is null || gate.Recipient != recipient || gate.Warning != warning || !gate.Claim())
            return;
        var coordinator = requestCoordinator();
        gate.Accepted.TrySetResult(new TransportObservation(coordinator.HasActiveScope, coordinator.HasLifecycleScope));
        await gate.Released.Task.WaitAsync(ct);
        if (gate.ThrowAfterAccepted)
            throw new EmailDeliveryException("AddressChange", "HttpRequestException");
    }

    public void Dispose() => Volatile.Read(ref _transport)?.Dispose();

    private sealed class RevocationScope(EmailChangeActivationFaults owner) : IDisposable
    {
        public void Dispose() => owner._failedRevocation = null;
    }

    internal sealed record TransportObservation(bool HasActiveScope, bool HasLifecycleScope);

    internal sealed class TransportGate(EmailChangeActivationFaults owner, string recipient, bool warning,
        bool throwAfterAccepted) : IDisposable
    {
        private int _claimed;
        internal string Recipient { get; } = recipient;
        internal bool Warning { get; } = warning;
        internal bool ThrowAfterAccepted { get; } = throwAfterAccepted;
        internal TaskCompletionSource<TransportObservation> Accepted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Claim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
        internal void Release() => Released.TrySetResult();
        public void Dispose()
        {
            Release();
            Interlocked.CompareExchange(ref owner._transport, null, this);
        }
    }
}

internal sealed class ActivationEmailSender(IEmailSender inner, EmailChangeActivationFaults faults,
    IHttpContextAccessor context) : IEmailSender
{
    private IAccountAccessCoordinator RequestCoordinator => context.HttpContext?.RequestServices
        .GetRequiredService<IAccountAccessCoordinator>()
        ?? throw new InvalidOperationException("An address transport must originate in its authenticated request scope.");
    public bool CanDeliver => inner.CanDeliver;
    public Task SendMatchNotificationEmailAsync(string toEmail, MatchNotificationEmail content, CancellationToken ct) =>
        inner.SendMatchNotificationEmailAsync(toEmail, content, ct);
    public Task SendFollowedCompanyNotificationEmailAsync(string toEmail, FollowedCompanyNotificationEmail content,
        CancellationToken ct) => inner.SendFollowedCompanyNotificationEmailAsync(toEmail, content, ct);
    public Task SendEmailChangedNotificationAsync(string toEmail, CancellationToken ct) =>
        inner.SendEmailChangedNotificationAsync(toEmail, ct);

    public async Task SendAccountEmailChangeRequestedNotificationAsync(string toEmail,
        DateTimeOffset completableFrom, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await inner.SendAccountEmailChangeRequestedNotificationAsync(toEmail, completableFrom, expiresAt, ct);
        await faults.AfterAcceptedAsync(toEmail, warning: true, () => RequestCoordinator, ct);
    }

    public async Task SendLoginChallengeAsync(string toEmail, LoginChallengeEmail content, CancellationToken ct)
    {
        await inner.SendLoginChallengeAsync(toEmail, content, ct);
        if (content is LoginChallengeEmail.AddressChangeCode or LoginChallengeEmail.AccountEmailChangeCode)
            await faults.AfterAcceptedAsync(toEmail, warning: false, () => RequestCoordinator, ct);
    }
}

internal sealed class ActivationLoginChallengeStore(ILoginChallengeStore inner, EmailChangeActivationFaults faults)
    : ILoginChallengeStore
{
    public Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct) => inner.PutAsync(challenge, ct);
    public Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct) =>
        inner.ConsumeCodeAsync(id, presented, ct);
    public Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct) => inner.ConsumeLinkAsync(token, ct);

    public async Task<LoginCode> PutBoundAsync(NewBoundChallenge challenge, CancellationToken ct)
    {
        var code = await inner.PutBoundAsync(challenge, ct);
        faults.RecordBound(challenge);
        return code;
    }

    public Task<LoginChallengeProof?> ReadEmailChangeRequestAsync(ChallengeId id, Guid userId, CancellationToken ct) =>
        inner.ReadEmailChangeRequestAsync(id, userId, ct);

    public Task RevokeBoundAsync(ChallengeId id, ChallengeBinding expected, CancellationToken ct)
    {
        faults.BeforeBoundRevocation(id);
        return inner.RevokeBoundAsync(id, expected, ct);
    }

    public Task<ChallengeVerdict> ConsumeBoundCodeAsync(ChallengeId id, LoginCode presented, ChallengeBinding expected,
        CancellationToken ct)
    {
        faults.RecordConsumption(id, expected);
        return inner.ConsumeBoundCodeAsync(id, presented, expected, ct);
    }
}

internal sealed class ActivationAuditSaveFailure : SaveChangesInterceptor
{
    private (string EventType, Guid Target)? _armed;
    internal IDisposable FailingFor(string eventType, Guid target)
    {
        _armed = (eventType, target);
        return new Scope(this);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_armed is { } armed && eventData.Context is { } context
            && context.ChangeTracker.Entries<AuditLogEntry>().Any(entry => entry.State == EntityState.Added
                && entry.Entity.AggregateId == armed.Target && entry.Entity.EventType == armed.EventType))
            throw new DbUpdateException("The request activation audit save failed.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class Scope(ActivationAuditSaveFailure owner) : IDisposable
    {
        public void Dispose() => owner._armed = null;
    }
}

internal sealed class ActivationCommitAcknowledgementLoss : DbTransactionInterceptor
{
    private (string EventType, Guid Target)? _armed;
    private int _fired;
    internal int Fired => Volatile.Read(ref _fired);
    internal IDisposable AfterCommit(string eventType, Guid target)
    {
        _armed = (eventType, target);
        _fired = 0;
        return new Scope(this);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_armed is { } armed && eventData.Context is AppDbContext app
            && app.ChangeTracker.Entries<AuditLogEntry>().Any(entry => entry.Entity.AggregateId == armed.Target
                && entry.Entity.EventType == armed.EventType))
        {
            _armed = null;
            Interlocked.Increment(ref _fired);
            // PostgreSQL committed; the network then loses the acknowledgement.
            throw new NpgsqlException("The request activation commit acknowledgement was lost.");
        }
        return Task.CompletedTask;
    }

    private sealed class Scope(ActivationCommitAcknowledgementLoss owner) : IDisposable
    {
        public void Dispose() => owner._armed = null;
    }
}
