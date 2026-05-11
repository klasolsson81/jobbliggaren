using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace JobbPilot.Application.Common;

/// <summary>
/// Kanonisk pipeline-ordning för Mediator-behaviors per ADR 0008 + ADR 0022 + ADR 0023.
/// Refereras av båda composition roots (Api + Worker) så att de inte kan drifta isär.
/// Ändringar i denna lista påverkar båda hosts samtidigt — verifiering sker via
/// architecture test (<c>WorkerLayerTests.MediatorPipeline_should_have_expected_behaviors_in_order</c>).
///
/// Pipeline-flöde (yttersta först, innerst sist):
/// <list type="number">
/// <item><see cref="LoggingBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="ValidationBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AuthorizationBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AdminAuthorizationBehavior{TMessage,TResponse}"/> — defense-in-depth för IAdminRequest</item>
/// <item><see cref="UnitOfWorkBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AuditBehavior{TMessage,TResponse}"/> — innerst, atomisk persistens via UoW</item>
/// </list>
/// </summary>
public static class MediatorPipelineBehaviors
{
    public static readonly Type[] InOrder =
    [
        typeof(LoggingBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(AdminAuthorizationBehavior<,>),
        typeof(UnitOfWorkBehavior<,>),
        typeof(AuditBehavior<,>),
    ];

    /// <summary>
    /// Registrerar pipeline-behaviors i DI som open-generic <see cref="IPipelineBehavior{TMessage,TResponse}"/>.
    /// Mediator runtime hämtar dem via <c>GetServices&lt;IPipelineBehavior&lt;...&gt;&gt;()</c> i registrerings-ordning.
    /// Anropas av båda composition roots (Api + Worker) efter <c>AddMediator(...)</c>.
    /// </summary>
    public static IServiceCollection AddMediatorPipelineBehaviors(this IServiceCollection services)
    {
        foreach (var behaviorType in InOrder)
            services.AddScoped(typeof(IPipelineBehavior<,>), behaviorType);
        return services;
    }
}
