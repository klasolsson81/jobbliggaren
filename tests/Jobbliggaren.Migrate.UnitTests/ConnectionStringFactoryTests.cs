using Jobbliggaren.Migrate;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Regression-skydd för TD-38 TLS-postur. Säkerhetsskillnad mellan
/// <see cref="ConnectionStringFactory.ForMigrate"/> och
/// <see cref="ConnectionStringFactory.ForPersisted"/> får inte tyst suddas ut
/// vid framtida refaktorer.
/// </summary>
public class ConnectionStringFactoryTests
{
    private const string Host = "test-rds.amazonaws.com";
    private const int Port = 5432;
    private const string Db = "jobbliggaren";
    private const string User = "jobbliggaren_app";
    private const string Pwd = "test-password";

    [Fact]
    public void ForMigrate_InnehallerTrustServerCertificate()
    {
        var cs = ConnectionStringFactory.ForMigrate(Host, Port, Db, User, Pwd);

        cs.ShouldContain("SSL Mode=Require");
        cs.ShouldContain("Trust Server Certificate=true");
    }

    [Fact]
    public void ForPersisted_AnvanderVerifyFull()
    {
        var cs = ConnectionStringFactory.ForPersisted(Host, Port, Db, User, Pwd);

        cs.ShouldContain("SSL Mode=VerifyFull");
    }

    [Fact]
    public void ForPersisted_PekarPaContainerCaBundle()
    {
        var cs = ConnectionStringFactory.ForPersisted(Host, Port, Db, User, Pwd);

        cs.ShouldContain("Root Certificate=/etc/ssl/certs/rds-global-bundle.pem");
    }

    [Fact]
    public void ForPersisted_InnehallerInteTrustServerCertificate()
    {
        // Kritisk regression-skydd: persisterad CS får ALDRIG ha Trust=true.
        // Om denna assert börjar misslyckas → Sec-Major (TD-38-eskalering).
        var cs = ConnectionStringFactory.ForPersisted(Host, Port, Db, User, Pwd);

        cs.ShouldNotContain("Trust Server Certificate=true", Case.Insensitive);
    }

    [Fact]
    public void ForMigrate_InjicerarAllaParametrar()
    {
        var cs = ConnectionStringFactory.ForMigrate(Host, Port, Db, User, Pwd);

        cs.ShouldContain($"Host={Host}");
        cs.ShouldContain($"Port={Port}");
        cs.ShouldContain($"Database={Db}");
        cs.ShouldContain($"Username={User}");
        cs.ShouldContain($"Password={Pwd}");
    }

    [Fact]
    public void ForPersisted_InjicerarAllaParametrar()
    {
        var cs = ConnectionStringFactory.ForPersisted(Host, Port, Db, User, Pwd);

        cs.ShouldContain($"Host={Host}");
        cs.ShouldContain($"Port={Port}");
        cs.ShouldContain($"Database={Db}");
        cs.ShouldContain($"Username={User}");
        cs.ShouldContain($"Password={Pwd}");
    }
}
