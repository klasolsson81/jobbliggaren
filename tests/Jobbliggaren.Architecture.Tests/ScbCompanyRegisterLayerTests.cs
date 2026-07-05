using System.Reflection;
using Hangfire;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #560 (ADR 0091) — Clean-Architecture guard for the SCB company-register population channel (parity
/// <see cref="TaxonomyAclLayerTests"/>). The ports live in Application; the replica POCO, EF config,
/// cert client, wire DTOs, partition planner, store and orchestrator are Infrastructure-internal; the
/// <c>company_register</c> replica is NOT a <c>DbSet</c> on <c>IAppDbContext</c> (read-model, not an
/// aggregate — ADR 0043 / ADR 0087 D2); and the orchestrator never imports Hangfire (ADR 0023).
/// </summary>
public class ScbCompanyRegisterLayerTests
{
    private static readonly System.Reflection.Assembly ApplicationAsm =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAsm =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

    private const string InfraCompanyRegisterNs = "Jobbliggaren.Infrastructure.CompanyRegister";

    [Fact]
    public void Population_ports_and_ACL_record_live_in_Application()
    {
        // The population channel's port surface is BCL + Application ACL types only (parity IJobSource).
        typeof(Jobbliggaren.Application.CompanyRegister.Abstractions.IScbCompanyRegisterSource)
            .Assembly.ShouldBe(ApplicationAsm);
        typeof(Jobbliggaren.Application.CompanyRegister.Abstractions.IScbCompanyRegisterRefresher)
            .Assembly.ShouldBe(ApplicationAsm);
        typeof(Jobbliggaren.Application.CompanyRegister.Abstractions.ScbCompanyRecord)
            .Assembly.ShouldBe(ApplicationAsm);
    }

    [Fact]
    public void CompanyRegister_infrastructure_types_are_internal_except_the_options_contract()
    {
        // The replica POCO, EF config, wire DTOs, cert client, planner, store, orchestrator, filter and
        // gate are Infrastructure-internal (ACL isolation — Application never sees the SCB wire format).
        // The ONLY public type is ScbRegisterOptions (a config contract the Worker binds).
        var publicTypes = InfrastructureAsm.GetTypes()
            .Where(t => (t.Namespace?.StartsWith(InfraCompanyRegisterNs, StringComparison.Ordinal) ?? false)
                        && (t.IsPublic || (t.IsNested && t.IsNestedPublic)))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        publicTypes.ShouldBe(
            ["Jobbliggaren.Infrastructure.CompanyRegister.ScbRegisterOptions"],
            $"only ScbRegisterOptions may be public in {InfraCompanyRegisterNs}.*; found: {string.Join(", ", publicTypes)}");
    }

    [Fact]
    public void Company_register_replica_is_not_a_DbSet_on_IAppDbContext()
    {
        // ADR 0043 / ADR 0087 D2 — the replica is a read-model reached via the concrete
        // AppDbContext.Set<T>(), NOT an aggregate exposed on the Application DbContext port. Checked by
        // type NAME so this test need not see the internal POCO.
        var dbSetTypeArgNames = typeof(Jobbliggaren.Application.Common.Abstractions.IAppDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType)
            .SelectMany(p => p.PropertyType.GetGenericArguments())
            .Select(t => t.Name)
            .ToList();

        dbSetTypeArgNames.ShouldNotContain("ScbCompanyRegisterEntry");
    }

    [Fact]
    public void CompanyRegister_infrastructure_does_not_depend_on_Hangfire()
    {
        // Hangfire is Worker-only (ADR 0023 delbeslut 2) — the orchestrator + client + store are
        // Hangfire-agnostic; only the Worker wrapper carries the [DisableConcurrentExecution] attribute.
        var result = Types.InAssembly(InfrastructureAsm)
            .That().ResideInNamespaceStartingWith(InfraCompanyRegisterNs)
            .ShouldNot().HaveDependencyOn("Hangfire")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"CompanyRegister Infrastructure leaks a Hangfire dependency: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CompanyRegister_application_namespace_does_not_depend_on_Npgsql_or_HttpClient()
    {
        // The Application population ports must not leak the DB provider or HTTP — those live only in
        // the Infrastructure client/store (parity the TaxonomyAclLayerTests Application guard).
        var result = Types.InAssembly(ApplicationAsm)
            .That().ResideInNamespaceStartingWith("Jobbliggaren.Application.CompanyRegister")
            .ShouldNot().HaveDependencyOnAny("Npgsql", "NpgsqlTypes", "System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application.CompanyRegister leaks Npgsql/HttpClient: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // #688 (ADR 0091 amendment 2026-07-05) — attribute-reflection pin on the Worker wrapper's RunAsync.
    // The wrapper exists ONLY to carry these two Hangfire filter attributes without leaking Hangfire into
    // Application/Infrastructure (asserted above); their values are the entire resilience posture and are
    // silently catastrophic if dropped. Reflection-only, no Hangfire boot. SCB-only — the other workers'
    // retry behavior is a deliberate ADR 0032 decision and is explicitly out of scope here.
    private static readonly MethodInfo ScbWorkerRunAsync =
        typeof(Jobbliggaren.Worker.Hosting.ScbCompanyRegisterSyncWorker)
            .GetMethod(nameof(Jobbliggaren.Worker.Hosting.ScbCompanyRegisterSyncWorker.RunAsync))!;

    [Fact]
    public void ScbCompanyRegisterSyncWorker_RunAsync_CarriesAutomaticRetryZeroAttemptsFail()
    {
        // Attempts = 0 is the first [AutomaticRetry] in the codebase (precedent-setting) and silently
        // catastrophic if removed: Hangfire's global default (10 attempts, exponential backoff) restarts
        // the ~2 h metered SCB job FROM ZERO per attempt, reproducing the 8-starts/0-completions storm
        // (ADR 0032 anti-pattern precedent). OnAttemptsExceeded = Fail keeps the failed run VISIBLE in the
        // admin failed-jobs list — Delete would silently vanish it and defeat observability.
        var retry = ScbWorkerRunAsync.GetCustomAttribute<AutomaticRetryAttribute>();

        retry.ShouldNotBeNull("RunAsync must carry [AutomaticRetry(Attempts = 0)] — nothing else guards the no-retry posture.");
        retry.Attempts.ShouldBe(0);
        retry.OnAttemptsExceeded.ShouldBe(AttemptsExceededAction.Fail);
    }

    [Fact]
    public void ScbCompanyRegisterSyncWorker_RunAsync_CarriesDisableConcurrentExecution4hTimeout()
    {
        // The refresh must NEVER overlap itself: two ~2 h extracts against the same SCB throttle budget
        // would race the deregister sweep's per-run synced_at watermark. 14400 s (4 h) covers the full
        // population. Hangfire.Core 1.8.23 exposes the timeout via the public TimeoutSec property, so the
        // value is pinned directly (no private-field reflection needed).
        var disable = ScbWorkerRunAsync.GetCustomAttribute<DisableConcurrentExecutionAttribute>();

        disable.ShouldNotBeNull("RunAsync must carry [DisableConcurrentExecution] to prevent self-overlap.");
        disable.TimeoutSec.ShouldBe(14400);
    }
}
