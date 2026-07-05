using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.RecentJobSearches.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.Common;

/// <summary>
/// Kanonisk pipeline-ordning för Mediator-behaviors per ADR 0008 + ADR 0022 + ADR 0023.
/// Refereras av båda composition roots (Api + Worker) så att de inte kan drifta isär.
/// Ändringar i denna lista påverkar båda hosts samtidigt — verifiering sker via
/// architecture test (<c>WorkerLayerTests.MediatorPipeline_should_have_expected_behaviors_in_order</c>).
///
/// Pipeline-flöde (yttersta först, innerst sist):
/// <list type="number">
/// <item><see cref="LoggingScopeBehavior{TMessage,TResponse}"/> — TD-104/STEG 6: structured-field log scope (CorrelationId/UserId/OperationType), yttersta så all nedströms-loggning ärver scopet</item>
/// <item><see cref="LoggingBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="ValidationBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AuthorizationBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AdminAuthorizationBehavior{TMessage,TResponse}"/> — defense-in-depth för IAdminRequest</item>
/// <item><see cref="ReauthenticationBehavior{TMessage,TResponse}"/> — server-enforced re-auth för IReauthenticatingRequest (PR2c/C5, epik #481)</item>
/// <item><see cref="UnitOfWorkBehavior{TMessage,TResponse}"/></item>
/// <item><see cref="AuditBehavior{TMessage,TResponse}"/> — innerst, atomisk persistens via UoW</item>
/// </list>
/// </summary>
public static class MediatorPipelineBehaviors
{
    public static readonly Type[] InOrder =
    [
        // TD-104 / STEG 6 — yttersta: öppnar logg-scope (CorrelationId/UserId/OperationType)
        // så LoggingBehavior + alla nedströms-behaviors/handlers ärver de strukturerade fälten.
        typeof(LoggingScopeBehavior<,>),
        typeof(LoggingBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(AdminAuthorizationBehavior<,>),
        // PR2c (C5, epik #481) — server-enforced re-auth for IReauthenticatingRequest. After
        // authorization (the actor is known) and before KMS-prefetch/UnitOfWork/Audit so a failed
        // re-auth prefetches no DEK, commits nothing and writes no audit row. Validation runs
        // earlier, so an empty password yields 400 before re-auth runs (empty vs wrong = 400 vs 401).
        typeof(ReauthenticationBehavior<,>),
        // TD-13 (ADR 0049 Mekanik-not 3/4) — efter auth (ingen KMS-op för ej
        // auktoriserad principal, §5.4), före UnitOfWork (DEK-cache varm när
        // handlerns query materialiserar krypterade entiteter).
        typeof(FieldEncryptionKeyPrefetchBehavior<,>),
        typeof(UnitOfWorkBehavior<,>),
        // ADR 0060 — post-handler auto-capture för ICapturesRecentSearch-queries
        // (no-op för commands + queries utan markör). Placerad efter UoW så
        // capture bara körs vid lyckad query; före Audit — queries audit:as inte
        // och capture-fel ska inte blandas in i audit-pipeline.
        typeof(RecentJobSearchCaptureBehavior<,>),
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
