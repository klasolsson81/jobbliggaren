using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #616 anti-regression (CTO-bind Variant B). The HIBP breach-check must respect Clean Arch:
/// the port lives in Application and stays HTTP-agnostic — the AspNetCore half of that
/// condition is pinned by <c>DomainLayerTests</c>, and the <c>System.Net.Http</c> half by
/// <see cref="Application_should_not_depend_on_HttpClient"/> below (the pre-existing
/// System.Net.Http pin in ScbCompanyRegisterLayerTests is namespace-scoped to CompanyRegister
/// and does not cover Common.Abstractions). ALL wire/adapter types stay internal to
/// Infrastructure so Application/Api/Worker can never couple to the HIBP protocol or bypass
/// the port. JobSourceLayerTests parity.
/// </summary>
public class BreachCheckLayerTests
{
    [Fact]
    public void IBreachedPasswordChecker_is_in_Application_layer()
    {
        // The port is an Application abstraction (CTO-bind FORK 2) — Infrastructure both
        // implements it (HibpPasswordBreachClient) and consumes it (PwnedPasswordValidator).
        var port = typeof(Jobbliggaren.Application.Common.Abstractions.IBreachedPasswordChecker);
        port.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);

        var verdict = typeof(Jobbliggaren.Application.Common.Abstractions.BreachCheckVerdict);
        verdict.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void Application_should_not_depend_on_HttpClient()
    {
        // The other half of the CTO-bind FORK 2 condition (AspNetCore is pinned by
        // DomainLayerTests): the port must stay HTTP-agnostic — a future HttpClient-typed
        // member on IBreachedPasswordChecker (or anywhere in Application) must fail here.
        // The SDK's implicit `global using System.Net.Http` alone creates no type dependency.
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot System.Net.Http: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void BreachCheck_wire_types_are_internal_to_Infrastructure()
    {
        // The HIBP client + disabled fallback must be internal so nothing outside Infrastructure
        // can construct or reference them directly (port-only access). BreachCheckOptions is the
        // deliberate public exception (IOptions binding target, JobTechOptions parity).
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var publicBreachCheckTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.Security.BreachCheck"
                        && t.IsPublic
                        && t.Name != "BreachCheckOptions")
            .Select(t => t.FullName)
            .ToList();

        publicBreachCheckTypes.ShouldBeEmpty(
            "HIBP-adaptertyper ska vara internal (BreachCheckOptions är det medvetna undantaget). " +
            $"Public: {string.Join(", ", publicBreachCheckTypes!)}");
    }

    [Fact]
    public void PwnedPasswordValidator_is_internal()
    {
        // The Identity validator is an Infrastructure wiring detail (Api-only Identity chain,
        // ADR 0023 keeps the Worker composition free of it) — never a public surface.
        var validator = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly
            .GetTypes()
            .SingleOrDefault(t => t.Name == "PwnedPasswordValidator");

        validator.ShouldNotBeNull("PwnedPasswordValidator saknas i Infrastructure (#616).");
        validator.IsPublic.ShouldBeFalse(
            "PwnedPasswordValidator ska vara internal — konsumeras endast via Identity-kedjan.");
    }
}
