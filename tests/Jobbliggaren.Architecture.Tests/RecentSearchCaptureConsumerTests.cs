using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Infrastructure.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2285 (ADR 0060 Amendment 2026-11-22) — a search reaches recent searches only by the capture command, the POST a
/// Server Action sends when the user searches on purpose, so no query, behaviour or GET can store one again.
/// <see cref="UserJobAdMatchClaimerTests"/>' IL sweep of every production assembly finds each method's callers, through
/// the port or the adapter, however the instance was obtained. The sets are exact: a predicate such as "is a command
/// handler" would let the next writer in without a test failing.
/// </summary>
public class RecentSearchCaptureConsumerTests
{
    [Fact]
    public void Only_the_capture_command_stores_a_recent_search() =>
        CallersOf(nameof(IRecentJobSearchCapturer.CaptureAsync)).ShouldBe(
            [$"{typeof(CaptureRecentSearchCommandHandler).FullName}.Handle"],
            "#2285: a search is stored only by an intentional POST, never by a query or a behaviour.");

    [Fact]
    public void The_gate_is_asked_by_the_list_and_the_capture_command_only() =>
        UserJobAdMatchClaimerTests.ReferencesTo(typeof(RecentSearchCaptureGate), nameof(RecentSearchCaptureGate.Admit))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList()
            .ShouldBe(
                [
                    $"{typeof(ListJobAdsQueryHandler).FullName}.Handle",
                    $"{typeof(CaptureRecentSearchCommandHandler).FullName}.Handle",
                ],
                "#2199 PR 2b / #2285: the list answers the gate's verdict and the command stores what the gate admits.");

    private static List<string> CallersOf(string method) =>
        [.. UserJobAdMatchClaimerTests.ReferencesTo(typeof(IRecentJobSearchCapturer), method)
            .Concat(UserJobAdMatchClaimerTests.ReferencesTo(typeof(RecentJobSearchCapturer), method))
            .Distinct()
            .Order(StringComparer.Ordinal)];
}
