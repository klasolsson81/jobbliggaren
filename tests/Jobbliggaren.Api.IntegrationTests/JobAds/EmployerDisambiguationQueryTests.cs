using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0087 D6/D7 (#311 PR-2b C2) — the employer-disambiguation projection against REAL
// Testcontainers-Postgres (NEVER EF-InMemory: ILIKE + GROUP BY over the STORED organization_number
// shadow column translate ONLY via Npgsql; InMemory gives false greens —
// feedback_ef_strongly_typed_vo_contains_translation).
//
// On-disk payload path: org.nr is a STORED generated column populated by Postgres at INSERT from
// raw_payload->'employer'->>'organization_number'. Each test uses a UNIQUE brand token in the
// company name + queries it, so the shared Api DB's other rows never contaminate the assertions
// (reference_api_integration_shared_db_contamination).
//
// This suite pins the Infra projection ONLY (raw org.nr, no masking — that is the handler's job,
// pinned by DisambiguateEmployersQueryHandlerTests). It proves: ILIKE case-insensitive contains,
// GROUP BY → one row per legal entity with COUNT, distinct entities, NULL-org.nr exclusion, the cap,
// count-desc ordering, and that Infra returns the RAW value even for a personnummer-shaped org.nr.
[Collection("Api")]
public class EmployerDisambiguationQueryTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedAdAsync(
        string organizationNumber, string companyName, string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var employerJson = organizationNumber is null
            ? $"{{\"name\":\"{companyName}\"}}"
            : $"{{\"name\":\"{companyName}\",\"organization_number\":\"{organizationNumber}\"}}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"employer\":{employerJson}}}";

        var jobAd = JobAd.Import(
            title: "Utvecklare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<Application.JobAds.Abstractions.EmployerAdGroup>> RunAsync(
        string nameQuery, int limit, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new EmployerDisambiguationQuery(db).SearchAsync(nameQuery, limit, ct);
    }

    // Unique brand token so ILIKE %token% matches ONLY this test's seeded ads in the shared DB.
    private static string NewBrand() => "Disam" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Search_IsCaseInsensitiveContains_OnCompanyName()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        await SeedAdAsync("5566010101", $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);

        // Lower-cased, partial term still matches (ILIKE contains).
        var result = await RunAsync(brand.ToLowerInvariant(), 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} Cars AB");
        result[0].OrganizationNumber.ShouldBe("5566010101");
        result[0].AdCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_GroupsByOrgNr_CountingAdsPerEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string org = "5566020202";

        // Three ads, SAME employer (org.nr + name) → one group, count 3.
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe(org);
        result[0].AdCount.ShouldBe(3);
    }

    [Fact]
    public async Task Search_DistinctEntities_SameBrand_YieldSeparateRows_OrderedByCountDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string orgBig = "5566030303";   // 2 ads
        const string orgSmall = "5566040404"; // 1 ad

        await SeedAdAsync(orgBig, $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(orgBig, $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(orgSmall, $"{brand} Bussar AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(2);
        // Most-prolific first (count desc).
        result[0].OrganizationNumber.ShouldBe(orgBig);
        result[0].AdCount.ShouldBe(2);
        result[1].OrganizationNumber.ShouldBe(orgSmall);
        result[1].AdCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_ExcludesAdsWithNullOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // One with org.nr, one without (B2-era payload: employer.name only).
        await SeedAdAsync("5566050505", $"{brand} Med AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(null!, $"{brand} Utan AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} Med AB");
    }

    [Fact]
    public async Task Search_ReturnsRawOrgNr_EvenWhenPersonnummerShaped()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string solePropOrgNr = "8501010101"; // 3rd digit '0' → personnummer-shaped

        await SeedAdAsync(solePropOrgNr, $"{brand} Enskild firma", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        // Infrastructure does NOT mask — it returns the RAW value (the handler masks at the surfacing
        // boundary, ADR 0087 D8(c) / DisambiguateEmployersQueryHandlerTests). This pins the SoC.
        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe(solePropOrgNr);
    }

    [Fact]
    public async Task Search_TreatsLikeWildcardsInTerm_AsLiterals()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // One name literally contains "50%"; another would match if % were a wildcard ("50" + rest).
        await SeedAdAsync("5566080801", $"{brand} 50% rabatt AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566080802", $"{brand} 5000 kr AB", $"ext-{Guid.NewGuid():N}", ct);

        // The term carries a literal "%"; EscapeLike must neutralise it so ILIKE treats it literally —
        // only the "50%"-named row matches, NEVER the "5000" row (which % would match as a wildcard).
        var result = await RunAsync($"{brand} 50%", 50, ct);

        result.Count.ShouldBe(1);
        result[0].CompanyName.ShouldBe($"{brand} 50% rabatt AB");
    }

    [Fact]
    public async Task Search_TieBreaksByNameAscending_WhenCountsEqual()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // Two entities, one ad each (EQUAL count) — the deterministic tiebreak is name ascending
        // (ordinal). Seeded Beta-before-Alfa so a stable order must re-sort them, not echo insert order.
        await SeedAdAsync("5566090901", $"{brand} Beta AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566090902", $"{brand} Alfa AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 50, ct);

        result.Count.ShouldBe(2);
        result[0].CompanyName.ShouldBe($"{brand} Alfa AB");
        result[1].CompanyName.ShouldBe($"{brand} Beta AB");
    }

    [Fact]
    public async Task Search_CapsResultsAtLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // Four distinct entities sharing the brand; a limit of 2 returns only the top 2.
        await SeedAdAsync("5566060601", $"{brand} A AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060602", $"{brand} B AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060603", $"{brand} C AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync("5566060604", $"{brand} D AB", $"ext-{Guid.NewGuid():N}", ct);

        var result = await RunAsync(brand, 2, ct);

        result.Count.ShouldBe(2);
    }

    // ═══════════════ #1546 — the suggest-scoped sibling (Active-only) ═══════════════

    private async Task<IReadOnlyList<Application.JobAds.Abstractions.EmployerAdGroup>> SuggestAsync(
        string nameTerm, int limit, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new EmployerDisambiguationQuery(db)
            .SuggestActiveEmployersAsync(nameTerm, limit, ct);
    }

    /// <summary>
    /// Archives the ad seeded at <paramref name="externalId"/> through the aggregate's own
    /// <see cref="JobAd.Archive"/> — the transition <c>ArchiveExternalJobAdCommand</c> drives and that
    /// <c>ExpireJobAdsJob</c> performs in bulk. NOT a hand-written status column: an Archived ad is a
    /// state production produces, and this is the actor that produces it (CLAUDE.md §5 <c>Tests:</c>).
    /// </summary>
    private async Task ArchiveSeededAdAsync(string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var url = $"https://example.com/jobs/{externalId}";
        var ad = await db.JobAds.SingleAsync(j => j.Url == url, ct);

        ad.Archive(clock).IsSuccess.ShouldBeTrue("the seeded ad must be Active before archiving");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The difference the method exists for. Asserts BOTH directions against the same row, so a green
    /// result cannot come from the term simply matching nothing.
    /// </summary>
    [Fact]
    public async Task SuggestActiveEmployers_ExcludesAnEmployerWhoseAdsAreAllArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        var externalId = $"ext-{Guid.NewGuid():N}";

        await SeedAdAsync("5566070701", $"{brand} AB", externalId, ct);
        (await SuggestAsync(brand, 50, ct)).Count.ShouldBe(1, "the ad is Active at this point");

        await ArchiveSeededAdAsync(externalId, ct);

        (await SuggestAsync(brand, 50, ct)).ShouldBeEmpty(
            "selecting a suggestion navigates to ?employer=, which ApplyFilter scopes to Active — an "
            + "employer with no active ads would be a chip with no target.");

        (await RunAsync(brand, 50, ct)).Count.ShouldBe(1,
            "SearchAsync must stay status-agnostic: following an employer is a bet on its FUTURE ads "
            + "(Klas, 2026-07-17). If this side moved too, the two reads were collapsed into one.");
    }

    /// <summary>
    /// The count a suggestion shows is the count the chip then shows. Same employer, one Active ad and
    /// one Archived, so the two methods must disagree by exactly one.
    /// </summary>
    [Fact]
    public async Task SuggestActiveEmployers_CountsOnlyActiveAds()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string org = "5566070702";
        var staleExternalId = $"ext-{Guid.NewGuid():N}";

        await SeedAdAsync(org, $"{brand} AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} AB", staleExternalId, ct);
        await ArchiveSeededAdAsync(staleExternalId, ct);

        var suggested = await SuggestAsync(brand, 50, ct);
        suggested.Count.ShouldBe(1);
        suggested[0].AdCount.ShouldBe(1, "the archived ad is not reachable through ?employer=");

        var searched = await RunAsync(brand, 50, ct);
        searched[0].AdCount.ShouldBe(2, "the follow picker counts every ad, by design");
    }

    /// <summary>
    /// Infrastructure does NO masking, for the suggest branch exactly as for its sibling. The
    /// personnummer exclusion (ADR 0087 D8(c), CTO bind F3) lives in the Application handler; if it
    /// ever migrates down here this fact goes red, which is the point.
    /// </summary>
    [Fact]
    public async Task SuggestActiveEmployers_ReturnsRawOrgNr_EvenWhenPersonnummerShaped()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();

        // Third digit 0 → personnummer-shaped by OrganizationNumber.IsPersonnummerShaped().
        await SeedAdAsync("8501012384", $"{brand} Konsult", $"ext-{Guid.NewGuid():N}", ct);

        var result = await SuggestAsync(brand, 50, ct);

        result.Count.ShouldBe(1);
        result[0].OrganizationNumber.ShouldBe("8501012384",
            "the port returns the RAW value; masking and exclusion are the handler's job.");
    }

    /// <summary>
    /// #1546 — one legal entity whose ads spell its name two ways must be ONE suggestion.
    ///
    /// <para>
    /// <c>company_name</c> is written per ad from the source payload and nothing normalises it against
    /// org.nr, so the sibling <c>SearchAsync</c>'s composite <c>GROUP BY (org.nr, name)</c> would split
    /// such an employer into two rows. That matters here and not there: a suggestion carries an
    /// <c>AdCount</c> the user then sees again on <c>?employer=</c>, which filters on org.nr ALONE — so
    /// a split row shows a count smaller than the page it leads to, and the <c>Take(limit)</c> can drop
    /// one fragment entirely.
    /// </para>
    ///
    /// <para>
    /// The premise the sibling rests on ("company_name is stable per org.nr") is written in two places
    /// and pinned in none. This fact does not argue about whether it holds in the corpus; it removes
    /// the dependency.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SuggestActiveEmployers_GroupsNameVariantsUnderOneOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        var brand = NewBrand();
        const string org = "5566070703";

        await SeedAdAsync(org, $"{brand} Bygg AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(org, $"{brand} Bygg Aktiebolag", $"ext-{Guid.NewGuid():N}", ct);

        var suggested = await SuggestAsync(brand, 50, ct);

        suggested.Count.ShouldBe(1,
            "one org.nr is one legal entity and one ?employer= filter, however its ads spell the name.");
        suggested[0].OrganizationNumber.ShouldBe(org);
        suggested[0].AdCount.ShouldBe(2,
            "the count must be what ?employer=<org.nr> then shows, not what one spelling of the name shows.");

        // The contrast that makes the assertion above mean something: the status-agnostic sibling still
        // groups on the composite and therefore still splits this employer in two.
        (await RunAsync(brand, 50, ct)).Count.ShouldBe(2,
            "SearchAsync is unchanged by #1546 — if this moved, the two methods were collapsed.");
    }

    /// <summary>
    /// LINK 1 OF 2 in the index chain, and the one that cannot drift.
    ///
    /// <para>
    /// The trigram index #1546 shipped is keyed on the EXPRESSION <c>lower(company_name)</c>. An
    /// <c>ILIKE</c> over the bare column is semantically identical and NOT index-servable, so the
    /// difference is invisible to every behavioural test in this file — on a per-keystroke surface it
    /// is a sequential scan of <c>job_ads</c> per keystroke.
    /// </para>
    ///
    /// <para>
    /// This fact reads the SQL EF actually emits from production's own expression tree, so it binds
    /// C# to the indexed expression. LINK 2 — that this expression is served by
    /// <c>ix_job_ads_company_name_lower_trgm</c> — is
    /// <c>JobAdPlannerUsabilityOracleTests.EmployerSuggest_IsIndexServed</c>. Together they cover
    /// what neither does alone.
    /// </para>
    /// </summary>
    [Fact]
    public void SuggestActiveEmployers_EmitsTheIndexedLowerExpression_NeverIlike()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sql = EmployerDisambiguationQuery
            .SuggestActiveEmployersQuery(db, "volvo", 10)
            .ToQueryString();

        // Checked explicitly, because "LIKE" is a substring of "ILIKE": asserting the presence of
        // LIKE alone would pass against the very form this fact exists to forbid.
        sql.ShouldNotContain("ILIKE", Case.Insensitive,
            "ILIKE over the bare column cannot use ix_job_ads_company_name_lower_trgm, whose key is "
            + $"the expression lower(company_name). SQL:\n{sql}");

        sql.ShouldContain("lower(", Case.Insensitive,
            $"the company-name predicate must be written over lower(company_name). SQL:\n{sql}");

        sql.ShouldContain("LIKE", Case.Sensitive,
            $"the company-name predicate must be a LIKE against the lowered expression. SQL:\n{sql}");
    }
}
