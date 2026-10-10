using System.Globalization;

namespace Jobbliggaren.Infrastructure.Diagnostics;

/// <summary>
/// Reads the aggregate <c>cpu</c> line of <c>/proc/stat</c> (proc(5)): the ticks all CPUs together have spent
/// in each mode, cumulative since boot, in USER_HZ. The columns are
/// <c>user nice system idle iowait irq softirq steal guest guest_nice</c>.
///
/// <para>
/// The grouping is the adapter's, so the port can speak in <c>Busy</c> and <c>Idle</c> and not in procfs
/// columns: <c>Idle = idle + iowait</c>; <c>Busy = user + nice + system + irq + softirq + steal</c>. The two
/// guest columns are left out because the kernel already counts guest time inside <c>user</c> and <c>nice</c>
/// (proc(5)); adding them would count it twice. Steal is busy: the CPU the hypervisor gave to another guest
/// was wanted and not available (the htop convention). Grouped, because proc(5) does not promise that
/// <c>iowait</c> alone never decreases, while the sums do not.
/// </para>
/// </summary>
internal static class ProcStatParser
{
    private const int MinimumColumns = 8; // through steal: Linux 2.6.11 and later
    private const int KnownColumns = 10;

    /// <summary>
    /// False when there is no aggregate line, it has fewer than eight columns, a column is not a plain
    /// non-negative integer, or a sum would overflow. The per-CPU lines (<c>cpu0</c>…) are not the aggregate.
    /// </summary>
    public static bool TryParse(string text, out long busy, out long idle)
    {
        (busy, idle) = (0, 0);
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            // "cpu" followed by whitespace is the aggregate; "cpu0" is one CPU.
            if (line.Length <= 3 || !line.StartsWith("cpu", StringComparison.Ordinal) || !char.IsWhiteSpace(line[3]))
            {
                continue;
            }

            Span<long> column = stackalloc long[KnownColumns];
            var count = 0;
            var rest = line[3..];
            while (count < KnownColumns)
            {
                rest = rest.TrimStart();
                if (rest.IsEmpty)
                {
                    break;
                }

                var end = rest.IndexOfAny(' ', '\t');
                var token = end < 0 ? rest : rest[..end];
                if (!long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out column[count]))
                {
                    return false;
                }

                count++;
                rest = end < 0 ? default : rest[end..];
            }

            if (count < MinimumColumns)
            {
                return false;
            }

            try
            {
                checked
                {
                    busy = column[0] + column[1] + column[2] + column[5] + column[6] + column[7];
                    idle = column[3] + column[4];
                }
            }
            catch (OverflowException)
            {
                (busy, idle) = (0, 0);
                return false;
            }

            return true;
        }

        return false;
    }
}
