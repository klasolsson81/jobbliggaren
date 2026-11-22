using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 — the resolver the two search ports take, for tests that construct a port by hand: the real
/// parser, the host's real <see cref="ITaxonomyLabelMatcher"/>, occupation-name lineage and junior field catalog over
/// the scope's own context, with the synonym expander the test controls.
/// </summary>
internal static class SearchTextResolvers
{
    public static JobAdSearchTextResolver For(IServiceScope scope, IOccupationSynonymExpander expander) =>
        new(
            new SearchQueryParser(),
            expander,
            scope.ServiceProvider.GetRequiredService<ITaxonomyLabelMatcher>(),
            scope.ServiceProvider.GetRequiredService<IOccupationNameLineageProvider>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IJuniorFieldCatalog>());
}
