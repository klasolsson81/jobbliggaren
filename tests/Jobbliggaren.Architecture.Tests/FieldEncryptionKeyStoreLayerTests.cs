using System.Reflection;
using Jobbliggaren.Infrastructure.Persistence;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// TD-13 FAS 3.5 batch C2 — FRÅGA 2-spärr (CTO-triage 2026-05-18,
/// <c>docs/reviews/2026-05-18-td13-stopp-i-cto-triage.md</c>).
///
/// <para>
/// ADR 0049 Beslut 1 lagrar per-användar-wrapped-DEK:er i en
/// <c>user_data_keys</c>-tabell. CTO-domen (FRÅGA 2) mandaterar att denna yta
/// mappas keyless på <see cref="AppDbContext"/> via <c>Set&lt;UserDataKey&gt;()</c>
/// men ALDRIG exponeras via <c>IAppDbContext</c> och ALDRIG refereras från
/// <c>Jobbliggaren.Application</c> — Application-handlers ska aldrig kunna querera
/// nyckelmaterial (ISP/Clean Arch, Martin 2017 kap. 10/22; ADR 0009).
/// </para>
///
/// <para>
/// Arch-testet är en C2 in-scope-leverabel (CTO: "icke-förhandlingsbart, ej
/// TD"). RÖTT tills C2-impl skapar <c>UserDataKey</c>-entiteten — kompilerar
/// inte förrän typen finns (medvetet TDD-kontrakt, CLAUDE.md §2.4/§7).
/// </para>
///
/// C2-impl-krav som dessa tester kodifierar:
/// <list type="bullet">
/// <item><c>Jobbliggaren.Infrastructure.Persistence.UserDataKey</c> — keyless
/// Infrastructure-entitet (EJ Domain, EJ aggregate root).</item>
/// <item><see cref="AppDbContext"/> mappar den via <c>Set&lt;UserDataKey&gt;()</c>
/// (keyless / <c>HasNoKey</c> alt. PK <c>(job_seeker_id, dek_version)</c> i
/// EF-config — designvalet ligger hos C2; arch-testet kräver bara
/// isoleringen).</item>
/// <item>Ingen <c>UserDataKey</c>-medlem i <c>IAppDbContext</c>.</item>
/// <item>Ingen typ i <c>Jobbliggaren.Application</c>-assemblyn refererar
/// <c>UserDataKey</c>.</item>
/// </list>
public class FieldEncryptionKeyStoreLayerTests
{
    private const string UserDataKeyFqn =
        "Jobbliggaren.Infrastructure.Persistence.UserDataKey";

    [Fact]
    public void IAppDbContext_DoesNotExpose_UserDataKey()
    {
        // ISP (Martin 2017 kap. 10): Application-porten exponerar bara
        // aggregate-root-DbSet:ar. UserDataKey är nyckelmaterial — får aldrig
        // läcka via IAppDbContext (varken som DbSet<UserDataKey>, property,
        // metod-parameter eller returtyp).
        var appDbContextPort = typeof(Jobbliggaren.Application.Common.Abstractions.IAppDbContext);

        var members = appDbContextPort
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var leakingMembers = new List<string>();
        foreach (var member in members)
        {
            var signature = member switch
            {
                PropertyInfo p => p.PropertyType.FullName ?? p.PropertyType.Name,
                MethodInfo m => string.Join(
                    ",",
                    new[] { m.ReturnType.FullName ?? m.ReturnType.Name }
                        .Concat(m.GetParameters()
                            .Select(pi => pi.ParameterType.FullName ?? pi.ParameterType.Name))),
                _ => string.Empty,
            };

            if (signature.Contains("UserDataKey", StringComparison.Ordinal))
                leakingMembers.Add($"{member.MemberType} {member.Name}");
        }

        leakingMembers.ShouldBeEmpty(
            "IAppDbContext får ALDRIG exponera UserDataKey (FRÅGA 2, ADR 0049 " +
            "Beslut 1 + Clean Arch ISP). Läckande medlemmar: " +
            string.Join(", ", leakingMembers));
    }

    [Fact]
    public void ApplicationLayer_DoesNotReference_UserDataKey()
    {
        // CTO FRÅGA 2 (b): ingen typ i Jobbliggaren.Application får ha en
        // dependency på UserDataKey. Skulle ge Application-handlers en
        // teoretisk väg till nyckelmaterial.
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(UserDataKeyFqn)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Jobbliggaren.Application får inte referera UserDataKey (FRÅGA 2, " +
            "ISP/Clean Arch). Läckande typer: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_DoesNotReference_FieldEncryption_Or_UserDataKey()
    {
        // ADR 0009 / ADR 0049 Konsekvenser: krypto-laget är ett
        // Infrastructure-bekymmer. Domain ska varken känna till
        // IFieldEncryptor/IDataKeyProvider (Application-portar) eller
        // UserDataKey (Infrastructure-entitet) — persistensartefakt läcker
        // aldrig in i aggregatet (Evans 2003). Fas 4b PR-9a (ADR 0100)
        // utökar spärren med Form C-portarna: ResumeFile tar emot FÄRDIG-
        // förseglade bytes och får aldrig själv känna till cipher/sealer.
        var result = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.Entity<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application.Common.Security.IFieldEncryptor",
                "Jobbliggaren.Application.Common.Security.IDataKeyProvider",
                "Jobbliggaren.Application.Common.Security.GeneratedDataKey",
                "Jobbliggaren.Application.Common.Security.IBinaryFieldEncryptor",
                "Jobbliggaren.Application.Common.Security.IBinaryFieldSealer",
                "Jobbliggaren.Application.Common.Security.IBinaryFieldOpener",
                UserDataKeyFqn,
                "Jobbliggaren.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot fält-kryptering / UserDataKey (ADR 0009/0049): " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
