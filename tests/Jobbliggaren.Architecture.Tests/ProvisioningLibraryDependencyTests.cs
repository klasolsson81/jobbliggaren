using Jobbliggaren.Migrate;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Enforces the property <c>Jobbliggaren.Migrate.Provisioning</c> exists for (#1232).
///
/// <para>
/// That library holds the ADR 0034 privilege model as data so three assemblies can share it:
/// <c>Jobbliggaren.Migrate</c> executes it, <c>Jobbliggaren.Migrate.UnitTests</c> pins it, and
/// <c>Jobbliggaren.Api.IntegrationTests</c> provisions its migration oracle with it. What makes
/// that sharing safe is that the library carries no data-access or hosting dependency of its own —
/// <b>these types emit SQL text, they never execute it</b>. Without that, an integration-test
/// assembly would inherit a console tool's dependency graph through a <c>src</c> project.
/// </para>
///
/// <para>
/// The csproj states it in a comment. <see cref="PhaseADatabaseGrants"/>' own doc comment states
/// the principle against that: <i>"a precondition documented but not enforced is one the next
/// caller does not know about."</i> So it is enforced here.
/// </para>
///
/// <para>
/// <b>Stated against IL type references, not <c>PackageReference</c> entries</b> — NetArchTest
/// reads the former, and CLAUDE.md §2.1 makes the same distinction for Application's EF Core
/// rule. That is the load-bearing half anyway: an unused package entry changes nothing, while a
/// single type reference is what actually drags a dependency into the graph.
/// </para>
/// </summary>
public class ProvisioningLibraryDependencyTests
{
    // `Roles` lives only in the Provisioning assembly, so this resolves unambiguously despite the
    // library deliberately sharing the `Jobbliggaren.Migrate` root namespace with the console Exe.
    private static readonly System.Reflection.Assembly Provisioning = typeof(Roles).Assembly;

    [Fact]
    public void Provisioning_should_not_reference_data_access_or_hosting()
    {
        var result = Types.InAssembly(Provisioning)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                // System.Data too, and it is not redundant: the csproj carries zero package and
                // zero project references, so the SHARED FRAMEWORK is the only data-access surface
                // reachable WITHOUT touching the csproj. An `ExecuteAsync(DbConnection, ...)`
                // helper added here would pass a list that bans only Npgsql, while making these
                // types execute the SQL they exist to emit.
                "System.Data",
                "Microsoft.EntityFrameworkCore",
                "Hangfire",
                "Microsoft.Extensions",
                "Jobbliggaren.Infrastructure",
                "Jobbliggaren.Application",
                "Jobbliggaren.Domain")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Jobbliggaren.Migrate.Provisioning emits SQL text and never executes it. A data-access " +
            "or hosting reference here would travel into Jobbliggaren.Api.IntegrationTests, which " +
            "references this library precisely because it must NOT reference the console tool. " +
            $"Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Provisioning_is_the_assembly_this_test_thinks_it_is()
    {
        // The vacuity guard. `HaveDependencyOnAny` on an empty or wrong type set passes, and the
        // shared root namespace makes "wrong assembly" a live way to be wrong rather than a
        // theoretical one — a rule pointed at the console Exe would fail loudly, but one pointed
        // at an empty set would pass silently.
        Provisioning.GetName().Name.ShouldBe("Jobbliggaren.Migrate.Provisioning");
        Types.InAssembly(Provisioning).GetTypes().ShouldNotBeEmpty();
    }
}
