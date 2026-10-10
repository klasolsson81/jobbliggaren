using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Backup.Queries.GetBackupStatus;

public sealed class GetBackupStatusQueryHandler(
    IBackupSampleSource source,
    IDateTimeProvider clock) : IQueryHandler<GetBackupStatusQuery, BackupStatusDto>
{
    public async ValueTask<BackupStatusDto> Handle(GetBackupStatusQuery query, CancellationToken cancellationToken)
    {
        var read = await source.ReadAsync(cancellationToken);
        return BackupStatusEvaluator.Evaluate(read, clock.UtcNow);
    }
}
