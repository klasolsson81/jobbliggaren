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

        var command = runbook[start..(end < 0 ? runbook.Length : end)]
            .SingleOrDefault(l => l.Contains(" run --rm ", StringComparison.Ordinal));
        command.ShouldNotBeNull("§3c carries no single `run --rm` command");
        command.ShouldContain("--pull never");
        command.Trim().ShouldEndWith(" migrate bootstrap");

        File.ReadAllLines(Path.Combine(root, "deploy", "docker-compose.yml")).ShouldContain("  migrate:");
        File.ReadAllText(Path.Combine(root, "src", "Jobbliggaren.Migrate", "Program.cs"))
            .ShouldContain("\"bootstrap\" =>");
    }

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/BootstrapRunbookParityTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
