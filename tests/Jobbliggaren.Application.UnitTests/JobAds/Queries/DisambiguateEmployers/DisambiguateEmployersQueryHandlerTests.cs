using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.DisambiguateEmployers;

// ADR 0087 D6/D8(c) (#311 PR-2b C2) — the disambiguation handler's job beyond delegation is the
// sole-prop personnummer guard at the surfacing boundary: it masks a personnummer-shaped org.nr
// (3rd digit < 2 → OrganizationNumber.IsPersonnummerShaped) to null + IsProtectedIdentity=true
// BEFORE the raw value becomes a wire DTO — a raw org.nr is never surfaced un-flagged (CLAUDE.md §5,
// highest priority). The Infra projection (ILIKE + GROUP BY, RAW org.nr, no masking) is pinned
// separately by EmployerDisambiguationQueryTests (Testcontainers). The DTO's structural
// mask-capability is pinned by OrganizationNumberSurfacingGuardTests (fail-closed partition).
//
// CA2012: NSubstitute stubbing of ValueTask-returning port members is a known analyzer
// false-positive (the substitute call is never consumed — NSubstitute intercepts it to register
// Returns). Suppression scoped to the mock setup, never production code. Mirrors
// DeriveOccupationCodesQueryHandlerTests.
#pragma warning disable CA2012
public class DisambiguateEmployersQueryHandlerTests
{
    private readonly IEmployerDisambiguationQuery _projection =
        Substitute.For<IEmployerDisambiguationQuery>();

    private DisambiguateEmployersQueryHandler Handler() => new(_projection);

    // A legal-entity org.nr (3rd digit >= 2 — Skatteverket group number for a legal person).
    private const string LegalOrgNr = "5566010101"; // 3rd digit '6'
    // A personnummer-shaped org.nr (3rd digit < 2 — an enskild firma whose org.nr == the owner's pnr).
    private const string SolePropOrgNr = "8501010101"; // 3rd digit '0'

    private void ProjectionReturns(params EmployerAdGroup[] groups)
    {
        _projection
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<EmployerAdGroup>>(groups));
    }

    [Fact]
    public async Task Handle_LegalEntityOrgNr_SurfacesRawValue_FlagFalse()
    {
        ProjectionReturns(new EmployerAdGroup(LegalOrgNr, "Volvo Cars AB", 12));

        var result = await Handler().Handle(
            new DisambiguateEmployersQuery("Volvo"), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe(LegalOrgNr);
        result[0].IsProtectedIdentity.ShouldBeFalse();
        result[0].CompanyName.ShouldBe("Volvo Cars AB");
        result[0].AdCount.ShouldBe(12);
    }

    [Fact]
    public async Task Handle_SolePropPersonnummerShapedOrgNr_MasksToNull_FlagTrue()
    {
        ProjectionReturns(new EmployerAdGroup(SolePropOrgNr, "Anna Andersson Enskild firma", 2));

        var result = await Handler().Handle(
            new DisambiguateEmployersQuery("Anna"), CancellationToken.None);

        result.Count.ShouldBe(1);
        // The raw personnummer-shaped org.nr is NEVER surfaced.
        result[0].OrganizationNumber.ShouldBeNull();
        result[0].IsProtectedIdentity.ShouldBeTrue();
        // The entity is still identifiable by name + count (data-minimal, not dropped).
        result[0].CompanyName.ShouldBe("Anna Andersson Enskild firma");
        result[0].AdCount.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_MixedList_MasksEachIndependently_AndPreservesProjectionOrder()
    {
        ProjectionReturns(
            new EmployerAdGroup(LegalOrgNr, "Legal AB", 5),
            new EmployerAdGroup(SolePropOrgNr, "Sole Prop", 1));

        var result = await Handler().Handle(
            new DisambiguateEmployersQuery("x"), CancellationToken.None);

        result.Count.ShouldBe(2);
        // Order preserved from the projection (which orders by ad count desc, Infra concern).
        result[0].OrganizationNumber.ShouldBe(LegalOrgNr);
        result[0].IsProtectedIdentity.ShouldBeFalse();
        result[1].OrganizationNumber.ShouldBeNull();
        result[1].IsProtectedIdentity.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_EmptyProjection_ReturnsEmpty()
    {
        ProjectionReturns();

        var result = await Handler().Handle(
            new DisambiguateEmployersQuery("nothing here"), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_TrimsQuery_AndPassesTheResultCap_ToTheProjection()
    {
        ProjectionReturns();

        await Handler().Handle(
            new DisambiguateEmployersQuery("  Volvo  "), CancellationToken.None);

        // The port receives the TRIMMED term (never leading/trailing whitespace) + the v1 cap.
        await _projection.Received(1).SearchAsync(
            "Volvo", DisambiguateEmployersQuery.MaxResults, Arg.Any<CancellationToken>());
    }
}
