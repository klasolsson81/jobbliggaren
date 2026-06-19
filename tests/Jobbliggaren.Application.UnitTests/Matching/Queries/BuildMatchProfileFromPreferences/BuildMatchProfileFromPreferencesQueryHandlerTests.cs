using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.BuildMatchProfileFromPreferences;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.BuildMatchProfileFromPreferences;

// F4-12/F4-13 (senior-cto-advisor 2026-06-19 Decision B = B2) — the handler is now a
// thin delegation to IMatchProfileBuilder (the shared preference→profile SSOT). The
// mapping behaviour (DB-load, field mapping, honest-empty fallback, owner-scoping) is
// pinned by MatchProfileBuilderTests; here we only prove the handler forwards to the
// collaborator and returns its result unchanged (no handler-invokes-handler, CLAUDE.md §2.3).
public class BuildMatchProfileFromPreferencesQueryHandlerTests
{
    // Hand-rolled fake (over NSubstitute) — a ValueTask-returning port mocked via
    // NSubstitute trips CA2012 at the call-setup site; a tiny stub keeps the delegation
    // assertion clean.
    private sealed class StubProfileBuilder(CandidateMatchProfile result) : IMatchProfileBuilder
    {
        public int CallCount { get; private set; }

        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return new ValueTask<CandidateMatchProfile>(result);
        }
    }

    [Fact]
    public async Task Handle_DelegatesToProfileBuilder_AndReturnsItsResult()
    {
        var expected = new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: ["grp_12345"],
            PreferredRegionConceptIds: ["stockholm_AB"],
            PreferredEmploymentTypeConceptIds: ["et_fast"]);
        var builder = new StubProfileBuilder(expected);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(builder);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldBeSameAs(expected);
        builder.CallCount.ShouldBe(1);
    }
}
