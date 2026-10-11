using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Infrastructure.JobAds;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 — the resolver the two search ports take, for tests that construct a port by hand: the real
/// parser and the host's real <see cref="ITaxonomyLabelMatcher"/>, with the synonym expander the test
/// controls.
/// </summary>
internal static class SearchTextResolvers
{
    public static JobAdSearchTextResolver For(IServiceScope scope, IOccupationSynonymExpander expander) =>
        new(
            new SearchQueryParser(),
            expander,
            scope.ServiceProvider.GetRequiredService<ITaxonomyLabelMatcher>());
}
