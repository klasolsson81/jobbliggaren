using System.Globalization;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// A compose file read as lines and scoped to one service block, the way the <c>DeployCompose*</c> pins in
/// this project read theirs. Every lookup skips comment lines, because the blocks these pins read explain
/// their own keys in prose and a <c>Contains</c> over raw lines would count the explanation as the setting.
/// </summary>
internal sealed class ComposeFile(string relativePath)
{
    private readonly string[] _lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, relativePath));

    /// <summary>Everything above <c>services:</c> — where the <c>x-*</c> anchors live.</summary>
    public IReadOnlyList<string> Preamble =>
        _lines.TakeWhile(l => !l.StartsWith("services:", StringComparison.Ordinal)).ToList();

    /// <summary>The lines belonging to one service, exclusive of the next service's key.</summary>
    public IReadOnlyList<string> ServiceBlock(string service)
    {
        var start = Array.FindIndex(_lines, l => l.TrimEnd() == $"  {service}:");
        if (start < 0)
            throw new InvalidOperationException($"{relativePath} declares no `{service}` service.");

        var end = Array.FindIndex(_lines, start + 1, l => IsServiceKey(l) || IsTopLevelKey(l));
        return _lines[(start + 1)..(end < 0 ? _lines.Length : end)];
    }

    /// <summary>The block's own keys — the ones at its shallowest indent — in the order written.</summary>
    public static IReadOnlyList<string> Keys(IReadOnlyList<string> block)
    {
        var settings = block.Where(l => !IsComment(l) && !string.IsNullOrWhiteSpace(l)).ToList();
        if (settings.Count == 0)
            return [];

        var indent = settings.Min(Indent);
        return settings.Where(l => Indent(l) == indent).Select(l => l.Trim().Split(':', 2)[0]).ToList();
    }

    public static string? ServiceSetting(IReadOnlyList<string> block, string key) =>
        Setting(block.Where(line => Indent(line) == 4).ToArray(), key);

    /// <summary>The value of the one <c>key: value</c> line in the block, or null when the key is absent.</summary>
    public static string? Setting(IReadOnlyList<string> block, string key)
    {
        var matches = block
            .Where(l => !IsComment(l) && l.TrimStart().StartsWith($"{key}:", StringComparison.Ordinal))
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"`{key}:` occurs {matches.Count} times in the block, expected at most 1. If the file was "
                + "restructured, this pin must be rewritten rather than deleted.");

        return matches.Count == 0 ? null : matches[0].Split(':', 2)[1].Trim();
    }

    /// <summary>The items of the block-form list under <c>key:</c>, in order, exactly as written.</summary>
    public static IReadOnlyList<string> ListUnder(IReadOnlyList<string> block, string key)
    {
        var lines = block.ToList();
        var at = lines.FindIndex(l => !IsComment(l) && l.Trim() == $"{key}:");
        if (at < 0)
            return [];

        var keyIndent = Indent(lines[at]);
        return lines
            .Skip(at + 1)
            .Where(l => !IsComment(l) && !string.IsNullOrWhiteSpace(l))
            .TakeWhile(l => Indent(l) > keyIndent)
            .Where(l => Indent(l) == keyIndent + 2 && l.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l.TrimStart()[2..].Trim())
            .ToList();
    }

    /// <summary>Mebibytes out of Redis's <c>64mb</c>, Docker's <c>160m</c> and a tmpfs <c>size=16m</c> alike.</summary>
    public static int Mebibytes(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        var unit = value[digits.Length..].ToLowerInvariant();
        if (digits.Length == 0 || unit is not ("m" or "mb"))
            throw new InvalidOperationException($"'{value}' is not a whole number of mebibytes.");

        return int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith('#');

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static bool IsServiceKey(string line) =>
        line.Length > 2
        && line.StartsWith("  ", StringComparison.Ordinal)
        && line[2] is not (' ' or '#')
        && line.TrimEnd().EndsWith(':');

    // `networks:` / `volumes:` at column 0 end the LAST service's block.
    private static bool IsTopLevelKey(string line) =>
        line.Length > 0 && line[0] is not (' ' or '#') && line.TrimEnd().EndsWith(':');
}
