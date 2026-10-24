using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc.Decoding;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Worker.IntegrationTests.Email.Dmarc.DmarcTestReports;

namespace Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the DMARC ingestion and retention resolve through the registration
/// <c>Worker/Program.cs</c> makes (<see cref="DmarcReportIngestionRegistration.AddDmarcReportIngestion"/>, which the
/// fixture calls rather than mirrors). The fixture configures no mailbox password, so the mailbox is the not-configured
/// one: it never connects, and a run only records that it is not configured.
/// </summary>
[Collection("Worker")]
public sealed class DmarcReportIngestionRegistrationIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await ClearAsync(fixture.ConnectionString);

    public async ValueTask DisposeAsync() => await ClearAsync(fixture.ConnectionString);

    [Fact]
    public void TheWrappersTheJobsAndTheirPorts_ResolveThroughTheHostsRegistration()
    {
        using var scope = fixture.Services.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<DmarcReportIngestionWorker>().ShouldNotBeNull();
        provider.GetRequiredService<DmarcReportRetentionWorker>().ShouldNotBeNull();
        provider.GetRequiredService<DmarcReportIngestionJob>().ShouldNotBeNull();
        provider.GetRequiredService<DmarcReportRetentionJob>().ShouldNotBeNull();
        provider.GetRequiredService<IDmarcReportStore>().ShouldBeOfType<SqlDmarcReportStore>();
        provider.GetRequiredService<IDmarcReportRetention>().ShouldBeOfType<SqlDmarcReportRetention>();
        provider.GetRequiredService<IDmarcReportDecoder>().ShouldBeOfType<XmlDmarcReportDecoder>();
    }

    [Fact]
    public async Task TheMailbox_IsTheNotConfiguredOne_WhenNoPasswordIsSet()
    {
        fixture.Services.GetRequiredService<IConfiguration>()[DmarcMailboxCollectionExtensions.PasswordKey]
            .ShouldBeNull("the fixture must configure no mailbox password, or this pins nothing");

        var mailbox = fixture.Services.GetRequiredService<IDmarcMailbox>();

        mailbox.GetType().FullName.ShouldBe("Jobbliggaren.Infrastructure.Mailbox.Dmarc.NotConfiguredDmarcMailbox");
        (await mailbox.OpenAsync(Ct)).ShouldBeOfType<DmarcMailboxOpen.NotConfigured>();
    }

    [Fact]
    public async Task TheHourlyWrapper_WithoutAPassword_RecordsNotConfigured_AndStoresNothing()
    {
        using (var scope = fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DmarcReportIngestionWorker>().RunAsync(Ct);
        }

        using var read = fixture.Services.CreateScope();
        var state = await read.ServiceProvider.GetRequiredService<IDmarcReportStore>().ReadStateAsync(Ct);
        state.ShouldNotBeNull("the run records how it ended");
        state.LastOutcome.ShouldBe(DmarcRunOutcome.NotConfigured);
        state.LastSuccessAt.ShouldBeNull();
        state.Cursor.ShouldBeNull("a mailbox that was never opened has no epoch");
        (await ReadStringsAsync(fixture.ConnectionString,
                "SELECT count(*)::text FROM dmarc_reports UNION ALL SELECT count(*)::text FROM dmarc_mailbox_messages"))
            .ShouldBe(["0", "0"]);
    }
}
