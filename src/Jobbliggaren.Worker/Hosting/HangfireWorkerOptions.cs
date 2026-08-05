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
///   <item><see cref="ShutdownTimeoutSeconds"/> — deliberately just under the
///     orchestrator's grace period, so Hangfire commits job state before SIGKILL.
///     Range 1-300, default 25 s. THE VALUE IS DERIVED, NOT CHOSEN: its companion
///     is <c>stop_grace_period</c> on the <c>worker</c> service in
///     <c>deploy/docker-compose.yml</c>, set to 30 s there because Compose
///     defaults to 10 s and would otherwise kill the process mid-commit. Raising
///     one without the other reopens exactly that window, so raise both in the
///     same change. (This derivation named Fargate <c>stopTimeout</c> until
///     2026-08-05; ADR 0066 retired that platform and #196 replaced it.)</item>
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
