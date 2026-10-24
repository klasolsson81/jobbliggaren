using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence.Configurations;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0184) — where the stored DMARC reports may go. Their configurations map the four tables, the
/// ingestion store, the page's read and the retention reach them, the Art. 17 registry classifies the report columns,
/// and nothing else in the production assemblies names a table or references an entity. The sites are the IL's own, by
/// owning type; EF migrations and model snapshots are admitted by kind (<see cref="EmailSendCountBoundaryTests"/>). The
/// test cannot see SQL assembled without a table's name, or a read made outside the code (CLAUDE.md §9.2 (b)).
/// </summary>
public class DmarcReportStorageBoundaryTests
{
    private const string Bound =
        "ADR 0184: the reports hold third parties' addresses and reach the page through one read; a new reader or "
        + "writer is a new use of that data and needs its own review.";

    [Fact]
    public void Only_the_configurations_the_store_the_read_and_the_erasure_registry_name_the_reports()
        => OwnersNaming("dmarc_reports").ShouldBe(
            [
                typeof(ErasureCascadeRegistry).FullName!,
                typeof(DmarcStoredReportConfiguration).FullName!,
                typeof(DmarcStoredReportRowConfiguration).FullName!,
                typeof(SqlDmarcReportStore).FullName!,
                typeof(SqlDmarcReportsSource).FullName!,
            ],
            ignoreOrder: true,
            Bound);

    [Fact]
    public void Only_the_configuration_the_store_the_read_and_the_erasure_registry_name_the_records()
        => OwnersNaming("dmarc_report_rows").ShouldBe(
            [
                typeof(ErasureCascadeRegistry).FullName!,
                typeof(DmarcStoredReportRowConfiguration).FullName!,
                typeof(SqlDmarcReportStore).FullName!,
                typeof(SqlDmarcReportsSource).FullName!,
            ],
            ignoreOrder: true,
            Bound);

    [Fact]
    public void Only_the_configuration_the_store_and_the_read_name_the_ledger()
        => OwnersNaming("dmarc_mailbox_messages").ShouldBe(
            [
                typeof(DmarcMailboxMessageConfiguration).FullName!,
                typeof(SqlDmarcReportStore).FullName!,
                typeof(SqlDmarcReportsSource).FullName!,
            ],
            ignoreOrder: true,
            Bound);

    [Fact]
    public void Only_the_configuration_the_store_and_the_shared_state_read_name_the_state_row()
        => OwnersNaming("dmarc_mailbox_state").ShouldBe(
            [
                typeof(DmarcMailboxStateConfiguration).FullName!,
                typeof(SqlDmarcReportStore).FullName!,
                typeof(DmarcStateRow).FullName!,
            ],
            ignoreOrder: true,
            Bound);

    [Fact]
    public void Only_the_configurations_and_the_retention_reference_the_report_entities()
    {
        ProductionTypeSweep.OwnersReferencing(typeof(DmarcStoredReport).FullName!).ShouldBe(
            [
                typeof(DmarcStoredReportConfiguration).FullName!,
                typeof(DmarcStoredReportRowConfiguration).FullName!,
                typeof(SqlDmarcReportRetention).FullName!,
            ],
            ignoreOrder: true,
            Bound);
        ProductionTypeSweep.OwnersReferencing(typeof(DmarcStoredReportRow).FullName!).ShouldBe(
            [typeof(DmarcStoredReportRowConfiguration).FullName!], ignoreOrder: true, Bound);
    }

    [Fact]
    public void Only_the_configurations_and_the_retention_reference_the_ledger_and_state_entities()
    {
        ProductionTypeSweep.OwnersReferencing(typeof(DmarcMailboxMessage).FullName!).ShouldBe(
            [typeof(DmarcMailboxMessageConfiguration).FullName!, typeof(SqlDmarcReportRetention).FullName!],
            ignoreOrder: true,
            Bound);
        ProductionTypeSweep.OwnersReferencing(typeof(DmarcMailboxState).FullName!).ShouldBe(
            [typeof(DmarcMailboxStateConfiguration).FullName!], ignoreOrder: true, Bound);
    }

    private static List<string> OwnersNaming(string table) =>
    [
        .. EmailSendCountBoundaryTests.SitesNaming(table)
            .Select(OwnerOfSite)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The type part of a <c>Type.Method</c> site key; a constructor's is <c>Type..ctor</c>.</summary>
    private static string OwnerOfSite(string site)
    {
        var dot = site.LastIndexOf('.');
        if (dot > 0 && site[dot - 1] == '.')
            dot--;
        return site[..dot];
    }
}
