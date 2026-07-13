using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Companies;

/// <summary>
/// #454 (ADR 0088 D5) — the Testcontainers proof for <c>LookupCompanyQueryHandler.ActiveAdCount</c>,
/// the handler's OWN inline #447-idiom aggregate:
/// <code>db.JobAds.Count(j =&gt; EF.Property&lt;string?&gt;(j, "OrganizationNumber") == orgNr
///                             &amp;&amp; j.Status == JobAdStatus.Active)</code>
/// This filters the STORED generated <c>organization_number</c> column, which EF-InMemory NEVER
/// populates (<c>HasComputedColumnSql(stored: true)</c> is a Postgres-only concern) — so the
/// <see cref="LookupCompanyQueryHandlerTests"/> unit suite can only ever observe <c>ActiveAdCount == 0</c>
/// and explicitly defers the <c>&gt; 0</c> branch here (memory
/// <c>feedback_ef_strongly_typed_vo_contains_translation</c> / <c>reference_api_integration_shared_db_contamination</c>).
/// The sibling matching count (<see cref="IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync"/>)
/// against the same column is proven separately by <c>CompanyWatchMatchCountTests</c> (#452) and is
/// deliberately held to the not-assessed <c>null</c> path here (empty-SSYK profile) to isolate the
/// ActiveAdCount predicate.
///
/// <para>
/// The handler is constructed DIRECTLY over a real scoped <see cref="AppDbContext"/> (parity
/// <c>EmployerDisambiguationQueryTests</c> / <c>CompanyWatchMatchCountTests</c>) with a SUBSTITUTE
/// registry returning <c>Found</c> — going through the endpoint/Fake provider is impossible without
/// contaminating a shared-DB fixture org.nr (every Fake <c>Found</c> org.nr is asserted
/// <c>ActiveAdCount == 0</c> by the endpoint suite). Each test uses a PRIVATE legal-entity org.nr so
/// the shared <c>[Collection("Api")]</c> Postgres never cross-contaminates the count.
/// </para>
/// </summary>
[Collection("Api")]
public class CompanyLookupActiveAdCountTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICompanyRegistry _registry = Substitute.For<ICompanyRegistry>();
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    public CompanyLookupActiveAdCountTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(Guid.NewGuid());
        // Empty-SSYK ⇒ not-assessed: MatchingAdCount stays null and the per-user search port is never
        // touched, so this suite isolates the ActiveAdCount predicate (parity the unit suite default).
        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(EmptySsykProfile());
    }

    private static FullCandidateMatchProfile EmptySsykProfile() =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // Private, legal-entity-shaped (third digit 5 ≥ 2) org.nr, unique per test run so the shared Api
    // Postgres never leaks a count between tests (reference_api_integration_shared_db_contamination).
    // Third digit pinned to 6 (>= 2 ⇒ legal entity): a random third digit of 0/1 makes the value
    // personnummer-shaped, which the handler CORRECTLY refuses (ADR 0088 D4) — the original
    // "55" + 8 random digits generator drew a 1 in ~1/9 runs and flaked CI (PR #543 backend run).
    private static string NewOrgNr() => $"556{Random.Shared.Next(1_000_000, 9_999_999)}";

    // Seeds one imported JobAd whose raw_payload carries employer.organization_number — the STORED
    // generated organization_number column is computed by Postgres at INSERT (never in C#, never in
    // InMemory). Mirrors JobAdGeneratedColumnsTests / CompanyWatchMatchCountTests seeding verbatim.
    private async Task SeedAdAsync(
        string orgNr,
        CancellationToken ct,
        bool expired = false,
        bool archived = false)
    {
        var externalId = $"clac-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"Test Company AB\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Aktiv annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        // Archived: a real domain transition (status='Archived') — excluded by status='Active'.
        if (archived)
            jobAd.Archive(clock);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);

        // JobAd has no domain Expire transition; stamp the value-converted Status shadow via EF direct
        // (parity CompanyWatchMatchCountTests) so status='Active' excludes it.
        if (expired)
        {
            db.Entry(jobAd).Property(nameof(JobAd.Status)).CurrentValue = JobAdStatus.Expired;
            await db.SaveChangesAsync(ct);
        }

    }

    // Builds the SUT over a FRESH scoped AppDbContext (separate from the seed scope so Postgres has
    // materialised the generated column) + a registry stub that resolves the seeded org.nr to Found.
    private (IServiceScope Scope, LookupCompanyQueryHandler Handler) NewHandler(string foundOrgNr, string name)
    {
        _registry.LookupAsync(
                Arg.Is<OrganizationNumber>(o => o.Value == foundOrgNr), Arg.Any<CancellationToken>())
            .Returns(CompanyRegistryLookup.Found(new CompanyRegistryEntry(foundOrgNr, name)));

        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new LookupCompanyQueryHandler(
            db, _currentUser, _registry, _profileBuilder, _perUserSearch);
        return (scope, handler);
    }

    [Fact]
    public async Task Handle_ActiveAdCount_CountsMatchingActiveAds_AndKeysOnOrganizationNumber()
    {
        // The load-bearing >0 branch InMemory cannot prove: the #447 count over the STORED generated
        // organization_number column, keyed on THIS org.nr (a different employer's Active ad must not
        // bleed in).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        var otherOrgNr = NewOrgNr();

        await SeedAdAsync(orgNr, ct);
        await SeedAdAsync(orgNr, ct);
        await SeedAdAsync(otherOrgNr, ct); // different employer — must be excluded by the org.nr key

        var (scope, handler) = NewHandler(orgNr, "Testbolaget AB");
        using var dispose = scope;

        var result = await handler.Handle(new LookupCompanyQuery(orgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CompanyLookupDto.StatusFound);
        result.Value.OrganizationNumber.ShouldBe(orgNr);
        result.Value.ActiveAdCount.ShouldBe(2,
            "ActiveAdCount MÅSTE räkna exakt de 2 Active-annonserna för DENNA org.nr via den STORED " +
            "generated organization_number-kolumnen — en annan arbetsgivares annons får aldrig blöda " +
            "in (org.nr-nyckeln). Detta är >0-grenen som InMemory inte kan bevisa.");
        // Empty-SSYK ⇒ not-assessed null; the per-user matching port stays untouched.
        result.Value.MatchingAdCount.ShouldBeNull();
        await _perUserSearch.DidNotReceiveWithAnyArgs().CountPerUserByEmployerAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveAdCount_ExcludesExpiredAndArchived()
    {
        // The status='Active' predicate is the WHOLE exclusion (#821 — JobAd has no soft-delete axis
        // and no query filter). Proven against real Postgres (InMemory neither populates the generated
        // column nor applies the value-converted Status shadow the same way).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();

        await SeedAdAsync(orgNr, ct);                       // Active → counted
        await SeedAdAsync(orgNr, ct, expired: true);        // Expired → excluded (status)
        await SeedAdAsync(orgNr, ct, archived: true);       // Archived → excluded (status)

        var (scope, handler) = NewHandler(orgNr, "Testbolaget AB");
        using var dispose = scope;

        var result = await handler.Handle(new LookupCompanyQuery(orgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveAdCount.ShouldBe(1,
            "Endast den Active-annonsen ska räknas — Expired/Archived exkluderas av status='Active', " +
            "soft-deleted av den globala soft-delete-query-filtren (ADR 0048).");
    }
}
