using System.Reflection;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;
using Jobbliggaren.Infrastructure.Admin.Email;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 (ADR 0178, PR 2) — the provider reads' boundaries. Each provider port has only its named admin query handlers
/// as readers; Application owns the arithmetic and none of the transport; the reads never reach the sending key's
/// options; and the Worker, which sends mail, never reads the provider.
/// </summary>
public class EmailAllowanceBoundaryTests
{
    private static readonly string ProviderReadNamespace = typeof(EmailUsageSourceCollectionExtensions).Namespace!;

    public static TheoryData<string, string[]> PortConsumers() => new()
    {
        { typeof(IEmailProviderUsageSource).FullName!, [typeof(GetEmailAllowanceQueryHandler).FullName!] },
        {
            typeof(IEmailDeliveryStatisticsSource).FullName!,
            [
                typeof(GetEmailDeliveryQueryHandler).FullName!,
                typeof(GetEmailRecentFailuresQueryHandler).FullName!,
                typeof(GetEmailSendCountsQueryHandler).FullName!,
            ]
        },
        { typeof(IEmailRecentFailuresSource).FullName!, [typeof(GetEmailRecentFailuresQueryHandler).FullName!] },
    };

    [Theory]
    [MemberData(nameof(PortConsumers))]
    public void EachProviderPort_IsInjectedOnlyIntoItsNamedQueryHandlers(string port, string[] handlers)
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assembly[] owned =
        [
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Api.Endpoints.AdminEmailEndpoints).Assembly,
            typeof(WorkerSystemUser).Assembly,
            typeof(Jobbliggaren.Infrastructure.Mailbox.Dmarc.DmarcMailboxCollectionExtensions).Assembly,
            typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
        ];

        var consumers = owned
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(declared).Cast<MethodBase>().Concat(type.GetMethods(declared))
                .Any(member => member.GetParameters().Any(parameter => parameter.ParameterType.FullName == port)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe([.. handlers.Order(StringComparer.Ordinal)],
            $"a new reader of {port} needs the admin gate and #1981's review. Found: " + string.Join(", ", consumers));
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
                "System.Text.Json.JsonSerializer",
                // #1981 PR 4a: the DMARC report's wire format, its compression and the mailbox transport are the
                // adapters' too. System.Net.Sockets stays allowed: an address family is vocabulary, not transport.
                "System.Xml",
                "System.IO.Compression",
                "System.Net.Security",
                "MailKit",
                "MimeKit")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The wire format and the transport belong to the adapters: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void NoReadSideType_DependsOnTheSendingKeysOptions_ExceptTheRegistration()
    {
        var readSide = Types.InAssembly(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .That().ResideInNamespace(ProviderReadNamespace)
            .And().DoNotHaveName(nameof(EmailUsageSourceCollectionExtensions));
        readSide.GetTypes().ShouldContain(typeof(ScalewayReadTransport), "the rule must see the transport, or it pins nothing");

        var result = readSide.ShouldNot().HaveDependencyOn(typeof(ScalewayEmailOptions).FullName!).GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "a provider read authenticates with the read key alone (ScalewayReadOptions), never the sending key's options: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void TheWorker_ShouldNotReferenceTheProviderReads()
    {
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot().HaveDependencyOnAny(ProviderReadNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The provider reads are Api-only, the Worker references them: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
