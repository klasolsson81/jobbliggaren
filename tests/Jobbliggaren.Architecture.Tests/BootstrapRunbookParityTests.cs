using System.Runtime.CompilerServices;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0142 part 5a (#1743) — <c>docs/runbooks/vps-deploy-stack.md</c> §3c runs <c>migrate bootstrap</c> by hand.
/// The service it names must be one the deploy compose file declares, and <c>bootstrap</c> a mode
/// <c>Jobbliggaren.Migrate</c> dispatches, or the procedure fails on the box the one time it is run.
/// </summary>
public class BootstrapRunbookParityTests
{
    [Fact]
    public void The_bootstrap_command_names_a_declared_service_and_a_dispatched_mode()
    {
        var root = RepoRoot();
        var runbook = File.ReadAllLines(Path.Combine(root, "docs", "runbooks", "vps-deploy-stack.md"));
        var start = Array.FindIndex(runbook, l => l.StartsWith("## 3c. ", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "vps-deploy-stack.md has no §3c");
        var end = Array.FindIndex(runbook, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));

        var section = runbook[start..(end < 0 ? runbook.Length : end)];
        var command = BashCommands(section).SingleOrDefault(l => l.Contains(" run --rm ", StringComparison.Ordinal));
        command.ShouldNotBeNull("§3c carries no single `run --rm` command");
        var preconditions = Array.FindIndex(section, l => l.StartsWith("**Preconditions", StringComparison.Ordinal));
        Array.FindIndex(section, l => l.StartsWith("sudo flock ", StringComparison.Ordinal))
            .ShouldBeInRange(0, preconditions - 1, "§3c must take the reconcile lock before its preconditions");
        command.ShouldContain("--pull never");
        command.ShouldBe("docker compose -f docker-compose.yml run --rm -T --pull never --no-deps migrate bootstrap "
            + "--expect-history \"$identity_predecessor\" --expect-migrations \"$identity_additions\"");

        File.ReadAllLines(Path.Combine(root, "deploy", "docker-compose.yml")).ShouldContain("  migrate:");
        File.ReadAllText(Path.Combine(root, "src", "Jobbliggaren.Migrate", "Program.cs"))
            .ShouldContain("\"bootstrap\" =>");
    }

    private static List<string> BashCommands(IEnumerable<string> lines)
    {
        List<string> commands = [];
        var inBash = false;
        var pending = string.Empty;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line == "```bash")
            {
                inBash = true;
                continue;
            }
            if (line == "```")
            {
                pending.ShouldBeEmpty("a bash command must not end with an unfinished continuation");
                inBash = false;
                continue;
            }
            if (!inBash)
                continue;
            if (line.StartsWith('#'))
            {
                pending.ShouldBeEmpty("a bash continuation cannot cross a comment line");
                continue;
            }

            var continued = line.EndsWith('\\');
            var body = continued ? line[..^1].TrimEnd() : line;
            pending = pending.Length == 0 ? body : pending + " " + body;
            if (!continued)
            {
                commands.Add(pending);
                pending = string.Empty;
            }
        }
        pending.ShouldBeEmpty("a bash command must not end with an unfinished continuation");
        return commands;
    }

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/BootstrapRunbookParityTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
