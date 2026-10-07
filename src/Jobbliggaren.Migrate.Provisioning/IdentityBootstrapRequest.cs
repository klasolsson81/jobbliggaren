using System.Text.RegularExpressions;

namespace Jobbliggaren.Migrate;

public sealed partial class IdentityBootstrapRequest
{
    private readonly string[] _predecessor;
    private readonly string[] _additions;
    private readonly bool _initial;

    private IdentityBootstrapRequest(string[] predecessor, string[] additions, bool initial)
    {
        _predecessor = predecessor;
        _additions = additions;
        _initial = initial;
    }

    public static bool TryParse(IReadOnlyList<string> arguments, out IdentityBootstrapRequest? request)
    {
        request = null;
        if (arguments is ["--initial"])
        {
            request = new([], [], initial: true);
            return true;
        }

        if (arguments is not ["--expect-history", var predecessor, "--expect-migrations", var additions])
            return false;

        var before = predecessor.Length == 0 ? [] : predecessor.Split(',');
        var added = additions.Split(',');
        if (!IsValid(before, allowEmpty: true) || !IsValid(added, allowEmpty: false))
            return false;

        request = new(before, added, initial: false);
        return true;
    }

    public IReadOnlyList<string> Validate(IReadOnlyList<string> compiled, IReadOnlyList<string> applied)
    {
        if (!IsValid(compiled, allowEmpty: false) || !IsValid(applied, allowEmpty: true))
            throw new InvalidOperationException("Identity migration manifest or primary history is malformed.");

        if (_initial)
        {
            if (applied.Count != 0)
                throw new InvalidOperationException("Initial Identity bootstrap requires empty primary history.");
        }
        else
        {
            if (!_predecessor.Concat(_additions).SequenceEqual(compiled, StringComparer.Ordinal))
                throw new InvalidOperationException("Approved Identity predecessor and additions differ from the compiled candidate.");

            if (!applied.SequenceEqual(_predecessor, StringComparer.Ordinal)
                && !applied.SequenceEqual(compiled, StringComparer.Ordinal))
                throw new InvalidOperationException("Primary Identity history is neither the approved predecessor nor the complete candidate.");
        }

        return Array.AsReadOnly(compiled.Skip(applied.Count).ToArray());
    }

    private static bool IsValid(IReadOnlyList<string> ids, bool allowEmpty)
    {
        if (!allowEmpty && ids.Count == 0)
            return false;
        for (var index = 0; index < ids.Count; index++)
        {
            if (!MigrationIdPattern().IsMatch(ids[index])
                || index > 0 && StringComparer.Ordinal.Compare(ids[index - 1], ids[index]) >= 0)
                return false;
        }
        return true;
    }

    [GeneratedRegex("\\A[0-9]{14}_[A-Za-z0-9_]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationIdPattern();
}
