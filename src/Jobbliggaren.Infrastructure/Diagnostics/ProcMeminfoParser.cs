using System.Globalization;

namespace Jobbliggaren.Infrastructure.Diagnostics;

/// <summary>
/// Reads <c>MemTotal</c> and <c>MemAvailable</c> from <c>/proc/meminfo</c> (proc(5)), in kB. <c>MemAvailable</c>
/// is the kernel's estimate of the memory available to start applications without swapping; it counts
/// reclaimable page cache and slab as available, which <c>MemFree</c> does not. There is no fallback to
/// <c>MemFree</c>: a kernel without <c>MemAvailable</c> (before 3.14) has no honest answer, and a wrong one
/// would show the host as nearly full.
/// </summary>
internal static class ProcMeminfoParser
{
    /// <summary>False unless both fields are present, in kB, as plain non-negative integers.</summary>
    public static bool TryParse(string text, out long totalBytes, out long availableBytes)
    {
        long? total = null, available = null;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon];
            if (name.SequenceEqual("MemTotal"))
            {
                total ??= Bytes(line[(colon + 1)..]);
            }
            else if (name.SequenceEqual("MemAvailable"))
            {
                available ??= Bytes(line[(colon + 1)..]);
            }
        }

        (totalBytes, availableBytes) = (total ?? 0, available ?? 0);
        return total is not null && available is not null;
    }

    // "   8135992 kB": a plain non-negative integer and the unit kB, which is what proc(5) documents.
    private static long? Bytes(ReadOnlySpan<char> field)
    {
        field = field.Trim();
        var space = field.IndexOf(' ');
        if (space <= 0 || !field[(space + 1)..].Trim().SequenceEqual("kB"))
        {
            return null;
        }

        if (!long.TryParse(field[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes)
            || kilobytes > long.MaxValue / 1024)
        {
            return null;
        }

        return kilobytes * 1024;
    }
}
