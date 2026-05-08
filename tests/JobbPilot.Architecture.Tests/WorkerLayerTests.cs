using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Behaviors;
using JobbPilot.Worker.Auditing;
using JobbPilot.Worker.Hosting;
using NetArchTest.Rules;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

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
    public void DetectGhostedApplicationsJob_should_reside_in_Application_layer()
    {
        // Job-orchestrators ligger i Application-lagret (testbart utan Hangfire).
        // Worker är tunn binding-layer som registrerar cron-bindning via Hangfire.
        var jobType = typeof(DetectGhostedApplicationsJob);
        jobType.Assembly.ShouldBe(typeof(JobbPilot.Application.AssemblyMarker).Assembly,
            "DetectGhostedApplicationsJob ska bo i JobbPilot.Application, inte Worker/Infrastructure.");
        jobType.Namespace.ShouldNotBeNull();
        jobType.Namespace!.StartsWith("JobbPilot.Application.Applications.Jobs",
            StringComparison.Ordinal).ShouldBeTrue(
            $"DetectGhostedApplicationsJob.Namespace = {jobType.Namespace}");
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
            .ShouldBe("JobbPilot.Worker");
    }

    [Fact]
    public void MediatorPipeline_should_have_expected_behaviors_in_order()
    {
        // Pipeline-ordning per ADR 0008 + ADR 0022. Ändringar måste vara medvetna
        // och uppdatera detta test (samt eventuellt ADR 0008/0022).
        var expected = new[]
        {
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(UnitOfWorkBehavior<,>),
            typeof(AuditBehavior<,>),
        };

        MediatorPipelineBehaviors.InOrder.ShouldBe(expected,
            "MediatorPipelineBehaviors.InOrder ändrad — verifiera ADR 0008/0022 + audit-paritet.");
    }
}
