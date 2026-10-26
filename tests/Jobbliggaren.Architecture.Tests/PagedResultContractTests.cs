using System.Reflection;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
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
///
/// <para>
/// <b><c>Result&lt;PagedResult&lt;T&gt;&gt;</c> är AVSIKTLIGT inte tillåtet</b> (senior-cto-advisor
/// 2026-07-13, #560 PR-2 — läs detta INNAN du sträcker dig efter en <c>Result&lt;&gt;</c>-unwrap i
/// <see cref="ReturnsPagedResult"/>). Två skäl. (1) <see cref="PagedResult{T}"/> implementerar
/// <c>IRecentSearchCaptureResponse</c>, så huset har minst en pipeline-behavior nycklad på
/// RESPONSTYPENS identitet med en TYST no-op-grind — ett <c>Result&lt;&gt;</c>-hölje byter den
/// identiteten och kopplar tyst bort responsen från varje sådan mekanism. Testet låser responstypen,
/// inte bara payload-formatet. (2) En paginerad query vars enda felväg är not-found har inget för
/// <c>ErrorKind</c> att diskriminera: 401 kastas av <c>AuthorizationBehavior</c> och 400 av
/// <c>ValidationBehavior</c>, så de når aldrig Result-kanalen. Husregeln: <b>enda felet är not-found →
/// <c>T?</c></b> (endpointen mappar null → 404; se <c>RunSavedSearchQuery</c> och
/// <c>BrowseCompaniesQuery</c>); <b><c>Result&lt;T&gt;</c> reserveras för queries där
/// <c>ErrorKind</c> faktiskt väljer</b> (jfr <c>LookupCompanyQuery</c>, som bär både en
/// Validation-vägran och en NotFound).
///
/// <b>Men <c>T?</c> bär TVÅ betydelser i huset, och bara den ena är ett fel.</b> Den andra är ett
/// PRODUKTBESLUT: null = "vi frågade inte", och endpointen serialiserar det i en 200-kropp i stället
/// för att mappa det till 404 — se <c>GetCompanySearchMagnitudeQuery</c> (#1149, ingen magnitud på en
/// ofiltrerad browse-all) och <c>GetLatestPendingParsedResumeQuery</c>. Läs alltså aldrig <c>T?</c> som
/// "alltså 404" utan att läsa queryns egen docblock; de två formerna är omöjliga att skilja på
/// signaturen ensam, och att harmonisera dem åt fel håll skulle 404:a en normal vy.
/// </para>
///
/// <para>
/// <b>A third form, declared: a closed union whose page arm carries <see cref="PagedResult{T}"/></b> (ADR 0176, the
/// public catalogue). <c>GetPublicJobAdCatalogueQuery</c> has <c>Page</c> and a constant page size, so the heuristic
/// above does not see it. Its answer has two arms that are not a page: a page past the end (404) and a snapshot too old
/// to answer (503), which is availability and no <c>ErrorKind</c>. The page arm still carries
/// <see cref="PagedResult{T}"/>, pinned by its own test below, so the exception is stated rather than missed.
/// </para>
///
/// <para>
/// <b>A fourth form, declared: a flat page plus one verdict</b> (#2199 PR 2b, senior-cto-advisor 2026-10-26).
/// <c>ListJobAdsQuery</c> answers <see cref="JobAdListPage"/>: every member of <see cref="PagedResult{T}"/>,
/// delegated to the page it wraps, and whether the search may be stored in recent searches. It is flat so the
/// wire only gains a field, and it implements <c>IRecentSearchCaptureResponse</c> itself, which is the identity
/// reason (1) above protects. The generic test exempts exactly this type, and its own test below pins all three.
/// </para>
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
            .Where(t => t != typeof(ListJobAdsQuery)) // the declared fourth form, pinned below
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
    public void ListJobAdsQuery_answers_the_declared_fourth_form()
    {
        // F2-P7 (TD-56 stängd 2026-05-12) made it paged; #2199 PR 2b wraps the page with one verdict.
        ResponseTypeOf(typeof(ListJobAdsQuery)).ShouldBe(typeof(JobAdListPage));

        static IEnumerable<(string, Type)> Members(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p.Name, p.PropertyType))
                .OrderBy(m => m.Name, StringComparer.Ordinal);

        Members(typeof(JobAdListPage)).ShouldBe(
            Members(typeof(PagedResult<JobAdDto>))
                .Append((nameof(JobAdListPage.IsStorableAsRecentSearch), typeof(bool)))
                .OrderBy(m => m.Item1, StringComparer.Ordinal));

        typeof(Jobbliggaren.Application.RecentJobSearches.Common.IRecentSearchCaptureResponse)
            .IsAssignableFrom(typeof(JobAdListPage))
            .ShouldBeTrue("without the marker the capture behaviour silently stores nothing");
    }

    [Fact]
    public void GetPublicJobAdCatalogueQuery_Available_arm_carries_PagedResult()
    {
        ReturnsPagedResult(typeof(GetPublicJobAdCatalogueQuery)).ShouldBeFalse(
            "the catalogue answers a closed union, not a bare PagedResult (ADR 0176).");

        var available = typeof(PublicJobAdCataloguePage.Available).GetProperty(nameof(PublicJobAdCataloguePage.Available.Rows));
        available.ShouldNotBeNull();
        available.PropertyType.ShouldBe(typeof(PagedResult<PublicJobAdSummaryDto>));
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

    private static Type? ResponseTypeOf(Type queryType) =>
        queryType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
            ?.GetGenericArguments()[0];

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
