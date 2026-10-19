using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// The services of a compose file and the items of a service's <c>volumes:</c> list, read the way the
/// <c>DeployCompose*</c> pins read their blocks: comment lines skipped, a long-syntax mount kept as its own lines.
/// </summary>
internal static class ComposeMounts
{
    public static List<string> ServiceNames(string composePath)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, composePath));
        var start = Array.FindIndex(lines, l => l.StartsWith("services:", StringComparison.Ordinal));
        start.ShouldBeGreaterThan(-1);
        return lines.Skip(start + 1)
            .TakeWhile(l => l.Length == 0 || l[0] == ' ' || l[0] == '#')
            .Where(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Length > 2 && l[2] is not (' ' or '#') && l.TrimEnd().EndsWith(':'))
            .Select(l => l.Trim().TrimEnd(':'))
            .ToList();
    }

    /// <summary>Each item of the service's <c>volumes:</c> list, as its own lines (a mount in long syntax spans several).</summary>
    public static List<List<string>> Of(IReadOnlyList<string> block)
    {
        var lines = block.Where(l => !l.TrimStart().StartsWith('#')).ToList();
        var at = lines.FindIndex(l => l.Trim() == "volumes:");
        if (at < 0)
        {
            return [];
        }

        var keyIndent = lines[at].Length - lines[at].TrimStart().Length;
        var mounts = new List<List<string>>();
        foreach (var line in lines.Skip(at + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            if (indent <= keyIndent)
            {
                break;
            }

            if (indent == keyIndent + 2 && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                mounts.Add([line.TrimStart()[2..].Trim()]);
            }
            else if (mounts.Count > 0)
            {
                mounts[^1].Add(line.Trim());
            }
        }

        return mounts;
    }

    /// <summary>The host path of a mount, in long syntax (<c>source:</c>) or short (<c>/host:/container[:ro]</c>); null for an alias.</summary>
    public static string? SourceOf(List<string> mount)
    {
        var long_ = mount.FirstOrDefault(l => l.StartsWith("source: ", StringComparison.Ordinal));
        if (long_ is not null)
        {
            return long_["source: ".Length..].Trim();
        }

        return mount[0].StartsWith('/') ? mount[0].Split(':')[0] : null;
    }
}
