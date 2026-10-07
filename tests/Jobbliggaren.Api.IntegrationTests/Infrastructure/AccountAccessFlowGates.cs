using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Grants;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

internal sealed class AccountAccessFlowGates : IDisposable
{
    private Gate? _gate;

    public void Dispose() => Volatile.Read(ref _gate)?.Dispose();

    internal Gate PauseBeforeAdmission(Guid userId) => Arm(userId, Stage.Admission);
    internal Gate PauseBeforeSession(Guid userId) => Arm(userId, Stage.Session);
    internal Gate PauseAfterReauthenticationProof(Guid userId) => Arm(userId, Stage.Reauthentication);

    internal void RecordAdmissionMode(Guid userId, bool lifecycle)
    {
        var gate = Volatile.Read(ref _gate);
        if (gate is not null && gate.UserId == userId && gate.At == Stage.Admission && !gate.Reached.Task.IsCompleted)
            gate.LifecycleRequested = lifecycle;
    }

    private Gate Arm(Guid userId, Stage stage)
    {
        var gate = new Gate(this, userId, stage);
        if (Interlocked.CompareExchange(ref _gate, gate, null) is not null)
            throw new InvalidOperationException("An account flow gate is already armed.");
        return gate;
    }

    internal Task PauseAsync(Guid userId, bool afterCommit, AccountAccessProof? proof, CancellationToken ct) =>
        PauseAsync(userId, afterCommit ? Stage.Session : Stage.Admission, proof, ct);

    internal Task PauseReauthenticationAsync(GrantSubject? subject, CancellationToken ct) =>
        subject is GrantSubject.Reauthentication reauthentication
            ? PauseAsync(reauthentication.UserId, Stage.Reauthentication, reauthentication.Access, ct)
            : Task.CompletedTask;

    private async Task PauseAsync(Guid userId, Stage stage, AccountAccessProof? proof, CancellationToken ct)
    {
        var gate = Volatile.Read(ref _gate);
        if (gate is null || gate.UserId != userId || gate.At != stage || !gate.Claim())
            return;
        gate.Reached.TrySetResult(proof);
        await gate.Released.Task.WaitAsync(ct);
    }

    internal enum Stage { Admission, Session, Reauthentication }

    internal sealed class Gate(AccountAccessFlowGates owner, Guid userId, Stage stage) : IDisposable
    {
        private int _claimed;
        internal Guid UserId { get; } = userId;
        internal Stage At { get; } = stage;
        internal bool? LifecycleRequested { get; set; }
        internal TaskCompletionSource<AccountAccessProof?> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Claim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
        internal void Release() => Released.TrySetResult();
        public void Dispose()
        {
            Release();
            Interlocked.CompareExchange(ref owner._gate, null, this);
        }
    }
}

internal sealed class GatedAccountAccessCoordinator(IAccountAccessCoordinator inner, AccountAccessFlowGates gates)
    : IAccountAccessCoordinator
{
    public bool HasActiveScope => inner.HasActiveScope;
    public bool HasLifecycleScope => inner.HasLifecycleScope;
    public bool Holds(Guid userId) => inner.Holds(userId);

    public async Task<IAccountAccessScope> BeginAsync(IReadOnlyCollection<Guid> userIds, bool lifecycle, CancellationToken ct)
    {
        if (userIds.Distinct().Count() == 1 && !inner.HasActiveScope)
        {
            gates.RecordAdmissionMode(userIds.First(), lifecycle);
            await gates.PauseAsync(userIds.First(), afterCommit: false, null, ct);
        }
        return await inner.BeginAsync(userIds, lifecycle, ct);
    }
}
