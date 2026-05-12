using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.JobAds;

namespace JobbPilot.Api.IntegrationTests.JobAds;

/// <summary>
/// Test-stub för <see cref="IJobSource"/>. Returnerar förkonfigurerade items
/// från snapshot. Används av admin-endpoint-tester som vill verifiera flow
/// utan att starta WireMock-server för varje test.
/// </summary>
internal sealed class StubJobSource(JobSource source, IReadOnlyList<JobAdImportItem> snapshotItems)
    : IJobSource
{
    public JobSource Source { get; } = source;

    public Task<JobAdSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new JobAdSnapshot(snapshotItems, DateTimeOffset.UtcNow));

#pragma warning disable CS1998 // Async method lacks await — test-stub returnerar empty stream
    public async IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield break;
    }
#pragma warning restore CS1998
}
