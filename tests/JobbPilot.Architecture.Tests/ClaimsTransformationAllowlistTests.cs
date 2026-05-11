using System.Reflection;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// Architecture-regler för <see cref="IClaimsTransformation"/>-konsumtion (H-3
/// hardening 2026-05-11).
///
/// <para>
/// Auth-pipeline-ordning: <c>SessionAuthenticationHandler</c> → <c>IClaimsTransformation</c>
/// → authorization-policy. Transformation körs efter authentication-success och
/// kan addera Role-claims eller andra säkerhetskritiska claims. En framtida
/// transformation (t.ex. impersonation-promote-flow, test-only role-injector,
/// federerat IdP-claim-mapping) är säkerhetskritisk och måste passera medveten
/// review.
/// </para>
///
/// <para>
/// Detta test låser konsument-listan i Infrastructure-lagret så att ny
/// <c>IClaimsTransformation</c>-impl bryter build:en tills allowlist:en
/// uppdateras explicit — samma pattern som audit-bypass-portar (ADR 0024 D1).
/// </para>
/// </summary>
public class ClaimsTransformationAllowlistTests
{
    [Fact]
    public void IClaimsTransformation_implementations_in_Infrastructure_must_be_in_allowlist()
    {
        var allowed = new[] { "SessionRoleClaimsTransformation" };

        var implementations = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IClaimsTransformation).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        implementations.ShouldNotBeEmpty(
            "Sanity: minst en IClaimsTransformation måste finnas i Infrastructure.");

        var unauthorized = implementations.Where(impl => !allowed.Contains(impl)).ToList();

        unauthorized.ShouldBeEmpty(
            "Nya IClaimsTransformation-implementationer kräver explicit review innan " +
            "de läggs till denna allowlist. Auth-pipeline:n är säkerhetskritisk — " +
            "nya transformations kan addera Role-claims eller andra privilege-claims. " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }
}
