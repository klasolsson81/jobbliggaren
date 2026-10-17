using System.Collections.Concurrent;

namespace Jobbliggaren.Infrastructure.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172 D4) — this process's claim on (account, UTC minute). The first request of a minute claims it and
/// writes; every later request of that minute does not. The claim is a compare-and-swap, so concurrent requests of
/// one account claim a minute once. Entries from earlier minutes are pruned when a new minute is first claimed, so
/// the map holds roughly the accounts active in the current minute.
/// </summary>
internal sealed class AccountActivityClaims
{
    private readonly ConcurrentDictionary<Guid, long> _claimed = new();
    private long _prunedThrough;

    /// <summary>The UTC minute that contains <paramref name="instant"/> — the precision the record stores.</summary>
    public static DateTimeOffset MinuteOf(DateTimeOffset instant) =>
        new(instant.UtcTicks - instant.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);

    public bool TryClaim(Guid userId, DateTimeOffset minute)
    {
        var bucket = minute.UtcTicks;
        Prune(bucket);
        while (true)
        {
            if (_claimed.TryGetValue(userId, out var held))
            {
                if (held >= bucket)
                    return false;
                if (_claimed.TryUpdate(userId, bucket, held))
                    return true;
            }
            else if (_claimed.TryAdd(userId, bucket))
            {
                return true;
            }
        }
    }

    /// <summary>A login writes the activity minute itself, so the rest of that minute needs no request write.</summary>
    public void Prime(Guid userId, DateTimeOffset minute) =>
        _claimed.AddOrUpdate(userId, minute.UtcTicks, (_, held) => Math.Max(held, minute.UtcTicks));

    private void Prune(long bucket)
    {
        var through = Interlocked.Read(ref _prunedThrough);
        if (bucket <= through || Interlocked.CompareExchange(ref _prunedThrough, bucket, through) != through)
            return;
        foreach (var entry in _claimed)
        {
            // Removes the pair only while it still holds the stale minute, never a claim made meanwhile.
            if (entry.Value < bucket)
                _claimed.TryRemove(entry);
        }
    }
}
