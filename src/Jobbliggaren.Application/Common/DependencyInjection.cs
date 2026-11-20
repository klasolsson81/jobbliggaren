using FluentValidation;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>();
        services.AddScoped<AccountDeletionScheduler>();

        // ADR 0067 Fas D2 — residual-fritext-parser. Ren CPU, stateless,
        // trådsäker → singleton (paritet IIpAnonymizer). Bor i Application
        // (ingen IOptions/Npgsql-binding, till skillnad från
        // IOccupationSynonymExpander); DI i samma commit som impl
        // (feedback_di_with_handlers_same_commit). Konsumeras av
        // ListJobAdsQueryHandler.
        services.AddSingleton<ISearchQueryParser, SearchQueryParser>();

        // #2199 PR 2b — the one predicate on what recent searches may store, read by the capture
        // behaviour and by ListJobAdsQueryHandler. Its religious-occupation set is registered in
        // Infrastructure's AddPersistence, which both hosts call.
        services.AddSingleton<RecentJobSearches.Common.RecentSearchCaptureGate>();

        // Mediator + pipeline behaviors registreras i composition roots (Api/Worker)
        return services;
    }
}
