using System.Linq.Expressions;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — the re-ingest requirements backfill.
/// <see cref="BackfillJobAdRequirementsJob"/> (parity <c>BackfillJobAdKlass2Job</c>)
/// drives <see cref="JobAdRefetchBackfillRunner"/> with the LOCKED predicate
/// <c>j =&gt; j.RawPayload != null &amp;&amp; !EF.Functions.JsonExists(j.RawPayload, "must_have")</c>:
/// rows whose <c>raw_payload</c> predates the must_have POCO are re-fetched, the
/// re-written payload carries must_have, and the extractor populates Requirement
/// terms. Real Postgres (Testcontainers via <see cref="ApiFactory"/> — the
/// <c>jsonb_exists</c> predicate + the full re-ingest Mediator pipeline only run on
/// the real engine; NEVER EF-InMemory) with the REAL Upsert pipeline; <c>IJobSource</c>
/// is the only substitute (the JobTech refetch is faked, parity the Klass2/refetch
/// backfill which the unit test mirrors with a substitute source).
///
/// <para>
/// The runner is constructed DIRECTLY with the substitute <see cref="IJobSource"/>
/// but the factory's REAL <see cref="IServiceScopeFactory"/> (so each item's
/// <c>UpsertExternalJobAdCommand</c> runs through the real handler + real extractor +
/// real Postgres). The job wrapper just supplies this predicate + auditJobType
/// "backfill-requirements"; we pin the predicate's behavior here (the architect's
/// recommended Testcontainers pin of the jsonb_exists translation).
/// </para>
///
/// RED until: <c>JobTechHit.must_have</c> POCO + <c>JobAdImportItem.Requirements</c> +
/// the extractor requirement pass + the ingest hook ship (so a re-fetched payload
/// carrying must_have lands Requirement terms and flips the predicate to "skip").
/// </summary>
[Collection("Api")]
public sealed class BackfillJobAdRequirementsJobTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // THE LOCKED F4-4b re-ingest predicate (CTO Decision 2A / architect Del 2):
    // a row whose raw_payload lacks the must_have key predates the POCO expansion →
    // must be re-ingested; once re-ingested (payload carries must_have) it is skipped.
    private static Expression<Func<JobAd, bool>> MissingMustHavePredicate =>
        j => j.RawPayload != null && !EF.Functions.JsonExists(j.RawPayload, "must_have");

    private static BackfillRunnerOptions Opts =>
        new(PerItemDelayMs: 0, MaxItemsPerRun: 1_000_000, ProgressLogEvery: 1000);

    // The refetched item the substitute source returns: a payload that NOW carries
    // must_have (so the re-write flips the predicate) + Requirements with skill
    // concepts (so the extractor produces Requirement terms).
    private static JobAdImportItem RefetchedItemWithMustHave(string externalId)
    {
        // Sanitized payload re-emitting the must_have key (what the new POCO produces).
        var payload =
            "{\"id\":\"" + externalId + "\",\"must_have\":{\"skills\":" +
            "[{\"concept_id\":\"Rq01_must_aaa\",\"label\":\"C#\",\"weight\":10}]}}";
        return new JobAdImportItem(
            ExternalId: externalId,
            Title: $"Refetched-{externalId}",
            CompanyName: "Region Stockholm",
            Description: "Beskrivning av tjänsten.",
            Url: $"https://example.com/jobs/{externalId}",
            PublishedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            SanitizedRawPayload: payload,
            // This payload carries no taxonomy keys — the facets are legitimately empty (#841).
            Facets: TestFacets.FromPayload(payload),
            Requirements: [new JobAdRequirement(ExtractedTermSource.MustHave, "Rq01_must_aaa", "C#", 10)], DeclaredContacts: []);
    }

    private JobAdRefetchBackfillRunner NewRunner(IJobSource jobSource)
    {
        // Resolve the runner's SCOPED dependencies (IAppDbContext + ISystemEventAuditor)
        // from a real scope — the factory's root provider validates scopes and refuses to
        // hand out scoped services (the runner's auditor/db are scoped; scopeFactory/clock
        // are singletons). The scope outlives the test method (leaked; the process exits
        // after). The runner's REAL scopeFactory → real Mediator/Upsert pipeline per item;
        // the runner makes its OWN per-item child scopes.
        var serviceProvider = _factory.Services.CreateScope().ServiceProvider;
        return new(
            jobSource: jobSource,
            scopeFactory: serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            db: serviceProvider.GetRequiredService<IAppDbContext>(),
            clock: serviceProvider.GetRequiredService<IDateTimeProvider>(),
            auditor: serviceProvider.GetRequiredService<ISystemEventAuditor>(),
            logger: NullLogger<JobAdRefetchBackfillRunner>.Instance);
    }

    // ---------------------------------------------------------------
    // Seeding — insert a JobAd whose raw_payload either LACKS or HAS must_have.
    // ---------------------------------------------------------------

    private async Task SeedAdAsync(string externalId, bool withMustHave, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = withMustHave
            ? "{\"id\":\"" + externalId + "\",\"must_have\":{\"skills\":[]}}"
            : "{\"id\":\"" + externalId + "\"}"; // pre-F4-4b payload — no must_have key

        var jobAd = JobAd.Import(
            title: $"Seed-{externalId}",
            company: Company.Create("Region Stockholm").Value,
            description: "Beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private async Task<JobAd> ReloadAsync(string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds.AsNoTracking()
            .FirstAsync(j => j.External!.ExternalId == externalId, ct);
    }

    // ===============================================================
    // Rows lacking must_have are refetched → Requirement terms populated
    // ===============================================================

    [Fact]
    public async Task RunAsync_SelectsRowsLackingMustHave_RefetchesAndPopulatesRequirements()
    {
        var ct = TestContext.Current.CancellationToken;
        var externalId = $"req-bf-missing-{Guid.NewGuid():N}";
        await SeedAdAsync(externalId, withMustHave: false, ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(externalId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobAdImportItem?>(RefetchedItemWithMustHave(externalId)));

        var runner = NewRunner(jobSource);
        var counts = await runner.RunAsync(
            MissingMustHavePredicate, Opts, "backfill-requirements", ct);

        counts.RefetchAttempted.ShouldBeGreaterThanOrEqualTo(1,
            "raden saknar must_have → ska re-hämtas.");
        await jobSource.Received().RefetchByExternalIdAsync(externalId, Arg.Any<CancellationToken>());

        // The re-ingested ad now carries Requirement terms in extracted_terms.
        var reloaded = await ReloadAsync(externalId, ct);
        reloaded.ExtractedTerms.ShouldNotBeNull();
        reloaded.ExtractedTerms!.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == "Rq01_must_aaa",
            "re-ingest ska populera Requirement-termer från must_have-skills.");
        // And its payload now has must_have → it would be excluded on a re-run.
        reloaded.RawPayload.ShouldNotBeNull();
        reloaded.RawPayload!.ShouldContain("must_have");
    }

    // ===============================================================
    // A row that ALREADY has must_have is excluded by the predicate (not refetched)
    // ===============================================================

    [Fact]
    public async Task RunAsync_ExcludesRowsThatAlreadyHaveMustHave()
    {
        var ct = TestContext.Current.CancellationToken;
        var hasId = $"req-bf-has-{Guid.NewGuid():N}";
        await SeedAdAsync(hasId, withMustHave: true, ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<JobAdImportItem?>(RefetchedItemWithMustHave(ci.Arg<string>())));

        var runner = NewRunner(jobSource);
        await runner.RunAsync(MissingMustHavePredicate, Opts, "backfill-requirements", ct);

        // The predicate excludes the already-has-must_have row → never refetched.
        await jobSource.DidNotReceive().RefetchByExternalIdAsync(hasId, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Idempotent re-run — after the first run a row now has must_have → skipped
    // ===============================================================

    [Fact]
    public async Task RunAsync_SecondRun_IsIdempotent_DoesNotRefetchTheNowMustHaveRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var externalId = $"req-bf-idem-{Guid.NewGuid():N}";
        await SeedAdAsync(externalId, withMustHave: false, ct);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.RefetchByExternalIdAsync(externalId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobAdImportItem?>(RefetchedItemWithMustHave(externalId)));

        // First run re-ingests the row (its payload now carries must_have).
        await NewRunner(jobSource).RunAsync(
            MissingMustHavePredicate, Opts, "backfill-requirements", ct);
        jobSource.ClearReceivedCalls();

        // Second run: the row now HAS must_have → excluded by the predicate → not
        // refetched again. Restart-safe (the NULL-key filter is the idempotency).
        await NewRunner(jobSource).RunAsync(
            MissingMustHavePredicate, Opts, "backfill-requirements", ct);

        await jobSource.DidNotReceive().RefetchByExternalIdAsync(externalId, Arg.Any<CancellationToken>());
    }
}
