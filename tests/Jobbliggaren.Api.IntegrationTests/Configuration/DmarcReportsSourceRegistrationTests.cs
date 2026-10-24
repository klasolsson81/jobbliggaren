using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc.Decoding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the Api reads the stored DMARC reports and does nothing else with them: its composition
/// registers the read and none of the Worker's side, neither the mailbox, the decoder, the ingestion store nor the
/// retention. The mailbox's own project is not referenced by the Api at all (an architecture test pins that); this pins
/// the registrations the Api's shared Infrastructure could still make.
/// </summary>
[Collection("Api")]
public sealed class DmarcReportsSourceRegistrationTests(ApiFactory factory)
{
    private static readonly Type[] WorkerSide =
    [
        typeof(IDmarcMailbox),
        typeof(IDmarcMailboxSession),
        typeof(IDmarcReportStore),
        typeof(IDmarcReportDecoder),
        typeof(IDmarcReportRetention),
        typeof(DmarcReportIngestionJob),
        typeof(DmarcReportRetentionJob),
    ];

    private static readonly Type[] WorkerSideAdapters =
        [typeof(SqlDmarcReportStore), typeof(XmlDmarcReportDecoder), typeof(SqlDmarcReportRetention)];

    [Fact]
    public void AddDmarcReportsSource_RegistersTheScopedSqlRead_AndNothingElse()
    {
        var services = new ServiceCollection();

        services.AddDmarcReportsSource();

        var descriptor = services.ShouldHaveSingleItem();
        descriptor.ServiceType.ShouldBe(typeof(IDmarcReportsSource));
        descriptor.ImplementationType.ShouldBe(typeof(SqlDmarcReportsSource));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheApisComposition_RegistersTheRead_AndNoneOfTheWorkersSide()
    {
        List<ServiceDescriptor>? composed = null;
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => composed = [.. services]));
        _ = host.Services;

        composed.ShouldNotBeNull("the capture must see the Api's composition, or it pins nothing");
        var read = composed.Where(d => d.ServiceType == typeof(IDmarcReportsSource)).ShouldHaveSingleItem();
        read.ImplementationType.ShouldBe(typeof(SqlDmarcReportsSource));
        read.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        foreach (var type in WorkerSide)
        {
            composed.ShouldNotContain(d => d.ServiceType == type, $"the Api registers {type.Name}, which is the Worker's");
        }

        foreach (var adapter in WorkerSideAdapters)
        {
            composed.ShouldNotContain(
                d => d.ServiceType == adapter || (!d.IsKeyedService && d.ImplementationType == adapter),
                $"the Api registers {adapter.Name}, which is the Worker's");
        }
    }

    [Fact]
    public void TheApisContainer_ResolvesTheRead_AndNoneOfTheWorkersSide()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDmarcReportsSource>().ShouldBeOfType<SqlDmarcReportsSource>();
        foreach (var type in WorkerSide)
        {
            scope.ServiceProvider.GetService(type).ShouldBeNull(type.Name);
        }
    }
}
