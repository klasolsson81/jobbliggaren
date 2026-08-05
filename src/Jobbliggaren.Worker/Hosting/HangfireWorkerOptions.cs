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
///   <item><see cref="ShutdownTimeoutSeconds"/> — 3 s under host disposal, which
///     must in turn sit under the orchestrator grace period, so Hangfire commits job
///     state before SIGKILL.
///     Range 1-300, default 25 s. THE VALUE IS DERIVED, NOT CHOSEN — and the thing it
///     is derived from is not in this repository yet: no compose file here declares a
///     worker service or a grace period, and #196 owns both. Until it lands, 25 s sits
///     against a contract no file carries. When it does, raise the two together;
///     raising this alone does nothing, because the grace period kills the process
///     first. (The derivation named Fargate <c>stopTimeout</c> until 2026-08-05;
///     ADR 0066 retired that platform.)</item>
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
