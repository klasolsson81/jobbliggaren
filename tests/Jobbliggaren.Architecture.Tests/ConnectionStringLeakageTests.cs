using Mono.Cecil;
using Mono.Cecil.Cil;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Architecture-test för TD-48 (Trust=true-läckage). Skannar alla Ldstr-instruktioner
/// i Api/Worker/Infrastructure/Migrate-assemblies via Mono.Cecil IL-introspektion och
/// failar om någon string-literal innehåller <c>"Trust Server Certificate=true"</c>.
///
/// Bakgrund (Fas 1 Block A4 / TD-38): connection-strings för Api+Worker tvingar
/// <c>SSL Mode=VerifyFull</c> (via injicerad config). Unit-test låser separata
/// factory-output, men detta arch-test skyddar hela assemblyn mot framtida
/// inline-konstanter (t.ex. en hårdkodad CS i en helper eller en appsettings-binder).
///
/// Migrate är numera INKLUDERAT (TD-105 / #199): efter AWS-exit (ADR 0066) bygger
/// <c>ConnectionStringFactory.Build</c> connection-strings med konfig-drivet SSL-läge
/// och hårdkodar aldrig <c>Trust Server Certificate=true</c> (den gamla RDS-bootstrap-
/// posturen <c>ForMigrate</c> är borttagen). Migrate omfattas därför av samma
/// läckage-vakt som resten av stacken.
/// </summary>
public class ConnectionStringLeakageTests
{
    private const string ForbiddenSubstring = "Trust Server Certificate=true";

    [Theory]
    [InlineData(typeof(Jobbliggaren.Api.Configuration.HstsOptions))]
    [InlineData(typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser))]
    [InlineData(typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext))]
    [InlineData(typeof(Jobbliggaren.Migrate.ConnectionStringFactory))]
    public void Assembly_should_not_contain_Trust_Server_Certificate_true_in_IL(Type assemblyMarker)
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
                        if (instruction.OpCode != OpCodes.Ldstr)
                            continue;

                        if (instruction.Operand is not string literal)
                            continue;

                        if (literal.Contains(ForbiddenSubstring, StringComparison.OrdinalIgnoreCase))
                        {
                            offenders.Add($"{type.FullName}::{method.Name}");
                        }
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"Trust Server Certificate=true detekterat i {assembly.Name.Name} (TD-48). " +
            $"Connection-strings MÅSTE bygga TLS-postur via injicerad/konfig-driven " +
            $"SSL-läge (aldrig hårdkodad Trust=true). " +
            $"Förekomster: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Pins that no compiled connection-string literal disables Npgsql's reset-on-close (#1232).
    ///
    /// <para>
    /// <c>pg_temp</c> is searched before <c>public</c> when resolving an unqualified relation
    /// name, and EF Core emits unqualified table names. Npgsql issues <c>DISCARD ALL</c> when a
    /// connection returns to the pool; <c>No Reset On Close=true</c> turns that off, so a temp
    /// table surviving a <b>physical</b> pooled connection becomes resolvable by a different
    /// user's later request. PR #1229 granted <c>TEMPORARY</c> on the database to
    /// <c>jobbliggaren_app</c>, which is what makes a temp table creatable on the application's
    /// own connections in the first place — so the library default is now load-bearing.
    /// </para>
    ///
    /// <para>
    /// <b>Asserted on the parsed PROPERTY, never on a substring.</b> Npgsql keyword lookup is
    /// case- and whitespace-insensitive, so <c>No Reset On Close=true</c>,
    /// <c>NoResetOnClose=true</c> and <c>no reset on close=TRUE</c> are one setting with three
    /// spellings. A substring guard — the idiom the sibling test above uses, correctly, for a
    /// literal that has exactly one spelling — would be defeated by the second while reading as
    /// though it covered it.
    /// </para>
    ///
    /// <para>
    /// The YAML surface, where the deploy stack and CI actually compose this value, is pinned
    /// separately in <c>PooledConnectionResetTests</c>: a Cecil literal scan cannot see a YAML
    /// file, and a text scan cannot see a compiled literal. Neither guard covers the other.
    /// </para>
    /// </summary>
    /// <summary>
    /// True when some <c>;</c>-separated segment of <paramref name="literal"/> has a KEY of
    /// <c>host</c> or <c>server</c>. Npgsql keyword lookup ignores case and internal spaces, so
    /// the key is normalised the same way before comparing.
    /// </summary>
    private static bool LooksLikeAConnectionString(string literal) =>
        literal.Split(';').Any(segment =>
        {
            var parts = segment.Split('=', 2);
            if (parts.Length != 2)
                return false;

            var key = parts[0].Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            return key.Equals("host", StringComparison.OrdinalIgnoreCase)
                || key.Equals("server", StringComparison.OrdinalIgnoreCase);
        });

    [Theory]
    [InlineData(typeof(Jobbliggaren.Api.Configuration.HstsOptions))]
    [InlineData(typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser))]
    [InlineData(typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext))]
    [InlineData(typeof(Jobbliggaren.Migrate.ConnectionStringFactory))]
    public void Assembly_should_not_disable_pooled_connection_reset_in_IL(Type assemblyMarker)
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
                        if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string literal)
                            continue;

                        // Only literals that ARE connection strings, decided STRUCTURALLY.
                        //
                        // A substring test for "Host=" is not that test, and this guard found
                        // out by failing: Migrate's own MEL template "Starting migrate:
                        // Host={Host} ..." contains it, and Npgsql then took the entire prefix
                        // "starting migrate: host" as a keyword and threw. So the predicate is
                        // "some semicolon-separated segment has a KEY of host or server", which
                        // is what a connection string is and what a log template is not.
                        if (!LooksLikeAConnectionString(literal))
                            continue;

                        // Deliberately UNGUARDED by try/catch. A literal that looks like a
                        // connection string and will not parse is a finding, not something to
                        // swallow — and a silent catch here is exactly how this guard would go
                        // quiet instead of red.
                        var builder = new NpgsqlConnectionStringBuilder(literal);

                        if (builder.NoResetOnClose)
                        {
                            offenders.Add($"{type.FullName}::{method.Name}");
                        }
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"Reset-on-close avstängt i en connection-string-literal i {assembly.Name.Name} (#1232). " +
            $"pg_temp söks före public och EF skickar okvalificerade tabellnamn, så en temp-tabell " +
            $"som överlever en poolad fysisk anslutning kan läsas av en ANNAN användares senare " +
            $"request. Vill man ändå ha det krävs en accepted-risk-ADR, inte en config-ändring. " +
            $"Förekomster: {string.Join(", ", offenders)}");
    }
}
