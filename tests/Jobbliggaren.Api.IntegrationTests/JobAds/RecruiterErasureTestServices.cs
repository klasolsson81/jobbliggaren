using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The ONE composition of <see cref="IRecruiterErasureMatchQuery"/> for the hand-built containers in
/// this project. Two test classes drive the erasure command against a real Postgres, and neither
/// calls <c>AddInfrastructure</c>, so each used to spell the registration out for itself.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> #1435 gave the implementation a second constructor parameter and updated
/// ONE of the two fixtures. `BuildServiceProvider()` runs without `ValidateOnBuild`, so nothing
/// failed until three facts in the other class hit resolution — and only CI saw it, because the
/// PR's own verification never printed a `total:` line for this project. A single home makes the
/// next parameter a compile-time concern instead of a per-fixture one.
/// <para>
/// The tokeniser is the REAL adapter, never a stub: <c>HMAC-SHA256</c> can only emit 64 lowercase
/// hex characters, so a stubbed return would be a value the real adapter cannot produce — the
/// premise AGENTS.md §5 <c>Tests:</c> forbids asserting a production fact off. It is a SINGLETON so
/// the seed path and the query path share one instance; that parity is the whole reason the token
/// arm can match at all, and a divergence would silently return zero.
/// </para>
/// </remarks>
internal static class RecruiterErasureTestServices
{
    /// <summary>
    /// A TEST pepper, derived rather than written as a base64 constant.
    /// <see cref="CompanyWatchPseudonymizationOptions"/> has no default and will not get one, so a
    /// test must supply its own — and a base64 blob in a public repo reads like a leaked key to
    /// every scanner and every human. 33 bytes, over the validator's 32-byte floor.
    /// </summary>
    private static readonly string TestWatchPepperBase64 =
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("jobbliggaren-test-watch-pepper-01"));

    public static IServiceCollection AddRecruiterErasureMatchQuery(this IServiceCollection services)
    {
        services.AddScoped<IRecruiterErasureMatchQuery, RecruiterErasureMatchQuery>();
        services.AddSingleton<IProtectedIdentityTokenizer>(
            new HmacProtectedIdentityTokenizer(Options.Create(
                new CompanyWatchPseudonymizationOptions { PepperBase64 = TestWatchPepperBase64 })));

        return services;
    }
}
