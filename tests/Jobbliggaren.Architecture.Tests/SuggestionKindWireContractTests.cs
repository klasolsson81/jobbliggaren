using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1546 — pins the <see cref="SuggestionKind"/> wire contract, which is an ordinal one and was
/// pinned on one side only.
///
/// <para>
/// <b>The contract.</b> The Api configures no JSON serialization at all, so System.Text.Json writes
/// this enum as a BARE INTEGER, and the frontend decodes it POSITIONALLY:
/// <c>SUGGESTION_KIND_ORDER[wireValue]</c> in <c>web/jobbliggaren-web/src/lib/dto/job-ads.ts</c>.
/// Reordering the enum, or inserting a member anywhere but the end, therefore silently remaps every
/// kind after the insertion point — a Region suggestion arrives at the client as a Municipality.
/// </para>
///
/// <para>
/// <b>What was unpinned, stated precisely.</b> Four test files under <c>tests/</c> DO name
/// <see cref="SuggestionKind"/>, so "no test mentions it" is false. But every one of them uses it
/// SYMBOLICALLY (<c>SuggestionKind.Region</c>), which is invariant under a renumbering: not one
/// asserts an ordinal or a serialized value. The only ordinal assertion in the repo lived in
/// TypeScript (<c>job-ads.test.ts</c>), and it compares the FE array against a hand-written FE
/// literal — so it catches an FE-only reorder and is blind to a C#-only one.
/// </para>
///
/// <para>
/// <b>Direction is why this class is in the C# suite.</b> The likely accident is backend-first:
/// someone appends a member to the enum and never opens the frontend. A backend change runs
/// <c>dotnet test</c>; it does not run vitest. So the cross-language join is asserted from here,
/// reading the TypeScript as source text. The reverse accident (the FE array reordered alone) is
/// already covered by the existing hand-written literal on that side, so the two together make the
/// contract two-sided.
/// </para>
///
/// <para>
/// <b>On the premise (CLAUDE.md §5 <c>Tests:</c>).</b> Nothing here is hand-seeded. The ordinals are
/// read from the real enum, the serialized form from the real DTO through the real serializer, and
/// the frontend list out of the shipped source file.
/// </para>
/// </summary>
public class SuggestionKindWireContractTests
{
    private const string FrontendDtoRelativePath = "web/jobbliggaren-web/src/lib/dto/job-ads.ts";
    private const string FrontendListName = "SUGGESTION_KIND_ORDER";

    /// <summary>
    /// The contract as a HAND-WRITTEN literal, deliberately not derived from the enum it checks —
    /// a list generated from the subject would be green under every reordering. Mirrors the
    /// frontend's own independent literal. Appending a member here is the moment to ask whether the
    /// frontend array was appended to as well; <see cref="TheFrontendDecodesTheSameOrderCSharpEmits"/>
    /// answers it rather than trusting the answer.
    /// </summary>
    private static readonly (int Ordinal, string Name)[] Expected =
    [
        (0, "Title"),
        (1, "Region"),
        (2, "Municipality"),
        (3, "OccupationField"),
        (4, "OccupationGroup"),
    ];

    [Fact]
    public void EveryMemberKeepsItsOrdinal()
    {
        var actual = Enum.GetValues<SuggestionKind>()
            .Select(k => ((int)k, k.ToString()))
            .ToArray();

        actual.ShouldBe(
            Expected,
            "SuggestionKind is decoded by ORDINAL in the frontend. A reorder, an inserted member, "
            + "or an explicit = N remaps every kind after the change with nothing failing at the "
            + "boundary. Append at the END, and update the frontend array in the same PR.");
    }

    /// <summary>
    /// The half the ordinal assertion cannot see: what the FE depends on is not the ordinal but the
    /// ordinal REACHING THE WIRE as a bare integer. Attaching a
    /// <see cref="JsonStringEnumConverter"/> keeps every ordinal intact and breaks every decode.
    /// </summary>
    [Fact]
    public void TheKindReachesTheWireAsABareInteger()
    {
        var json = JsonSerializer.Serialize(
            new SuggestionDto(SuggestionKind.Municipality, "PVZL_BQT_XtL", "Goteborg"),
            JsonSerializerOptions.Web);

        json.ShouldContain(
            "\"kind\":2",
            Case.Sensitive,
            "the frontend decodes this member positionally out of SUGGESTION_KIND_ORDER. A string "
            + "form would arrive as a name the positional branch never reads.");
    }

    /// <summary>
    /// The attribute route to a string form, checked on the type itself rather than through a
    /// serializer call that a caller-supplied option set could mask.
    /// </summary>
    [Fact]
    public void TheEnumCarriesNoConverterAttribute()
    {
        typeof(SuggestionKind)
            .GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .ShouldBeEmpty(
                "a [JsonConverter] here changes the wire form without moving a single ordinal, so "
                + "no ordinal assertion can see it.");
    }

    /// <summary>
    /// The global route, which neither of the two facts above can reach: a converter registered in
    /// the Api composition root would change the wire form while the enum, the DTO and
    /// <see cref="JsonSerializerOptions.Web"/> all stay exactly as they are. Read as source text
    /// because the subject IS the composition root's configuration, and resolving the host's real
    /// options would need a WebApplicationFactory this suite deliberately does not have (#1190).
    /// </summary>
    [Fact]
    public void TheApiRegistersNoEnumConverterGlobally()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "src", "Jobbliggaren.Api");
        var offenders = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "JsonStringEnumConverter", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(apiRoot, path))
            .ToArray();

        offenders.ShouldBeEmpty(
            "a globally registered string-enum converter turns every SuggestionKind into a name on "
            + "the wire. The frontend accepts names defensively, but SUGGESTION_KIND_ORDER is the "
            + "positional contract this class exists to hold — change both sides deliberately.");
    }

    /// <summary>
    /// The cross-language join. Compares element-by-element AND in order, so a same-length,
    /// same-membership reordering fails.
    /// </summary>
    [Fact]
    public void TheFrontendDecodesTheSameOrderCSharpEmits()
    {
        var frontendOrder = ReadFrontendKindOrder();

        frontendOrder.ShouldBe(
            Enum.GetNames<SuggestionKind>(),
            $"{FrontendListName} in {FrontendDtoRelativePath} is the frontend's positional decode "
            + "table. It must mirror the C# declaration order exactly. If you appended a member to "
            + "the enum, append it to that array too - in the same PR, at the same position.");
    }

    /// <summary>
    /// Read as source text, for the same reason the Caddyfile pin gives: the file is the artefact,
    /// and a parser clever enough to normalize it could hide the very spelling this join compares.
    /// Finding nothing THROWS rather than returning empty - an empty list would make the comparison
    /// above pass over zero elements the moment the constant is renamed, which is precisely the
    /// vacuous-green this class must not have.
    /// </summary>
    private static string[] ReadFrontendKindOrder()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                FrontendDtoRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        // \b so a future constant merely ENDING in this name cannot be read in its place - a
        // wrong-but-successful read would bypass the throw below.
        var body = Regex.Match(source, $@"\b{FrontendListName}\s*=\s*\[([^\]]*)\]").Groups[1];

        if (!body.Success)
            throw new InvalidOperationException(
                $"Could not read {FrontendListName} out of {FrontendDtoRelativePath}. It was "
                + "renamed or reshaped - re-make this join deliberately, do not delete it.");

        // Drop line comments before harvesting quoted names, so a commented-out member cannot be
        // read as a live one.
        var withoutComments = Regex.Replace(body.Value, @"//[^\n]*", string.Empty);

        var names = Regex.Matches(withoutComments, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        if (names.Length == 0)
            throw new InvalidOperationException(
                $"{FrontendListName} was found in {FrontendDtoRelativePath} but yielded no member "
                + "names. The array shape changed - re-make this join deliberately.");

        return names;
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
