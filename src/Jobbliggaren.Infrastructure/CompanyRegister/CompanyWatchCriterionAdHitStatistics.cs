using System.Data;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// ADR 0175 D10 — <see cref="ICompanyWatchCriterionAdHitStatistics"/>. Plain <c>ANALYZE</c> on the one table,
/// schema-qualified; never <c>VACUUM ANALYZE</c> (a different change-reason). The AGENTS.md §3.6 argument is
/// <see cref="ScbCompanyRegisterStore.AnalyzeAsync"/>'s and is not restated here.
/// </summary>
internal sealed class CompanyWatchCriterionAdHitStatistics(AppDbContext db) : ICompanyWatchCriterionAdHitStatistics
{
    private const int CommandTimeoutSeconds = 120;

    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = "ANALYZE public.company_watch_criterion_ad_hits;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
