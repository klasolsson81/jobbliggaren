using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Application.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>();

        // ADR 0067 Fas D2 — residual-fritext-parser. Ren CPU, stateless,
        // trådsäker → singleton (paritet IIpAnonymizer). Bor i Application
        // (ingen IOptions/Npgsql-binding, till skillnad från
        // IOccupationSynonymExpander); DI i samma commit som impl
        // (feedback_di_with_handlers_same_commit). Konsumeras av
        // ListJobAdsQueryHandler.
        services.AddSingleton<ISearchQueryParser, SearchQueryParser>();

        // #1656 (b) — SCOPED so the criterion's ad magnitude and its matching set are measured at
        // most once per request, however many handlers ask. Two resolutions of one criterion are two
        // measurements at two instants, and a response whose count and list came from different
        // instants is the divergence this type exists to close.
        services.AddScoped<CriterionMatchingAdSetResolver>();

        // Mediator + pipeline behaviors registreras i composition roots (Api/Worker)
        return services;
    }
}
