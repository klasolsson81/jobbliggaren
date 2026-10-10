namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// The Backup card's one source: what the host sampler last published about the backup stamp and the
/// backup timer. Application defines the port because it cannot read a host directory itself (AGENTS.md
/// §2.1); Infrastructure implements it. The only consumer is <c>GetBackupStatusQueryHandler</c>, which an
/// architecture test pins.
/// </summary>
public interface IBackupSampleSource
{
    /// <summary>Never throws for a missing, unreadable or malformed file; those are reasons on the result.</summary>
    ValueTask<BackupSampleRead> ReadAsync(CancellationToken cancellationToken);
}
