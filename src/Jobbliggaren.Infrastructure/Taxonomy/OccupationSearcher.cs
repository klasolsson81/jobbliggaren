using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// #2198 (ADR 0180) — the picker's occupation search: a lazy, retry-on-fault cache of the shared
/// <see cref="OccupationNameIndex"/> prepared by <see cref="OccupationSearchRule"/>, and one call to
/// the rule per query. The second reader of the index beside <see cref="OccupationCodeDeriver"/>;
/// it adds no index of its own. It takes no <c>ILogger</c> and no <c>HttpClient</c>: the query is
/// never logged and never leaves the process (an architecture test pins both).
/// </summary>
internal sealed class OccupationSearcher(OccupationNameIndex index) : IOccupationSearcher
{
    private Task<OccupationSearchRule.PreparedIndex>? _cached;

    public async ValueTask<OccupationSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var prepared = await GetPreparedAsync(cancellationToken);
        return OccupationSearchRule.Search(prepared, query);
    }

    private async ValueTask<OccupationSearchRule.PreparedIndex> GetPreparedAsync(CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is { IsCompletedSuccessfully: true })
            return cached.Result;

        // Await BEFORE publishing: a fault leaves _cached unpublished, so the next call retries
        // (parity with OccupationNameIndex.GetAsync).
        var task = PrepareAsync(ct);
        var prepared = await task;
        Volatile.Write(ref _cached, task);
        return prepared;
    }

    private async Task<OccupationSearchRule.PreparedIndex> PrepareAsync(CancellationToken ct) =>
        OccupationSearchRule.Prepare(await index.GetAsync(ct));
}
