using System.Text;

namespace Jobbliggaren.Infrastructure.Diagnostics;

/// <summary>
/// The structural half of the host-view check: does <c>/proc/self/mountinfo</c> show that something other
/// than the kernel's own procfs stands behind <c>/proc/stat</c> or <c>/proc/meminfo</c>? That is how lxcfs
/// presents a container's limit as the machine (proc_pid_mountinfo(5): the fields are
/// <c>id parent major:minor root mountpoint options [optional…] - fstype source superoptions</c>).
///
/// <para>
/// It finds a container-scoped view; it cannot prove a host-scoped one. A mountinfo without a <c>/proc</c>
/// line says nothing either way, so it is not a finding.
/// </para>
/// </summary>
internal static class ProcMountInfo
{
    private static readonly string[] Virtualised = ["/proc/stat", "/proc/meminfo"];

    public static bool IsContainerScoped(string mountinfo)
    {
        foreach (var raw in mountinfo.AsSpan().EnumerateLines())
        {
            var separator = raw.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var fields = raw[..separator].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
            {
                continue;
            }

            var mountPoint = Unescape(fields[4]);
            var tail = raw[(separator + 3)..];
            var end = tail.IndexOf(' ');
            var fsType = (end < 0 ? tail : tail[..end]).ToString();

            if (Array.IndexOf(Virtualised, mountPoint) >= 0 || (mountPoint == "/proc" && fsType != "proc"))
            {
                return true;
            }
        }

        return false;
    }

    // The kernel writes a space, tab, newline and backslash in a path as a three-digit octal escape.
    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
        {
            return field;
        }

        var result = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && IsOctal(field, i + 1))
            {
                result.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                result.Append(field[i]);
            }
        }

        return result.ToString();
    }

    private static bool IsOctal(string field, int start) =>
        start + 3 <= field.Length && field.AsSpan(start, 3).IndexOfAnyExceptInRange('0', '7') < 0;
}
