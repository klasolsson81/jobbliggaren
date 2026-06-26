using Npgsql;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Regression-skydd för TD-105 (VPS-portabel TLS-postur). Efter AWS-exit (ADR 0050/
/// 0066) bygger <see cref="ConnectionStringFactory.Build"/> connection-strings med
/// <em>konfig-drivet</em> SSL-läge i stället för de gamla RDS-hårdkodade
/// <c>ForMigrate</c>/<c>ForPersisted</c>.
///
/// Den säkerhets-invariant som ÖVERLEVER AWS-eran: ingen <em>tyst</em>
/// TLS-nedgradering. <see cref="SslMode.VerifyCA"/>/<see cref="SslMode.VerifyFull"/>
/// kräver en explicit root-cert (annars vore valet en no-op), och factory:n
/// hårdkodar aldrig <c>Trust Server Certificate=true</c> (arch-testet
/// <c>ConnectionStringLeakageTests</c> vaktar hela Migrate-assemblyn).
/// </summary>
public class ConnectionStringFactoryTests
{
    private const string Host = "db";
    private const int Port = 5432;
    private const string Db = "jobbliggaren";
    private const string User = "jobbliggaren_app";
    private const string Pwd = "test-password";

    private static NpgsqlConnectionStringBuilder Parse(string cs) => new(cs);

    [Theory]
    [InlineData("Disable", SslMode.Disable)]
    [InlineData("Require", SslMode.Require)]
    [InlineData("require", SslMode.Require)] // case-insensitiv parsning
    public void Build_propagerar_sslmode(string input, SslMode expected)
    {
        var cs = ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, input);

        Parse(cs).SslMode.ShouldBe(expected);
    }

    [Fact]
    public void Build_injicerar_alla_parametrar()
    {
        var cs = ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, "Require");

        var b = Parse(cs);
        b.Host.ShouldBe(Host);
        b.Port.ShouldBe(Port);
        b.Database.ShouldBe(Db);
        b.Username.ShouldBe(User);
        b.Password.ShouldBe(Pwd);
    }

    [Fact]
    public void Build_VerifyFull_utan_rootcert_kastar()
    {
        // Ingen tyst no-op: VerifyFull utan CA att verifiera mot är ett konfig-fel.
        Should.Throw<InvalidOperationException>(() =>
            ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, "VerifyFull"));
    }

    [Fact]
    public void Build_VerifyFull_med_rootcert_satter_root_certificate()
    {
        const string certPath = "/etc/ssl/certs/internal-ca.pem";

        var cs = ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, "VerifyFull", certPath);

        var b = Parse(cs);
        b.SslMode.ShouldBe(SslMode.VerifyFull);
        b.RootCertificate.ShouldBe(certPath);
    }

    [Fact]
    public void Build_okant_sslmode_kastar()
    {
        // Fail-loud på typo i MIGRATE_SSL_MODE (CLAUDE.md §3.4).
        Should.Throw<InvalidOperationException>(() =>
            ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, "Bogus"));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Require")]
    public void Build_hardkodar_aldrig_trust_server_certificate_true(string sslMode)
    {
        // Den säkerhets-invariant som överlever AWS-exit: factory:n emit:ar aldrig
        // skip-validering via Trust=true. Tidigare ForMigrate-postur är borttagen.
        var cs = ConnectionStringFactory.Build(Host, Port, Db, User, Pwd, sslMode);

        cs.ShouldNotContain("Trust Server Certificate=true", Case.Insensitive);
    }

    [Fact]
    public void Build_escapar_losenord_med_specialtecken()
    {
        // Operatör-givna lösenord kan innehålla ; ' = " (till skillnad från de gamla
        // genererade [A-Za-z0-9]-lösenorden). NpgsqlConnectionStringBuilder round-trippar.
        const string trickyPwd = "p;a'ss=w\"ord";

        var cs = ConnectionStringFactory.Build(Host, Port, Db, User, trickyPwd, "Require");

        Parse(cs).Password.ShouldBe(trickyPwd);
    }
}
