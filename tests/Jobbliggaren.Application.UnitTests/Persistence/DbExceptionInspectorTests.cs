using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Persistence;

/// <summary>
/// F2-P8c — Postgres-specifik UNIQUE-violation-detection (SqlState 23505).
/// <see cref="DbExceptionInspector"/> är internal sealed i Infrastructure;
/// vi reflektar fram instansen för isolated unit-test (alternativet är att
/// gå via riktig DB i Worker.IntegrationTests, men SqlState-mappningen är
/// pure logic värd att täcka separat).
/// </summary>
public class DbExceptionInspectorTests
{
    private static IDbExceptionInspector CreateInspector()
    {
        var type = typeof(AppDbContext).Assembly
            .GetType("Jobbliggaren.Infrastructure.Persistence.DbExceptionInspector")
            ?? throw new InvalidOperationException(
                "DbExceptionInspector hittas inte i Infrastructure-assembly.");
        var instance = Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException(
                "Kunde inte instansiera DbExceptionInspector via reflection.");
        return (IDbExceptionInspector)instance;
    }

    private static PostgresException BuildPostgresException(string sqlState)
    {
        // Npgsql 9.x: public ctor (messageText, severity, invariantSeverity, sqlState).
        var ctor = typeof(PostgresException).GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                var ps = c.GetParameters();
                return ps.Length == 4
                       && ps[0].ParameterType == typeof(string)
                       && ps[1].ParameterType == typeof(string)
                       && ps[2].ParameterType == typeof(string)
                       && ps[3].ParameterType == typeof(string);
            })
            ?? throw new InvalidOperationException(
                "PostgresException(string,string,string,string)-konstruktor hittades inte.");
        return (PostgresException)ctor.Invoke(
            ["unit-test-simulerad", "ERROR", "ERROR", sqlState]);
    }

    [Fact]
    public void IsUniqueConstraintViolation_ReturnsTrue_WhenInnerIsPostgresExceptionWith23505()
    {
        var inspector = CreateInspector();
        var pg = BuildPostgresException("23505");
        var dbEx = new DbUpdateException("UNIQUE-violation", pg);

        inspector.IsUniqueConstraintViolation(dbEx).ShouldBeTrue();
    }

    [Fact]
    public void IsUniqueConstraintViolation_ReturnsFalse_WhenInnerHasDifferentSqlState()
    {
        var inspector = CreateInspector();
        // 23502 = not_null_violation
        var pg = BuildPostgresException("23502");
        var dbEx = new DbUpdateException("NOT NULL-violation", pg);

        inspector.IsUniqueConstraintViolation(dbEx).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_ReturnsFalse_WhenInnerIsNotPostgresException()
    {
        var inspector = CreateInspector();
        var dbEx = new DbUpdateException("ingen inner");

        inspector.IsUniqueConstraintViolation(dbEx).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_ReturnsFalse_WhenInnerIsGenericException()
    {
        var inspector = CreateInspector();
        var dbEx = new DbUpdateException(
            "annan orsak", new InvalidOperationException("not a postgres exception"));

        inspector.IsUniqueConstraintViolation(dbEx).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_ThrowsArgumentNullException_WhenExceptionIsNull()
    {
        var inspector = CreateInspector();

        Should.Throw<ArgumentNullException>(
            () => inspector.IsUniqueConstraintViolation(null!));
    }
}
