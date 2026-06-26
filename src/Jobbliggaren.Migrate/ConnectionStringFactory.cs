using Npgsql;

namespace Jobbliggaren.Migrate;

/// <summary>
/// Bygger Postgres-connection-strings för Migrate-DDL-flow (master- och
/// roll-anslutningar) med <em>konfig-driven</em> TLS-postur.
///
/// Efter AWS-exit (TD-105 / ADR 0050 / ADR 0066) är RDS-specifik postur död: den
/// gamla <c>ForMigrate</c> (<c>Trust Server Certificate=true</c>) och <c>ForPersisted</c>
/// (<c>VerifyFull</c> + hårdkodad RDS-CA-bundle-path) ersätts av en enda
/// parameter-driven factory. SSL-läget kommer från <c>MIGRATE_SSL_MODE</c> och den
/// faktiska VPS-topologin (privat Docker-bridge utan TLS vs intern CA + VerifyFull)
/// sätts av Hetzner-Compose-stacken (#196/TD-106), inte här.
///
/// <see cref="NpgsqlConnectionStringBuilder"/> används så operatör-satta lösenord
/// escapas korrekt (env-creds kan innehålla <c>;</c>/<c>=</c> till skillnad från de
/// tidigare genererade [A-Za-z0-9]-lösenorden). Säkerhets-posturen regression-låses
/// i <c>ConnectionStringFactoryTests</c> + arch-testet <c>ConnectionStringLeakageTests</c>.
/// </summary>
public static class ConnectionStringFactory
{
    /// <summary>
    /// Bygger en connection-string med vald TLS-postur. <paramref name="sslMode"/>
    /// parsas mot Npgsqls <see cref="SslMode"/>-enum (fail-loud på okänt värde,
    /// CLAUDE.md §3.4). <see cref="SslMode.VerifyCA"/>/<see cref="SslMode.VerifyFull"/>
    /// kräver <paramref name="rootCertificatePath"/> — annars finns ingen CA att
    /// verifiera serverns certifikat mot och valet vore en tyst no-op.
    /// </summary>
    public static string Build(
        string host,
        int port,
        string database,
        string username,
        string password,
        string sslMode,
        string? rootCertificatePath = null)
    {
        if (!Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException(
                $"Ogiltigt SSL-läge '{sslMode}' (MIGRATE_SSL_MODE). Tillåtna värden: "
                + string.Join(", ", Enum.GetNames<SslMode>()) + ".");
        }

        var requiresRootCert = mode is SslMode.VerifyCA or SslMode.VerifyFull;
        if (requiresRootCert && string.IsNullOrWhiteSpace(rootCertificatePath))
        {
            throw new InvalidOperationException(
                $"SSL-läge '{mode}' kräver root-certifikat-path (MIGRATE_SSL_ROOT_CERT) "
                + "för att verifiera serverns certifikat.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = mode,
        };

        if (requiresRootCert)
        {
            builder.RootCertificate = rootCertificatePath;
        }

        return builder.ConnectionString;
    }
}
