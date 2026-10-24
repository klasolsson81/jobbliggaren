namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — one row of <c>dmarc_reports</c>: an aggregate report ingestion accepted, keyed by its
/// reporter, the reporter's id and a digest of its content. Written by <see cref="SqlDmarcReportStore"/> only.
/// Infrastructure-internal (the ADR 0172 D3 form): never a <c>DbSet</c> on <c>IAppDbContext</c>.
/// </summary>
internal sealed class DmarcStoredReport
{
    private DmarcStoredReport()
    {
    }

    public Guid Id { get; private set; }

    public string Reporter { get; private set; } = null!;

    public string ReporterReportId { get; private set; } = null!;

    public byte[] ContentSha256 { get; private set; } = null!;

    public DateTimeOffset BeginsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
}
