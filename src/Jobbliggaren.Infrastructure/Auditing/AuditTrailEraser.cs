using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Auditing;

/// <summary>
/// PostgreSQL-implementation av <see cref="IAuditTrailEraser"/>.
/// Direct SQL UPDATE bypasser AuditBehavior — porten är arch-låst till
/// HardDeleteAccountsJob via arch-test (ADR 0024 D3 audit-bypass-disciplin).
///
/// Implementationen injicerar konkret <see cref="AppDbContext"/> istället för
/// <see cref="Jobbliggaren.Application.Common.Abstractions.IAppDbContext"/> eftersom
/// <c>Database</c>-facaden inte exponeras på interfacet — det är medvetet
/// (raw SQL ska inte vara tillgängligt från Application-lagret).
/// </summary>
public sealed class AuditTrailEraser(AppDbContext db) : IAuditTrailEraser
{
    public async Task<int> AnonymizeUserAuditTrailAsync(Guid userId, CancellationToken cancellationToken)
    {
        // FormattableString-overload av ExecuteSqlAsync parametriserar {userId}
        // automatiskt — säkert mot SQL-injection. Idempotent eftersom NULL-
        // värden förblir NULL vid repeated runs.
        //
        // Inga FK-cascades påverkas (audit_log har inga FK per ADR 0022).
        // Default-partitionen + alla range-partitions hanteras transparent
        // av PG (UPDATE propageras till rätt partition).
        return await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE audit_log
            SET user_id = NULL,
                ip_address = NULL,
                user_agent = NULL
            WHERE user_id = {userId}
            """,
            cancellationToken);
    }
}
