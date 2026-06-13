using System.Reflection;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
using Mediator;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Lock-in för PagedResult&lt;T&gt;-kontraktet (TD-55 retro-fit, H-4 hardening 2026-05-11,
/// F2-P7 utvidgad 2026-05-12 — TD-56 stängd, ListJobAdsQuery är nu paginerad).
///
/// När en query har paged-semantik (<c>Page</c> + <c>PageSize</c> properties på record:n)
/// MÅSTE den returnera <see cref="PagedResult{T}"/> — inte <c>IReadOnlyList&lt;T&gt;</c>.
/// Regression-skydd mot framtida re-introduktion av "bare array"-return från
/// paginerade queries (vilket var en frontend typ-skew som TD-55 stängde).
///
/// Kanonisk paging-property är <c>Page</c> (matchar <see cref="PagedResult{T}.Page"/>).
/// H-4 (arch-audit 2026-05-11) renamade <c>PageNumber</c> → <c>Page</c> i alla queries
/// — heuristiken accepterar inte längre legacy-namnet, så regression till blandad
/// konvention bryter testet.
/// </summary>
public class PagedResultContractTests
{
    [Fact]
    public void Paged_queries_must_return_PagedResult_not_IReadOnlyList()
    {
        var pagedQueryTypes = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        || (t.IsValueType && !t.IsEnum))
            .Where(IsMediatorQuery)
            .Where(HasPagedSemantics)
            .ToList();

        pagedQueryTypes.ShouldNotBeEmpty(
            "Sanity: minst en paginerad query måste finnas i Application-lagret.");

        var offenders = pagedQueryTypes
            .Where(t => !ReturnsPagedResult(t))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            $"Paginerade queries måste returnera PagedResult<T>, inte IReadOnlyList<T>. " +
            $"Bryter kontraktet: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void GetApplicationsQuery_returns_PagedResult()
    {
        // Explicit regression-skydd för den kända typ-skew-buggen (TD-55).
        ReturnsPagedResult(typeof(GetApplicationsQuery)).ShouldBeTrue(
            "GetApplicationsQuery måste returnera PagedResult<ApplicationDto>.");
    }

    [Fact]
    public void GetResumesQuery_returns_PagedResult()
    {
        ReturnsPagedResult(typeof(GetResumesQuery)).ShouldBeTrue(
            "GetResumesQuery måste returnera PagedResult<ResumeListItemDto>.");
    }

    [Fact]
    public void ListJobAdsQuery_returns_PagedResult()
    {
        // F2-P7 (TD-56 stängd 2026-05-12). Tidigare opaginerad, nu paginerad.
        ReturnsPagedResult(typeof(ListJobAdsQuery)).ShouldBeTrue(
            "ListJobAdsQuery måste returnera PagedResult<JobAdDto>.");
    }

    private static bool IsMediatorQuery(Type type) =>
        type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

    private static bool HasPagedSemantics(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        // Kanonisk paging-property är `Page` (H-4 hardening 2026-05-11). Legacy-namnet
        // `PageNumber` accepteras inte — alla queries renamade till `Page` så heuristiken
        // är strikt och fångar regression till blandad konvention.
        var hasPage = properties.Any(p => p.Name == "Page" && p.PropertyType == typeof(int));
        var hasPageSize = properties.Any(p => p.Name == "PageSize" && p.PropertyType == typeof(int));
        return hasPage && hasPageSize;
    }

    private static bool ReturnsPagedResult(Type queryType)
    {
        var queryInterface = queryType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

        if (queryInterface is null)
            return false;

        var responseType = queryInterface.GetGenericArguments()[0];
        return responseType.IsGenericType
               && responseType.GetGenericTypeDefinition() == typeof(PagedResult<>);
    }
}
