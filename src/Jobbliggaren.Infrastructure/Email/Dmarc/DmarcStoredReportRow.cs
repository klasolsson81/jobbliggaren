using System.Net;
using Jobbliggaren.Application.Admin.Email.Dmarc;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — one record of a stored report in <c>dmarc_report_rows</c>, at its position in the report.
/// The source address is personal data (C-582/14 <i>Breyer</i>); the rest is a count, our own domain and two results.
/// Deleted with its report by the database's cascade.
/// </summary>
internal sealed class DmarcStoredReportRow
{
    private DmarcStoredReportRow()
    {
    }

    public Guid ReportId { get; private set; }

    public int Ordinal { get; private set; }

    public IPAddress SourceIp { get; private set; } = null!;

    public int MessageCount { get; private set; }

    public string HeaderFrom { get; private set; } = null!;

    public DmarcAlignment DkimAligned { get; private set; }

    public DmarcAlignment SpfAligned { get; private set; }
}
