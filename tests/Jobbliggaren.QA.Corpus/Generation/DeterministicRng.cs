namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// Index-derived determinism (CTO Fork 2 = 2B). Every corpus row is a pure function of
/// (seed, stratum, local index): row <c>i</c> is identical regardless of how many other
/// rows exist or the order they are produced in — so the corpus is parallelisable,
/// locally reproducible, and a quota bump (300 → 770) never shifts an existing row.
///
/// <para>Deliberately NOT <see cref="HashCode.Combine(int, int)"/> nor
/// <see cref="string.GetHashCode()"/>: both are randomised per-process in modern .NET
/// (DoS hardening) and would make the corpus non-reproducible across runs. The mix below
/// is a fixed SplitMix32 avalanche — stable across processes and runtimes — feeding
/// <see cref="Random(int)"/>, whose legacy algorithm is itself deterministic for a given
/// seed.</para>
/// </summary>
public static class DeterministicRng
{
    /// <summary>A stable 32-bit seed for the given coordinates. Pure; no process state.</summary>
    public static int StableSeed(int seed, CorpusStratum stratum, int localIndex)
    {
        // Fold the three coordinates into one 32-bit state, then avalanche it.
        var state = unchecked((uint)seed);
        state = Mix(state ^ 0x9E3779B9u);                  // golden-ratio constant separates seed-space
        state = Mix(state ^ unchecked((uint)((int)stratum + 1) * 0x85EBCA6Bu));
        state = Mix(state ^ unchecked((uint)(localIndex + 1) * 0xC2B2AE35u));
        // Map to a non-negative int (avoid int.MinValue having no positive magnitude).
        return (int)(state & 0x7FFFFFFFu);
    }

    /// <summary>A <see cref="Random"/> seeded deterministically for these coordinates.</summary>
    public static Random For(int seed, CorpusStratum stratum, int localIndex) =>
        new(StableSeed(seed, stratum, localIndex));

    // SplitMix32 finaliser (Steele/Vigna) — a fixed integer hash with good avalanche.
    private static uint Mix(uint z)
    {
        unchecked
        {
            z = (z ^ (z >> 16)) * 0x7FEB352Du;
            z = (z ^ (z >> 15)) * 0x846CA68Bu;
            return z ^ (z >> 16);
        }
    }
}
