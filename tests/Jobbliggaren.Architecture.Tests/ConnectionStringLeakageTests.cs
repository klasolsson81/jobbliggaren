using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Architecture-test för TD-48 (Trust=true-läckage). Skannar alla Ldstr-instruktioner
/// i varje produktionsassembly (<see cref="ProductionAssemblies"/>) via Mono.Cecil IL-introspektion och
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

    public static TheoryData<string> EveryAssembly() => ProductionAssemblies.Names();

    [Theory]
    [MemberData(nameof(EveryAssembly))]
    public void Assembly_should_not_contain_Trust_Server_Certificate_true_in_IL(string name)
    {
        var production = ProductionAssemblies.Named(name);
        using var assembly = production.ReadDefinition();

        var offenders = new List<string>();
        var anchorBodiesRead = 0;

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    if (type.FullName == production.Anchor.FullName)
                        anchorBodiesRead++;

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

        anchorBodiesRead.ShouldBeGreaterThan(0, $"{name}: the scan read none of the anchor's method bodies, so an empty offender list proves nothing");
        offenders.ShouldBeEmpty(
            $"Trust Server Certificate=true detekterat i {assembly.Name.Name} (TD-48). " +
            $"Connection-strings MÅSTE bygga TLS-postur via injicerad/konfig-driven " +
            $"SSL-läge (aldrig hårdkodad Trust=true). " +
            $"Förekomster: {string.Join(", ", offenders)}");
    }
}
