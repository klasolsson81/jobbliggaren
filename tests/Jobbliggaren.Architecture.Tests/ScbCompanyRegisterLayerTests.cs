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
        //
        // Kept as a cheap, explicit REGRESSION pin next to the fail-closed generalisation below: it
        // still fires if someone moved ScbCompanyRegisterEntry into the Domain assembly (which would
        // satisfy the general guard while defeating its intent).
        var dbSetTypeArgNames = typeof(Jobbliggaren.Application.Common.Abstractions.IAppDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType)
            .SelectMany(p => p.PropertyType.GetGenericArguments())
            .Select(t => t.Name)
            .ToList();

        dbSetTypeArgNames.ShouldNotContain("ScbCompanyRegisterEntry");
    }

    [Fact]
    public void IAppDbContext_exposes_only_Domain_types()
    {
        // #560 kriterie-vågen PR-1 / DPIA Part D C-D4 (M-C5 firewall), architect bind 2026-07-13 Q6.
        //
        // The name-pinned test above is FAIL-OPEN: it catches exactly ONE type name. A future
        // `CompanyRegisterEntry`, `ScbCompanyProjection` or any other Infrastructure read-model would
        // slide straight through — the same fail-open class AccountHardDeleteCascadeFitnessTests was
        // re-architected away from (Saltzer & Schroeder fail-safe defaults). This is the fail-CLOSED
        // form: IAppDbContext is the Application layer's DbContext port, so EVERY DbSet<T> on it must
        // be a Domain type. Name-independent → a new register type of ANY name is caught by
        // construction, and the M-C5 firewall (no handler can join the register against pnr-lookup
        // output) stays a build gate rather than a convention.
        var offenders = AppDbContextPortScan.FindNonDomainDbSets(
            typeof(Jobbliggaren.Application.Common.Abstractions.IAppDbContext), DomainAsm);

        offenders.ShouldBeEmpty(
            "IAppDbContext exponerar DbSet av icke-Domain-typ(er). Infrastructure-read-models " +
            "(company_register-repliken, taxonomy-cachen) nås via den konkreta AppDbContext.Set<T>() " +
            "inuti Infrastructure — ALDRIG via Application-porten (ADR 0043 / ADR 0087 D2 / DPIA " +
            "C-D4 M-C5-firewall: registret får aldrig kunna joinas mot pnr-lookup-output i en " +
            "handler). Otillåtna: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Port_scan_flags_a_non_domain_DbSet()
    {
        // Self-proving negative (house idiom — mirrors HardDeleteCascadeScan's negatives): the guard
        // above is only worth its green when the detector demonstrably FAILS on a violation. The
        // fixture lives in the TEST assembly, so it never pollutes the real port.
        var offenders = AppDbContextPortScan.FindNonDomainDbSets(
            typeof(IFakePortWithInfraDbSet), DomainAsm);

        offenders.Count.ShouldBe(1,
            "detektorn ska rapportera exakt den icke-Domain-DbSet:en");
        offenders[0].Contains("Registry", StringComparison.Ordinal).ShouldBeTrue(
            "rapporten ska namnge den felande DbSet-propertyn (Registry)");
        offenders[0].Contains(nameof(FakeInfraReadModel), StringComparison.Ordinal).ShouldBeTrue(
            "rapporten ska namnge den otillåtna entitets-typen (FakeInfraReadModel)");
    }

    private static readonly System.Reflection.Assembly DomainAsm =
        typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly;

    // Stand-in for "someone put an Infrastructure read-model on the Application DbContext port".
    private sealed class FakeInfraReadModel
    {
        public string Id { get; init; } = string.Empty;
    }

    private interface IFakePortWithInfraDbSet
    {
        Microsoft.EntityFrameworkCore.DbSet<FakeInfraReadModel> Registry { get; }
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

/// <summary>
/// Pure detection helper for the <c>IAppDbContext</c> port firewall (DPIA Part D C-D4 / M-C5).
/// Side-effect-free so it is independently testable — see the self-proving negative in
/// <see cref="ScbCompanyRegisterLayerTests.Port_scan_flags_a_non_domain_DbSet"/> (the form mirrors
/// <c>HardDeleteCascadeScan</c>).
/// </summary>
internal static class AppDbContextPortScan
{
    /// <summary>
    /// Returns a description of every <c>DbSet&lt;T&gt;</c> on <paramref name="port"/> whose entity
    /// type does NOT live in <paramref name="domainAssembly"/> — i.e. every Infrastructure
    /// read-model illegally exposed on the Application DbContext port.
    /// </summary>
    internal static IReadOnlyList<string> FindNonDomainDbSets(
        Type port, System.Reflection.Assembly domainAssembly) =>
        port.GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition()
                    == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
            .Select(p => (p.Name, Entity: p.PropertyType.GetGenericArguments()[0]))
            .Where(x => x.Entity.Assembly != domainAssembly)
            .Select(x => $"{x.Name} → {x.Entity.FullName}")
            .ToList();
}
