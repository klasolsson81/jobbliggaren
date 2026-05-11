using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// Architecture-test för TD-48 (Trust=true-läckage). Skannar alla Ldstr-instruktioner
/// i Api/Worker/Infrastructure-assemblies via Mono.Cecil IL-introspektion och failar
/// om någon string-literal innehåller <c>"Trust Server Certificate=true"</c>.
///
/// Bakgrund (Fas 1 Block A4 / TD-38): connection-strings för Api+Worker går nu via
/// <c>ConnectionStringFactory.ForPersisted</c> som tvingar <c>SSL Mode=VerifyFull</c>.
/// Unit-test i <c>JobbPilot.Migrate.UnitTests</c> låser factory:ns output, men skyddar
/// inte hela Api/Worker/Infrastructure mot framtida inline-konstanter (t.ex. en
/// <c>NpgsqlConnectionStringBuilder { ConnectionString = "...Trust=true..." }</c>
/// i en helper eller en hårdkodad CS i appsettings-binder).
///
/// Migrate exkluderas explicit — <c>ConnectionStringFactory.ForMigrate</c> har
/// Trust=true by design eftersom bootstrap-DDL kör innan root-cert finns i container
/// (ADR 0024 / TD-38-runbook).
/// </summary>
public class ConnectionStringLeakageTests
{
    private const string ForbiddenSubstring = "Trust Server Certificate=true";

    [Theory]
    [InlineData(typeof(JobbPilot.Api.Configuration.HstsOptions))]
    [InlineData(typeof(JobbPilot.Worker.Auditing.WorkerSystemUser))]
    [InlineData(typeof(JobbPilot.Infrastructure.Persistence.AppDbContext))]
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
            $"Connection-strings för Api/Worker MÅSTE använda SSL Mode=VerifyFull + " +
            $"Root Certificate (se ConnectionStringFactory.ForPersisted). " +
            $"Förekomster: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Migrate_assembly_is_intentionally_excluded_from_scan()
    {
        // Sanity-check: Migrate har Trust=true by design (bootstrap-DDL kör innan
        // root-cert finns i container). Detta test dokumenterar undantaget via
        // assertion mot ConnectionStringFactory.ForMigrate-output. Om Migrate
        // någonsin slutar använda Trust=true ska detta test failera och då kan
        // Migrate inkluderas i [Theory] ovan.
        var migrateAssembly = typeof(JobbPilot.Migrate.ConnectionStringFactory).Assembly;
        var assemblyPath = migrateAssembly.Location;
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var hasTrustTrue = false;
        foreach (var module in assembly.Modules)
            foreach (var type in module.GetTypes())
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode == OpCodes.Ldstr
                            && instruction.Operand is string s
                            && s.Contains(ForbiddenSubstring, StringComparison.OrdinalIgnoreCase))
                        {
                            hasTrustTrue = true;
                        }
                    }
                }

        hasTrustTrue.ShouldBeTrue(
            "Migrate-assembly förväntas innehålla Trust=true (ForMigrate by design). " +
            "Om denna assertion failar är det förmodligen positivt — Migrate har gått " +
            "över till VerifyFull-bootstrap, och då kan undantaget i [Theory]-arch-testet " +
            "tas bort. Granska och uppdatera ConnectionStringLeakageTests.");
    }
}
