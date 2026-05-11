using System.Globalization;

namespace JobbPilot.Migrate;

/// <summary>
/// Bygger Postgres-connection-strings för Migrate-flow (TD-38).
///
/// Två varianter med distinkt TLS-postur:
/// <list type="bullet">
///   <item><see cref="ForMigrate"/> — short-lived bootstrap-CS. SSL Mode=Require +
///     Trust Server Certificate=true. Acceptabelt eftersom Migrate-Dockerfile saknar
///     RDS-CA-bundle och task är sekunder-långt. MITM-yta minimal (ECS-SG-only-
///     ingress till RDS-SG inom VPC).</item>
///   <item><see cref="ForPersisted"/> — CS som skrivs PERMANENT till Secrets Manager
///     och läses av Api + Worker. SSL Mode=VerifyFull validerar både CA-signature
///     och hostname-match. Root Certificate pekar på RDS global CA-bundle som
///     installeras i Api/Worker-containers via Dockerfile-COPY.</item>
/// </list>
///
/// Lyft till egen klass (i stället för top-level statics i Program.cs) för
/// enhets-testbar yta — säkerhets-postur ska kunna regression-låsas mekaniskt
/// (CLAUDE.md §2.4 + §7).
/// </summary>
public static class ConnectionStringFactory
{
    /// <summary>Bundle-path i Api/Worker-containers (matchar Dockerfile-COPY-target).</summary>
    public const string RdsBundleContainerPath = "/etc/ssl/certs/rds-global-bundle.pem";

    /// <summary>
    /// CS för Migrate-app själv (master/migrations-creds). Trust=true OK eftersom:
    /// short-lived bootstrap-task, Migrate-Dockerfile saknar bundle, ECS-SG-only-
    /// ingress till RDS-SG inom VPC. MITM-yta minimal.
    /// </summary>
    public static string ForMigrate(string host, int port, string db, string user, string pwd) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Host={host};Port={port};Database={db};Username={user};Password={pwd};SSL Mode=Require;Trust Server Certificate=true");

    /// <summary>
    /// CS för persisterade tjänster (Api + Worker) som skrivs PERMANENT till
    /// Secrets Manager. SSL Mode=VerifyFull validerar CA-signature + hostname-match.
    /// Root Certificate pekar på RDS global CA-bundle (måste finnas i container,
    /// se Api/Worker Dockerfile COPY-direktiv).
    /// </summary>
    public static string ForPersisted(string host, int port, string db, string user, string pwd) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Host={host};Port={port};Database={db};Username={user};Password={pwd};SSL Mode=VerifyFull;Root Certificate={RdsBundleContainerPath}");
}
