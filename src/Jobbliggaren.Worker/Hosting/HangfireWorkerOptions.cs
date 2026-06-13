namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Hangfire-konfiguration som styrs per miljö (TD-17 punkt 1+6).
///
/// <list type="bullet">
///   <item><see cref="PrepareSchemaIfNecessary"/> — Development/Test = <c>true</c>;
///     övriga miljöer (Staging/Production/etc) = <c>false</c>. Worker-DB-
///     användarens GRANT-set blir minimal utanför dev (DML-only på
///     <c>hangfire.*</c>); schema-DDL körs via runbook
///     <c>docs/runbooks/hangfire-schema.md</c> innan deploy.</item>
///   <item><see cref="ShutdownTimeoutSeconds"/> — strax under Fargate default
///     stopTimeout (30 s) så Hangfire hinner committa job-state innan SIGKILL.
///     Range 1-300, default 25 s. Höjs via Fargate <c>stopTimeout</c> +
///     denna option om smoke-tests visar att rollback/cleanup tar &gt; 25 s.</item>
/// </list>
///
/// Direct-bound via <c>Configuration.GetSection().Get&lt;T&gt;()</c> i
/// Worker/Program.cs — inte injicerat som <c>IOptions&lt;T&gt;</c> eftersom
/// värdena bara läses vid host-uppstart.
/// </summary>
public sealed class HangfireWorkerOptions
{
    public const string SectionName = "Hangfire";

    public bool PrepareSchemaIfNecessary { get; init; } = true;

    public int ShutdownTimeoutSeconds { get; init; } = 25;
}
