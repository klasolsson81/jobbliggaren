using System.Runtime.CompilerServices;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.JobAds;

namespace JobbPilot.Api.IntegrationTests.JobAds;

/// <summary>
/// Test-stub för <see cref="IJobSource"/>. Strömmar förkonfigurerade items
/// från snapshot (IAsyncEnumerable, root-cause-fix 2026-05-16). Används av
/// admin-endpoint-tester som vill verifiera flow utan att starta WireMock-
/// server för varje test.
/// </summary>
internal sealed class StubJobSource(JobSource source, IReadOnlyList<JobAdImportItem> snapshotItems)
    : IJobSource
{
    public JobSource Source { get; } = source;

#pragma warning disable CS1998 // Async iterator utan await — test-stub strömmar in-memory items
    public async IAsyncEnumerable<JobAdImportItem> FetchSnapshotAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in snapshotItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
#pragma warning restore CS1998

#pragma warning disable CS1998 // Async iterator utan await — tom stream-stub
    public async IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield break;
    }
#pragma warning restore CS1998
}
