using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Worker.IntegrationTests.Common;

/// <summary>
/// ADR 0066 (#802) — test-only observable som ersätter den borttagna
/// <c>DeterministicFakeKms.DecryptCallCount</c>-seamen. Dekorerar den riktiga
/// <see cref="LocalDataKeyProvider"/> (via <c>WorkerTestFixture</c>:s
/// sista-vinner-registrering) och räknar <see cref="UnwrapDataKeyAsync"/> —
/// unwrap-primitivet — på <see cref="IDataKeyProvider"/>-gränsen. Detta är den
/// säkerhetsmeningsfulla mätpunkten (verkligt krypto-I/O per scope, inte
/// cachens interna bokföring): scenario 7 bevisar att en ägar-DEK unwrappas EN
/// gång per scope och sedan serveras ur cachen. Produktkod är orörd — ingen
/// prod-override-yta.
/// </summary>
public sealed class CountingDataKeyProvider(IDataKeyProvider inner) : IDataKeyProvider
{
    private int _unwrapCount;

    /// <summary>Antal <see cref="UnwrapDataKeyAsync"/>-anrop hittills.</summary>
    public int UnwrapCount => Volatile.Read(ref _unwrapCount);

    public void ResetUnwrapCount() => Interlocked.Exchange(ref _unwrapCount, 0);

    public Task<GeneratedDataKey> CreateDataKeyAsync(JobSeekerId owner, CancellationToken ct)
        => inner.CreateDataKeyAsync(owner, ct);

    public Task<byte[]> UnwrapDataKeyAsync(
        JobSeekerId owner, byte[] wrappedDek, CancellationToken ct)
    {
        Interlocked.Increment(ref _unwrapCount);
        return inner.UnwrapDataKeyAsync(owner, wrappedDek, ct);
    }
}
