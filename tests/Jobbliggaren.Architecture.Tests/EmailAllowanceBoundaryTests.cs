using System.Reflection;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
using Jobbliggaren.Infrastructure.Admin.Email;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 (ADR 0177) — the allowance read's boundaries. The provider port has one reader, the admin query; Application
/// owns the arithmetic and none of the transport; and the Worker, which sends mail, never reads the provider.
/// </summary>
public class EmailAllowanceBoundaryTests
{
    private static readonly string[] ProviderReadTypes =
    [
        typeof(ScalewayUsageSource).FullName!,
        typeof(MemoizedEmailProviderUsageSource).FullName!,
        typeof(EmailUsageSourceCollectionExtensions).FullName!,
    ];

    [Fact]
    public void TheProviderPort_IsInjectedOnlyIntoItsOneQueryHandler()
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assembly[] owned =
        [
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Api.Endpoints.AdminEmailEndpoints).Assembly,
            typeof(WorkerSystemUser).Assembly,
            typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
        ];

        var consumers = owned
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(declared).Cast<MethodBase>().Concat(type.GetMethods(declared))
                .Any(member => member.GetParameters().Any(parameter => parameter.ParameterType == typeof(IEmailProviderUsageSource))))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe([typeof(GetEmailAllowanceQueryHandler).FullName!],
            "a new reader of the provider's usage needs the admin gate and ADR 0177's review. Found: " + string.Join(", ", consumers));
    }

    [Fact]
    public void TheApplicationSide_HoldsNoTransportAndNoJsonParsing()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That().ResideInNamespaceStartingWith(typeof(IEmailProviderUsageSource).Namespace!)
            .ShouldNot().HaveDependencyOnAny(
                "System.Net.Http",
                "System.Text.Json.JsonDocument",
                "System.Text.Json.Utf8JsonReader",
                "System.Text.Json.JsonSerializer")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The provider's wire format belongs to the adapter: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void TheWorker_ShouldNotReferenceTheProviderRead()
    {
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot().HaveDependencyOnAny(ProviderReadTypes)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The allowance read is Api-only, the Worker references it: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
