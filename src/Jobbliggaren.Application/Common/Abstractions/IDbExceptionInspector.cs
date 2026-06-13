using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Application-port för provider-specifik <see cref="DbUpdateException"/>-analys.
/// Infrastructure-impl (<c>DbExceptionInspector</c>) binder mot Npgsql:s
/// <c>PostgresException.SqlState</c> ("23505" = unique_violation) utan att
/// Application får direkt-beroende på Npgsql (CLAUDE.md §5.1 + DIP per
/// Martin 2017 kap. 11). Används av race-säkra upsert-handlers (ADR 0032 §5)
/// för att skilja UNIQUE-constraint-violation från andra DbUpdateException-
/// orsaker (concurrency tokens, FK-violations, NOT NULL-violations, etc.).
/// </summary>
public interface IDbExceptionInspector
{
    /// <summary>
    /// True om <paramref name="exception"/> är en UNIQUE-index-violation
    /// (Postgres SqlState <c>23505</c>). Triggar reload + update i upsert-flöden.
    /// </summary>
    bool IsUniqueConstraintViolation(DbUpdateException exception);
}
