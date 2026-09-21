using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1737 — Identity's user-name charset is off (the user name IS the address), so nothing in the framework refuses
/// a control, whitespace, surrogate or format character any more: <c>StorableAddress.IsStorable</c> does, and only
/// where it is called. A source sweep, because no type can carry the rule: <c>UserManager</c> takes a string. It
/// reads call sites by name, so a writer that reaches Identity through another receiver name is outside it.
/// </summary>
public partial class StoredAddressWriterGuardTests
{
    private const string TheOneWriter = "UserAccountService.cs";
    private const string TheGuard = "StorableAddress.IsStorable(";

    // The UserManager members that store an address: three are its alone by name; CreateAsync is shared with other
    // types (roles, sessions), so it counts only on a receiver named for a user.
    [GeneratedRegex(@"\.(SetEmailAsync|ChangeEmailAsync|SetUserNameAsync)\(|\b\w*[Uu]ser\w*\.CreateAsync\(")]
    private static partial Regex AddressWritingCall();

    // A member declaration at class level, the repo's four-space indent.
    [GeneratedRegex(@"^    (public|private|internal|protected|async|static)\b")]
    private static partial Regex MemberDeclaration();

    [Fact]
    public void Only_the_account_service_writes_an_address_into_Identity()
    {
        var srcRoot = SrcRoot();

        var offenders = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && Path.GetFileName(path) != TheOneWriter)
            .Where(path => AddressWritingCall().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(srcRoot, path))
            .Order()
            .ToList();

        offenders.ShouldBeEmpty(
            "An address reaches Identity outside UserAccountService, where nothing asks StorableAddress.IsStorable: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_address_write_in_the_account_service_follows_the_storable_check_in_its_own_method()
    {
        var file = Directory
            .EnumerateFiles(SrcRoot(), TheOneWriter, SearchOption.AllDirectories)
            .Single(path => !IsBuildOutput(path));
        var lines = File.ReadAllLines(file);

        var writes = Enumerable.Range(0, lines.Length).Where(i => AddressWritingCall().IsMatch(lines[i])).ToList();
        writes.ShouldNotBeEmpty("the sweep must find the writes it guards");

        var unguarded = writes.Where(i => !GuardPrecedesInTheSameMethod(lines, i)).Select(i => i + 1).ToList();

        unguarded.ShouldBeEmpty(
            $"{TheOneWriter} writes an address at line(s) {string.Join(", ", unguarded)} with no "
            + $"{TheGuard}…) earlier in the same method.");
    }

    private static bool GuardPrecedesInTheSameMethod(string[] lines, int write)
    {
        for (var i = write - 1; i >= 0; i--)
        {
            if (lines[i].Contains(TheGuard, StringComparison.Ordinal))
                return true;
            if (MemberDeclaration().IsMatch(lines[i]))
                return false;
        }

        return false;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string SrcRoot([CallerFilePath] string thisFile = "")
    {
        var src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src"));
        Directory.Exists(src).ShouldBeTrue($"src root not found: {src}");
        return src;
    }
}
