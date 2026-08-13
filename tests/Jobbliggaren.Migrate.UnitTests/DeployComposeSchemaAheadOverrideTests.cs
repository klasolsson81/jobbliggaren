using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins the deploy stack's wiring of the schema-ahead override (#1236).
///
/// <para>
/// The gate reads <see cref="SchemaAheadGate.OverrideVariableName"/> from its container
/// environment, and compose only forwards variables that are ENUMERATED under an
/// <c>environment:</c> key — there is no <c>env_file</c> anywhere in the deploy stack. Without
/// this line, an operator who follows the refusal message's own instruction (set the value in
/// <c>deploy/.env</c>, re-run the unit) gets the IDENTICAL refusal again: the override would be
/// inert and the message a lie. The pin is what couples the message's promise to the file that
/// keeps it.
/// </para>
///
/// <para>
/// Text assertions for the same reason <see cref="DeployComposeRoleTests"/> gives: a compose
/// file is not .NET configuration, and the property under test IS the literal. The helpers are
/// deliberate small copies of that class's private ones — promoting them would couple two pins
/// that fail for different reasons. Service keys are matched as EXACT lines rather than
/// substrings: the <c>depends_on</c> blocks of api and worker also carry a <c>migrate:</c>
/// token at a deeper indent, and a Contains-match would count them.
/// </para>
/// </summary>
public class DeployComposeSchemaAheadOverrideTests
{
    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"))
            .Split('\n');

    /// <summary>Index of the single line that CONTAINS <paramref name="fragment"/>.</summary>
    private static int IndexOfSingleLineContaining(string fragment) =>
        SingleIndex(line => line.Contains(fragment, StringComparison.Ordinal), fragment);

    /// <summary>Index of the single line that IS <paramref name="exact"/> (CRLF-tolerant).</summary>
    private static int IndexOfExactLine(string exact) =>
        SingleIndex(line => line.TrimEnd('\r') == exact, exact);

    private static int SingleIndex(Func<string, bool> predicate, string described)
    {
        var lines = ComposeLines;
        var hits = Enumerable.Range(0, lines.Length).Where(i => predicate(lines[i])).ToList();
        return hits.Count == 1
            ? hits[0]
            : throw new InvalidOperationException(
                $"deploy/docker-compose.yml has {hits.Count} matching lines for '{described}', " +
                "expected exactly one. If the file was restructured, this pin must be rewritten " +
                "rather than deleted.");
    }

    [Fact]
    public void MigrateService_ForwardsTheOverride_WithASoftDefault()
    {
        var line = ComposeLines[IndexOfSingleLineContaining("MIGRATE_ALLOW_SCHEMA_AHEAD:")];

        // `:-`, not `:?`: the box must be able to `up` with the key undefined — an unset
        // override is the normal state, and a hard requirement here would refuse EVERY hourly
        // apply until the operator invented a value nobody chose.
        line.Split(':', 2)[1].Trim().ShouldBe("${MIGRATE_ALLOW_SCHEMA_AHEAD:-}");
    }

    [Fact]
    public void Override_LivesOnTheMigrateService_NeverInTheSharedConnectionAnchor()
    {
        // migrate-rewrap composes its environment from the x-migrate-app-connection anchor
        // alone. The override is a schema-mode judgment and must not reach the rotation, so the
        // line has to sit inside the migrate service block — after the `migrate:` service key
        // and before its sibling `migrate-rewrap:` — rather than in the anchor both share.
        var overrideIndex = IndexOfSingleLineContaining("MIGRATE_ALLOW_SCHEMA_AHEAD:");
        var migrateIndex = IndexOfExactLine("  migrate:");
        var rewrapIndex = IndexOfExactLine("  migrate-rewrap:");

        overrideIndex.ShouldBeGreaterThan(migrateIndex);
        overrideIndex.ShouldBeLessThan(rewrapIndex);
    }
}
