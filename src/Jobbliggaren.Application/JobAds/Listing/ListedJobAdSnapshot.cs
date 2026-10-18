using Jobbliggaren.Application.JobAds.Publication;

namespace Jobbliggaren.Application.JobAds.Listing;

/// <summary>
/// The listed set as one read saw it (ADR 0174): the ids of every ad <see cref="JobAdPublication.IsListed"/> admitted at
/// <see cref="AsOf"/>, in id order, split into the sitemap's parts. Immutable and replaced whole, so the stamp and the
/// ids always come from the same read.
/// </summary>
public sealed class ListedJobAdSnapshot
{
    private readonly IReadOnlyList<Guid>[] _parts;

    private ListedJobAdSnapshot(DateTimeOffset asOf, IReadOnlyList<Guid>[] parts)
    {
        AsOf = asOf;
        _parts = parts;
    }

    /// <summary>The instant the read passed to <see cref="JobAdPublication.IsListed"/>.</summary>
    public DateTimeOffset AsOf { get; }

    public static ListedJobAdSnapshot Create(DateTimeOffset asOf, IEnumerable<Guid> idsInOrder)
    {
        ArgumentNullException.ThrowIfNull(idsInOrder);

        var parts = new List<Guid>[ListedJobAdSnapshotLimits.PartCount];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = [];

        foreach (var id in idsInOrder)
            parts[ListedJobAdPartition.PartOf(id, ListedJobAdSnapshotLimits.PartCount) - 1].Add(id);

        return new ListedJobAdSnapshot(asOf, Array.ConvertAll(parts, IReadOnlyList<Guid> (part) => part.AsReadOnly()));
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

/// <summary>Where the sitemap query reads the newest snapshot. It never reads the database; null is "no read has succeeded yet".</summary>
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
