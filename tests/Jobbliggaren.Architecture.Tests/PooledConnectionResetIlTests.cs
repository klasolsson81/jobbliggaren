using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Pins that no compiled code disables Npgsql's reset-on-close (#1232) — the IL half of a
/// two-surface guard whose YAML half is <c>PooledConnectionResetTests</c> in
/// <c>Jobbliggaren.Migrate.UnitTests</c>.
///
/// <para>
/// <b>The risk.</b> <c>pg_temp</c> is searched before <c>public</c> when Postgres resolves an
/// unqualified relation name, and EF Core emits unqualified table names. Npgsql resets a
/// connection on return to the pool — <c>DISCARD ALL</c>, or the narrow set that still includes
/// <c>DISCARD TEMP</c> when prepared statements are live — and <c>No Reset On Close=true</c>
/// turns that off. A temp table surviving a <b>physical</b> pooled connection is then resolvable
/// by a <i>different user's</i> later request. PR #1229 granted <c>TEMPORARY</c> to
/// <c>jobbliggaren_app</c>, so reset-on-close is the load-bearing layer — and "just revoke
/// TEMPORARY" is not available, because two applied migrations create temp tables.
/// </para>
///
/// <para>
/// <b>KEYED ON THE SETTING, NOT ON THE CONTAINER, and that is the whole design.</b> The first
/// version of this guard only considered literals that were <i>themselves</i> connection strings
/// (a segment with a <c>host</c>/<c>server</c> key). Both `code-reviewer` and `security-auditor`
/// measured the same blind spot independently: the likeliest regression is an append —
/// <c>GetConnectionString("Postgres") + ";No Reset On Close=true"</c> — which compiles to the
/// standalone literal <c>";No Reset On Close=true"</c>, carrying no host segment at all. It was
/// skipped. So the predicate now asks only "does any segment set this key to true", which the
/// append form satisfies and a connection string containing it also satisfies.
/// </para>
///
/// <para>
/// <b>And the setter, because it emits no string at all.</b>
/// <c>new NpgsqlConnectionStringBuilder(cs) { NoResetOnClose = true }</c> produces no
/// <c>ldstr</c> whatsoever — only a <c>call</c> to <c>set_NoResetOnClose</c>. A literal-only scan
/// is structurally blind to it, so the same walk also flags that call.
/// </para>
///
/// <para>
/// Npgsql keyword lookup ignores case and internal spaces, so <c>No Reset On Close</c>,
/// <c>NoResetOnClose</c> and <c>no reset on close</c> are one key with many spellings; the key is
/// normalised the same way before comparison. There is no longer any call to
/// <c>NpgsqlConnectionStringBuilder</c>'s parser here — the earlier version's unguarded parse
/// threw on a MEL template that merely contained <c>Host=</c>, and keying on the setting removes
/// the need to interpret the surrounding text at all.
/// </para>
/// </summary>
public class PooledConnectionResetIlTests
{
    private const string Key = "noresetonclose";
    private const string Setter = "set_NoResetOnClose";

    /// <summary>
    /// True when some <c>;</c>-separated segment of <paramref name="literal"/> sets the
    /// reset-on-close key to a true value. Key normalisation mirrors Npgsql's: case-insensitive,
    /// internal spaces ignored.
    /// </summary>
    internal static bool DisablesResetOnClose(string literal)
    {
        foreach (var segment in literal.Split(';'))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            if (!key.Equals(Key, StringComparison.OrdinalIgnoreCase))
                continue;

            // Npgsql rejects anything that is not a bool here, so only `true` matters; a
            // non-bool value cannot reach a live connection at all.
            if (bool.TryParse(parts[1].Trim(), out var enabled) && enabled)
                return true;
        }

        return false;
    }

    [Theory]
    [InlineData(typeof(Jobbliggaren.Api.Configuration.HstsOptions))]
    [InlineData(typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser))]
    [InlineData(typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext))]
    [InlineData(typeof(Jobbliggaren.Migrate.ConnectionStringFactory))]
    public void Assembly_should_not_disable_pooled_connection_reset(Type assemblyMarker)
    {
        var assemblyPath = assemblyMarker.Assembly.Location;
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var offenders = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode == OpCodes.Ldstr
                            && instruction.Operand is string literal
                            && DisablesResetOnClose(literal))
                        {
                            offenders.Add($"{type.FullName}::{method.Name} (literal)");
                        }

                        if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                            && instruction.Operand is MethodReference called
                            && called.Name == Setter
                            && called.DeclaringType.Name == nameof(Npgsql.NpgsqlConnectionStringBuilder))
                        {
                            offenders.Add($"{type.FullName}::{method.Name} ({Setter})");
                        }
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"Reset-on-close avstängt i {assembly.Name.Name} (#1232). pg_temp söks före public och " +
            $"EF skickar okvalificerade tabellnamn, så en temp-tabell som överlever en poolad " +
            $"fysisk anslutning kan läsas av en ANNAN användares senare request. Vill man ändå ha " +
            $"det krävs en accepted-risk-ADR, inte en config-ändring. " +
            $"Förekomster: {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("No Reset On Close=true")]
    [InlineData("NoResetOnClose=true")]
    [InlineData("no reset on close=TRUE")]
    [InlineData("  NoResetOnClose  =  true  ")]
    [InlineData("Host=h;Database=d;No Reset On Close=true")]
    [InlineData(";No Reset On Close=true")]
    public void Predicate_catches_every_spelling_including_the_bare_append(string literal)
    {
        // THE VACUITY GUARD, and it is a self-test rather than a corpus count on purpose.
        //
        // Measured: only TWO literals in all four scanned assemblies are even candidates today,
        // both design-time factories in Infrastructure — so three of the four theory rows above
        // assert over an empty set and would pass no matter what the predicate did. A guard whose
        // corpus is empty cannot demonstrate anything about itself, so the predicate is exercised
        // directly instead.
        //
        // The last case is the one that motivated the rewrite: `cs + ";No Reset On Close=true"`
        // compiles to exactly that standalone literal, and the previous host-keyed predicate
        // skipped it.
        DisablesResetOnClose(literal).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Host=postgres;Port=5432;Database=jobbliggaren;Username=jobbliggaren_app")]
    [InlineData("Starting Migrate: host={Host}:{Port} db={Db}")]
    [InlineData("No Reset On Close=false")]
    [InlineData("NoResetOnCloseSomethingElse=true")]
    [InlineData("")]
    public void Predicate_does_not_fire_on_anything_else(string literal)
    {
        // The other half of the control. The MEL template is not hypothetical: an earlier version
        // of this guard parsed candidate literals with NpgsqlConnectionStringBuilder, and that
        // template — which merely contains `host=` — made Npgsql take the whole prefix as a
        // keyword and throw, turning one architecture test red.
        DisablesResetOnClose(literal).ShouldBeFalse();
    }
}
