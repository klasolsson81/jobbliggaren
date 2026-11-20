using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Commands.UnmarkRecentSearchQuickLink;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Commands;

/// <summary>
/// ADR 0188 D4 — a row deleted between Unmark's read and the unit of work's save (the user's delete in another tab,
/// the capture's eviction, the Art. 17 erasure) makes the UPDATE touch no row. The replay marker turns that into a
/// re-read that answers 404; without it the concurrency exception reaches the API unmapped, as a 500. The sibling
/// <c>DeleteRecentSearchCommand</c> carries no marker, so nothing else would notice it gone.
/// </summary>
public class UnmarkRecentSearchQuickLinkCommandTests
{
    [Fact]
    public void UnmarkRecentSearchQuickLinkCommand_Always_CarriesTheReplayMarker()
    {
        typeof(UnmarkRecentSearchQuickLinkCommand).IsAssignableTo(typeof(IReplayOnConcurrencyConflict)).ShouldBeTrue();
    }
}
