using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1293 — joins the <c>Auth.*</c> wire codes across the language boundary.
///
/// <para>
/// <b>The contract.</b> The Api puts a machine code in a ProblemDetails <c>title</c>, and the web
/// client compares that title against a string of its own to pick an arm. Both suites pinned their
/// own literal and nothing crossed: renaming a code in C# together with its C# tests left every
/// suite green and the client arm dead, falling through to a vaguer message.
/// </para>
///
/// <para>
/// <b>Direction is why this class is in the C# suite</b>, as <see cref="SuggestionKindWireContractTests"/>
/// argues for its own join: the likely accident is backend-first, and a backend change runs
/// <c>dotnet test</c>, never vitest. The reverse accident, a literal typed inline at a client call
/// site instead of read from the module, is held on that side by <c>auth-error-codes.test.ts</c>.
/// </para>
///
/// <para>
/// <b>On the premise (AGENTS.md §5 <c>Tests:</c>).</b> The client's codes are read out of the shipped
/// source file and the backend's out of the real constants. One thing is re-typed here and named:
/// the <c>"Auth."</c> prefix of <see cref="ComposedCodes"/>.
/// </para>
/// </summary>
public class AuthErrorCodeWireContractTests
{
    private const string FrontendModuleRelativePath =
        "web/jobbliggaren-web/src/lib/auth/auth-error-codes.ts";

    private const string FrontendObjectName = "AUTH_ERROR_CODES";

    /// <summary>
    /// Codes with no constant on <see cref="AuthErrorCodes"/>: <c>UserAccountService</c> composes them
    /// as <c>$"Auth.{error.Code}"</c> from an Identity error code. The composition's prefix is
    /// re-typed here; <c>BreachedPasswordTests</c> pins the composed value through the real endpoint.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ComposedCodes =
        new Dictionary<string, string>
        {
            ["PwnedPassword"] = $"Auth.{PwnedPasswordValidator.ErrorCode}",
        };

    [Fact]
    public void EveryCodeTheClientComparesIsTheValueTheBackendEmits()
    {
        foreach (var (name, literal) in ReadFrontendCodes())
        {
            BackendValueOf(name).ShouldBe(
                literal,
                $"{FrontendObjectName}.{name} in {FrontendModuleRelativePath} is what the web client "
                + "compares a ProblemDetails title against. If the backend code was renamed, rename it "
                + "there in the same PR; the client arm is dead until the two agree.");
        }
    }

    [Fact]
    public void AComposedCodeIsNotAlsoAConstant()
    {
        // If a constant appears for a composed code, the constant is the better source and the
        // special case above should go, or the join has two answers for one name.
        foreach (var name in ComposedCodes.Keys)
        {
            FieldNamed(name).ShouldBeNull(
                $"AuthErrorCodes.{name} now exists. Drop '{name}' from {nameof(ComposedCodes)} so the "
                + "join reads the constant.");
        }
    }

    private static string BackendValueOf(string name)
    {
        if (ComposedCodes.TryGetValue(name, out var composed))
            return composed;

        var field = FieldNamed(name);
        field.ShouldNotBeNull(
            $"the web client compares a code named '{name}' ({FrontendModuleRelativePath}), and "
            + $"{nameof(AuthErrorCodes)} has no public string field of that name. It was renamed or "
            + "removed; change both sides in the same PR.");
        return (string)field!.GetValue(null)!;
    }

    /// <summary>A <c>const</c> or a <c>static readonly</c>: either one is an answer for the name.</summary>
    private static FieldInfo? FieldNamed(string name)
    {
        var field = typeof(AuthErrorCodes).GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field?.FieldType == typeof(string) ? field : null;
    }

    /// <summary>
    /// Read as source text, for the reason <see cref="SuggestionKindWireContractTests"/> gives.
    /// A member the harvest cannot read THROWS: a skipped member is a code the loop above never
    /// compares, and an empty list is that for every one of them.
    /// </summary>
    private static (string Name, string Literal)[] ReadFrontendCodes()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                FrontendModuleRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        // Both comment forms go first, so neither a commented-out object nor a commented-out member
        // can be read as the live one.
        var withoutComments = Regex.Replace(
            source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        withoutComments = Regex.Replace(withoutComments, @"//[^\n]*", string.Empty);

        var body = Regex
            .Match(withoutComments, $@"\b{FrontendObjectName}\s*=\s*\{{([^}}]*)\}}")
            .Groups[1];

        if (!body.Success)
            throw new InvalidOperationException(
                $"Could not read {FrontendObjectName} out of {FrontendModuleRelativePath}. It was "
                + "renamed or reshaped - re-make this join deliberately, do not delete it.");

        var codes = Regex.Matches(body.Value, @"(\w+)\s*:\s*""([^""]+)""")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .ToArray();

        // One colon per member, whatever its quoting: a quoted key, a single-quoted or template
        // value, or a brace that cut the body short each leave a colon the harvest did not pair.
        var members = body.Value.Count(c => c == ':');
        if (codes.Length == 0 || codes.Length != members)
            throw new InvalidOperationException(
                $"{FrontendObjectName} in {FrontendModuleRelativePath} has {members} member(s) and "
                + $"the harvest read {codes.Length}. Write every member as a bare key and a "
                + "double-quoted value, or re-make this join deliberately.");

        return codes;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "could not find the repo root (CLAUDE.md) walking up from the test bin - this class "
            + "needs the source tree for its cross-language source-text scan");
        return dir!.FullName;
    }
}
