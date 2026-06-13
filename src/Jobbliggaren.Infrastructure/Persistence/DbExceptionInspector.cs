using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Persistence;

/// <summary>
/// Npgsql-specifik impl av <see cref="IDbExceptionInspector"/>. PostgresException
/// SqlState <c>23505</c> = unique_violation per PostgreSQL 18.3 error-codes-tabell.
/// </summary>
internal sealed class DbExceptionInspector : IDbExceptionInspector
{
    private const string PostgresUniqueViolationSqlState = "23505";

    public bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is PostgresException pg
               && pg.SqlState == PostgresUniqueViolationSqlState;
    }
}
