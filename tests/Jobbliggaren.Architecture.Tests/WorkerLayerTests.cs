using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Worker.Auditing;
using Jobbliggaren.Worker.Hosting;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Architecture-regler för Worker-composition-root per ADR 0010 + ADR 0023 / STEG 9.
/// </summary>
public class WorkerLayerTests
{
    [Fact]
    public void Worker_should_not_depend_on_AspNetCore_Http_or_Identity()
    {
        // Worker-stubs av audit-portarna (per ADR 0023 / STEG 9) får inte luta sig på
        // HTTP-bagage. HTTP-baserade implementationer (CorrelationIdProvider,
        // RequestContextProvider, CurrentUser) bor i Infrastructure.AddHttpAuditing /
        // Infrastructure.AddIdentityAndSessions och får aldrig laddas i Worker.
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Identity",
                "Microsoft.AspNetCore.Authentication.JwtBearer",
                "Microsoft.AspNetCore.Identity.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Worker läcker mot ASP.NET Core HTTP/Identity: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Worker_audit_stubs_should_implement_application_ports()
    {
        // Worker-stubs är de enda Worker-typerna som får implementera ICurrentUser /
        // ICorrelationIdProvider / IRequestContextProvider — de översätter HTTP-bagage
        // till null-state för system-jobb (ADR 0022 + ADR 0023).
        typeof(WorkerSystemUser).IsAssignableTo(typeof(ICurrentUser)).ShouldBeTrue();
        typeof(WorkerCorrelationIdProvider).IsAssignableTo(typeof(ICorrelationIdProvider)).ShouldBeTrue();
        typeof(WorkerRequestContextProvider).IsAssignableTo(typeof(IRequestContextProvider)).ShouldBeTrue();
    }

    [Fact]
    public void RecurringJobRegistrar_should_reside_in_Worker_assembly()
    {
        // Hangfire-bindings (cron-registrering) är Worker-specifika och får inte
        // läcka in i Application-lagret.
        typeof(RecurringJobRegistrar).Assembly.GetName().Name
            .ShouldBe("Jobbliggaren.Worker");
    }

    [Fact]
    public void MediatorPipeline_should_have_expected_behaviors_in_order()
    {
        // Pipeline-ordning per ADR 0008 + ADR 0022. Ändringar måste vara medvetna
        // och uppdatera detta test (samt eventuellt ADR 0008/0022).
        // AdminAuthorizationBehavior tillagd 2026-05-11 (defense-in-depth för
        // IAdminRequest, parallell med HTTP-lager-policy RequireRole("Admin")).
        var expected = new[]
        {
            // TD-104 / STEG 6 tillagd 2026-06-14 — yttersta: structured-field log scope
            // (CorrelationId/UserId/OperationType) så LoggingBehavior + nedströms ärver fälten.
            typeof(LoggingScopeBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(AdminAuthorizationBehavior<,>),
            // PR2c (C5, epik #481) tillagd 2026-07-05 — server-enforced re-auth för
            // IReauthenticatingRequest, efter auth och före DEK-prefetch/UnitOfWork/Audit.
            typeof(ReauthenticationBehavior<,>),
            // TD-13 (ADR 0049 Mekanik-not 3/4) tillagd 2026-05-18 — DEK-prefetch
            // efter auth, före UnitOfWork.
            typeof(FieldEncryptionKeyPrefetchBehavior<,>),
            typeof(UnitOfWorkBehavior<,>),
            // ADR 0060 tillagd 2026-05-20 — auto-capture post-handler för
            // ICapturesRecentSearch-queries. Efter UoW (capture sker bara vid
            // lyckad query), före Audit (queries audit:as inte).
            typeof(Jobbliggaren.Application.RecentJobSearches.Behaviors.RecentJobSearchCaptureBehavior<,>),
            typeof(AuditBehavior<,>),
        };

        MediatorPipelineBehaviors.InOrder.ShouldBe(expected,
            "MediatorPipelineBehaviors.InOrder ändrad — verifiera ADR 0008/0022 + audit-paritet.");
    }
}
