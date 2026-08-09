namespace Jobbliggaren.Migrate;

/// <summary>
/// The schema-level privilege statements Phase A issues, as data — the other half of the model
/// whose database-level half is <see cref="PhaseADatabaseGrants"/>.
///
/// <para>
/// <b>Why this exists (#1232).</b> These statements used to be a bare sequence of awaits inside
/// <c>ExecutePhaseAAsync</c>, which no test assembly can reach. That is the same shape that let
/// the <c>TEMPORARY</c> grant be deleted with every suite staying green, and it had the same
/// consequence one layer down: the migration oracle could not see the
/// <c>42501: permission denied for schema public</c> defect, because the grant that repairs it
/// — <see cref="PublicSchema"/>'s first statement — existed only as an await. Extracting it
/// gives the oracle something to execute and the model something to assert against.
/// </para>
///
/// <para>
/// <b>Three members, not one parameterised factory, and the asymmetry is the point.</b> The
/// three schemas do not carry the same posture and the differences are decisions from ADR 0034:
/// <c>public</c> already exists so it gets no <c>CREATE SCHEMA</c>; <c>hangfire</c> is owned and
/// written by <see cref="Roles.Migrations"/> and grants the app role nothing; <c>identity</c>
/// grants the app role the full DML/DDL set exactly as <c>public</c> does. A single factory
/// taking flags would hide precisely what the ADR decided.
/// </para>
///
/// <para>
/// <b>Role creation is deliberately NOT here.</b> <c>CREATE ROLE … LOGIN PASSWORD</c> stays an
/// imperative helper in <c>Program.cs</c>: it is provisioning <i>mechanism</i> rather than
/// privilege <i>model</i>, it works around pl/pgsql's parameter limitation with a two-step
/// SELECT-then-DDL, and a data list carrying password literals is a worse secret surface than a
/// function that never returns them.
/// </para>
/// </summary>
public static class PhaseASchemaGrants
{
    /// <summary>
    /// The <c>hangfire</c> schema: created, owned by <see cref="Roles.Migrations"/>, closed to
    /// <c>PUBLIC</c>. The worker's DML grants are Phase C, not here, because they must run after
    /// Hangfire's own installer has created the tables.
    /// </summary>
    public static IReadOnlyList<PrivilegeStatement> HangfireSchema { get; } =
    [
        new($"CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION {Roles.Migrations};",
            "CREATE SCHEMA hangfire"),
        new("REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;",
            "Revoke PUBLIC från hangfire"),
        new($"GRANT USAGE, CREATE ON SCHEMA hangfire TO {Roles.Migrations};",
            "GRANT USAGE/CREATE på hangfire till migrations"),
    ];

    /// <summary>
    /// The <c>public</c> schema: full DML/DDL to <see cref="Roles.App"/>, which is the role
    /// <c>schema</c> mode connects as and therefore the role EF Core's migrator runs as.
    ///
    /// <para>
    /// The first statement is the one #1229 repaired. A clean-database boot died with
    /// <c>42501: permission denied for schema public</c> because <c>schema</c> mode connected as
    /// <see cref="Roles.Migrations"/>, which holds no <c>CREATE</c> here. It is also the reason
    /// this list is data: it is the statement the migration oracle must run to be an oracle at
    /// all.
    /// </para>
    ///
    /// <para>
    /// No <c>CREATE SCHEMA</c> — <c>public</c> ships with the database. No
    /// <c>REVOKE ALL … FROM PUBLIC</c> either, unlike the two schemas below; that asymmetry is
    /// pre-existing and is left exactly as Phase A has always issued it rather than quietly
    /// changed under a refactor.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PrivilegeStatement> PublicSchema { get; } =
    [
        new($"GRANT USAGE, CREATE ON SCHEMA public TO {Roles.App};",
            "GRANT USAGE/CREATE på public till app"),
        new($"GRANT ALL ON ALL TABLES IN SCHEMA public TO {Roles.App};",
            "GRANT ALL på public.* till app"),
        new($"GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO {Roles.App};",
            "GRANT ALL på public-sequences till app"),
        new($"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO {Roles.App};",
            "DEFAULT PRIVILEGES public-tabeller -> app"),
        new($"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO {Roles.App};",
            "DEFAULT PRIVILEGES public-sequences -> app"),
    ];

    /// <summary>
    /// The <c>identity</c> schema (ADR 0034) for <c>AppIdentityDbContext</c>, which declares
    /// <c>HasDefaultSchema("identity")</c>.
    ///
    /// <para>
    /// <b>This list had two call sites emitting it byte-identically.</b> <c>init</c> mode issued
    /// it inside <c>ExecutePhaseAAsync</c> and <c>bootstrap</c> mode issued it again inside
    /// <c>ExecuteBootstrapSchemaAsync</c> — the same seven statements, character for character,
    /// differing only in the operator description on the first line. A privilege model in two
    /// places is one a repair can land in half of, which is the failure this whole extraction
    /// exists to make impossible; both call sites now read this property.
    /// </para>
    ///
    /// <para>
    /// The one merged description keeps both facts the two call sites used to carry separately:
    /// who owns the schema, and which ADR decided it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PrivilegeStatement> IdentitySchema { get; } =
    [
        new($"CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION {Roles.Migrations};",
            "CREATE SCHEMA identity AUTHORIZATION migrations (ADR 0034)"),
        new("REVOKE ALL ON SCHEMA identity FROM PUBLIC;",
            "Revoke PUBLIC från identity"),
        new($"GRANT USAGE, CREATE ON SCHEMA identity TO {Roles.App};",
            "GRANT USAGE/CREATE på identity till app"),
        new($"GRANT ALL ON ALL TABLES IN SCHEMA identity TO {Roles.App};",
            "GRANT ALL på identity-tabeller till app"),
        new($"GRANT ALL ON ALL SEQUENCES IN SCHEMA identity TO {Roles.App};",
            "GRANT ALL på identity-sequences till app"),
        new($"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON TABLES TO {Roles.App};",
            "DEFAULT PRIVILEGES identity-tabeller -> app"),
        new($"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON SEQUENCES TO {Roles.App};",
            "DEFAULT PRIVILEGES identity-sequences -> app"),
    ];
}
