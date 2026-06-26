using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Enhetstester för <see cref="MigrateEnv.Resolve"/> — env/Docker-secret-<c>_FILE</c>-
/// resolution efter AWS-exit (TD-105 / #199). Ren funktion testad med fejkade
/// env-lookup + fil-läsare (ingen process-env eller filsystem, CLAUDE.md §2.4).
/// </summary>
public class MigrateEnvTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] pairs) =>
        key => Array.Find(pairs, p => p.Key == key).Value;

    private static string NoFile(string path) =>
        throw new InvalidOperationException($"Fil-läsare anropades oväntat: {path}");

    [Fact]
    public void Resolve_laser_direkt_env_var_nar_satt()
    {
        var result = MigrateEnv.Resolve("FOO", Env(("FOO", "bar")), NoFile);

        result.ShouldBe("bar");
    }

    [Fact]
    public void Resolve_FILE_har_foretrade_over_direkt_var()
    {
        var result = MigrateEnv.Resolve(
            "FOO",
            Env(("FOO", "direkt"), ("FOO_FILE", "/run/secrets/foo")),
            path => path == "/run/secrets/foo" ? "fil-värde\n" : NoFile(path));

        result.ShouldBe("fil-värde"); // trimmat
    }

    [Fact]
    public void Resolve_trimmar_filinnehall()
    {
        var result = MigrateEnv.Resolve(
            "SECRET",
            Env(("SECRET_FILE", "/run/secrets/s")),
            _ => "  hemlig  \n");

        result.ShouldBe("hemlig");
    }

    [Fact]
    public void Resolve_returnerar_null_nar_inget_satt()
    {
        var result = MigrateEnv.Resolve("MISSING", Env(), NoFile);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_tom_var_behandlas_som_osatt()
    {
        var result = MigrateEnv.Resolve("EMPTY", Env(("EMPTY", "   ")), NoFile);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_tom_fil_behandlas_som_osatt()
    {
        var result = MigrateEnv.Resolve(
            "EMPTYFILE",
            Env(("EMPTYFILE_FILE", "/run/secrets/empty")),
            _ => "\n  \n");

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_tom_FILE_path_faller_tillbaka_pa_direkt_var()
    {
        // Tom _FILE-path räknas som osatt → direkt-varen används.
        var result = MigrateEnv.Resolve(
            "FOO",
            Env(("FOO", "direkt"), ("FOO_FILE", "")),
            NoFile);

        result.ShouldBe("direkt");
    }
}
