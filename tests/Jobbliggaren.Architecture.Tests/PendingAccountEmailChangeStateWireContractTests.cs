using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1975 — a pending address change's state reaches the admin page as its member NAME, and the page's schema lists
/// the names it accepts. A name the schema does not list fails the whole read, so a rename on either side would show a
/// burned code as a pending change nobody can read. The names are read out of the real enum through the real
/// serializer, and out of the shipped schema as source text, because a backend change runs <c>dotnet test</c>.
/// </summary>
public class PendingAccountEmailChangeStateWireContractTests
{
    private const string FrontendDto = "web/jobbliggaren-web/src/lib/dto/admin-accounts.ts";

    [Fact]
    public void The_page_accepts_exactly_the_names_the_api_writes()
    {
        var written = Enum.GetValues<PendingAccountEmailChangeState>()
            .Select(state => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(state))!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReadAcceptedStates().Order(StringComparer.Ordinal).ToArray().ShouldBe(
            written, $"{FrontendDto} lists the states its read accepts. Change both sides in the same PR.");
    }

    private static string[] ReadAcceptedStates()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), FrontendDto.Replace('/', Path.DirectorySeparatorChar)));
        var list = Regex.Match(
            source, @"\bpendingEmailChangeSchema\s*=\s*z\.object\(\{\s*state:\s*z\.enum\(\[([^\]]*)\]\)").Groups[1];

        list.Success.ShouldBeTrue(
            $"Could not read the state list out of {FrontendDto}. It was renamed or reshaped - re-make this join "
            + "deliberately, do not delete it.");
        return [.. Regex.Matches(list.Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value)];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not find the repo root (CLAUDE.md) walking up from the test bin");
        return dir!.FullName;
    }
}
