using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Backup.Queries.GetBackupStatus;

public sealed record GetBackupStatusQuery : IQuery<BackupStatusDto>, IAdminRequest;
