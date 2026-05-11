using System.Reflection;
using JobbPilot.Application.Applications.Queries.GetApplications;
using JobbPilot.Application.Common;
using JobbPilot.Application.Resumes.Queries.GetResumes;
using Mediator;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// Lock-in för PagedResult&lt;T&gt;-kontraktet (TD-55 retro-fit).
///
/// När en query har paged-semantik (PageNumber + PageSize properties på record:n)
/// MÅSTE den returnera <see cref="PagedResult{T}"/> — inte <c>IReadOnlyList&lt;T&gt;</c>.
/// Regression-skydd mot framtida re-introduktion av "bare array"-return från
/// paginerade queries (vilket var en frontend typ-skew som TD-55 stängde).
///
/// ListJobAdsQuery är explicit exkluderad — den är opaginerad idag och får
/// hard-cap via <c>.Take(MaxItems)</c> i handler. Full paginering defererad
/// till Fas 2 JobTech-integration.
/// </summary>
public class PagedResultContractTests
{
    [Fact]
    public void Paged_queries_must_return_PagedResult_not_IReadOnlyList()
    {
        var pagedQueryTypes = typeof(JobbPilot.Application.AssemblyMarker).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        || (t.IsValueType && !t.IsEnum))
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

    private static bool HasPagedSemantics(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var hasPageNumber = properties.Any(p => p.Name == "PageNumber" && p.PropertyType == typeof(int));
        var hasPageSize = properties.Any(p => p.Name == "PageSize" && p.PropertyType == typeof(int));
        return hasPageNumber && hasPageSize;
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
