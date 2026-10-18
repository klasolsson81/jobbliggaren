using Jobbliggaren.Application.JobAds.Publication;

namespace Jobbliggaren.Application.JobAds.Listing;

/// <summary>
/// The listed set as one read saw it (ADR 0174, ADR 0176): the ids of every ad <see cref="JobAdPublication.IsListed"/>
/// admitted at <see cref="AsOf"/>, in two views of that one read. The sitemap's parts hold the ids in id order; the
/// catalogue's order holds the same ids newest first. Immutable and replaced whole, so the stamp and both views always
/// come from the same read. It holds ids only: no publication date is kept once the order is built.
/// </summary>
public sealed class ListedJobAdSnapshot
{
    private readonly IReadOnlyList<Guid>[] _parts;
    private readonly IReadOnlyList<Guid> _catalogue;

    private ListedJobAdSnapshot(DateTimeOffset asOf, IReadOnlyList<Guid>[] parts, IReadOnlyList<Guid> catalogue)
    {
        AsOf = asOf;
        _parts = parts;
        _catalogue = catalogue;
    }

    /// <summary>The instant the read passed to <see cref="JobAdPublication.IsListed"/>.</summary>
    public DateTimeOffset AsOf { get; }

    /// <summary>
    /// The ids newest first, by publication date. Ads published at the same instant keep the read's id order, because the
    /// sort is stable over rows that arrive in it; no id is compared in memory.
    /// </summary>
    public IReadOnlyList<Guid> CatalogueOrder => _catalogue;

    /// <summary>How many ads the read admitted.</summary>
    public int Count => _catalogue.Count;

    public static ListedJobAdSnapshot Create(DateTimeOffset asOf, IReadOnlyCollection<ListedJobAdRow> rowsInIdOrder)
    {
        ArgumentNullException.ThrowIfNull(rowsInIdOrder);

        var parts = new List<Guid>[ListedJobAdSnapshotLimits.PartCount];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = new List<Guid>(rowsInIdOrder.Count / parts.Length + 1);

        foreach (var row in rowsInIdOrder)
            parts[ListedJobAdPartition.PartOf(row.Id, ListedJobAdSnapshotLimits.PartCount) - 1].Add(row.Id);

        var catalogue = rowsInIdOrder
            .OrderByDescending(row => row.PublishedAt)
            .Select(row => row.Id)
            .ToArray();

        return new ListedJobAdSnapshot(
            asOf,
            Array.ConvertAll(parts, IReadOnlyList<Guid> (part) => part.AsReadOnly()),
            Array.AsReadOnly(catalogue));
    }

    /// <summary>The ids of one part, numbered from 1, in id order.</summary>
    public IReadOnlyList<Guid> IdsIn(int part)
    {
        if (part < 1 || part > _parts.Length)
            throw new ArgumentOutOfRangeException(nameof(part), part, "No such sitemap part.");

        return _parts[part - 1];
    }

    /// <summary>
    /// Whether the API may answer from this snapshot at <paramref name="now"/>: not older than
    /// <see cref="ListedJobAdSnapshotLimits.MaxAge"/>, and not stamped ahead of the clock beyond
    /// <see cref="ListedJobAdSnapshotLimits.FutureTolerance"/>. Both edges, because a bound that a clock stepping back
    /// can stretch is no bound.
    /// </summary>
    public bool IsFreshAt(DateTimeOffset now) =>
        AsOf <= now + ListedJobAdSnapshotLimits.FutureTolerance
        && now - AsOf <= ListedJobAdSnapshotLimits.MaxAge;
}

/// <summary>One listed ad as the snapshot's read projects it: its id, and the date that orders the catalogue.</summary>
public readonly record struct ListedJobAdRow(Guid Id, DateTimeOffset PublishedAt);

/// <summary>
/// Where the sitemap and the catalogue read the newest snapshot. It never reads the database; null is "no read has
/// succeeded yet".
/// </summary>
public interface IListedJobAdSnapshotReader
{
    ListedJobAdSnapshot? Current { get; }
}

/// <summary>
/// One read of the listed set. The hosted service in the Api calls it on a timer and never lets a failure out of it;
/// the interface exists so a test can hand that guard a sampler that throws.
/// </summary>
public interface IListedJobAdSnapshotSampler
{
    Task SampleAsync(CancellationToken cancellationToken);
}
