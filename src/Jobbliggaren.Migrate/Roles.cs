namespace Jobbliggaren.Migrate;

// Postgres-rolnamn — hardcoded const per CLAUDE.md §5.1 (magic strings förbjudna).
// Per ADR 0034: jobbliggaren_app är runtime-roll (aldrig CREATE ON DATABASE),
// jobbliggaren_migrations äger schemas, jobbliggaren_worker är DML-only på hangfire.
internal static class Roles
{
    public const string Migrations = "jobbliggaren_migrations";
    public const string App = "jobbliggaren_app";
    public const string Worker = "jobbliggaren_worker";
}
