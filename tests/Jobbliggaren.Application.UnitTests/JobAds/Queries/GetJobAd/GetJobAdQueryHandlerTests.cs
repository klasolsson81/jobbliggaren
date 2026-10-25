using Jobbliggaren.Application.JobAds.Queries.GetJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetJobAd;

public class GetJobAdQueryHandlerTests
{
    private static JobAd CreateJobAd(string title = "Backend Developer", JobSource? source = null) =>
        JobAd.Create(
            title,
            Company.Create("Klarna").Value,
            "Vi söker en backend-utvecklare.",
            "https://jobs.klarna.com/job/1",
            source ?? JobSource.Manual,
            FakeDateTimeProvider.Default.UtcNow,
            null,
            FakeDateTimeProvider.Default).Value;

    [Fact]
    public async Task Handle_WhenJobAdExists_ReturnsDto()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd("Backend Developer");
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(jobAd.Id.Value);
        result.Value.Title.ShouldBe("Backend Developer");
        result.Value.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Handle_WhenJobAdNotFound_ReturnsNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();
        var handler = new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default);

        var result = await handler.Handle(new GetJobAdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound, "an id we never held is a 404.");
    }

    /// <summary>
    /// #842 — an erased ad is GONE (410), never MISSING (404). The distinction is not pedantry:
    /// 404 tells the person holding a link to an ad she applied to that we never had it, which is
    /// false — and manufacturing a false statement to a data subject is the exact defect class this
    /// issue is about. She keeps her own frozen record (ADR 0086's AdSnapshot) either way.
    /// </summary>
    [Fact]
    public async Task Handle_WhenJobAdErased_ReturnsGone_NotNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd();
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        jobAd.Erase(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Gone,
            "an erased ad existed and is deliberately gone — 410, which the central mapper "
            + "already translates. A 404 here would say we never held it.");

        // The message is deliberately NEUTRAL. The ad id is public and Arbetsförmedlingen publishes
        // the same ad in its open Historiska annonser dataset — so saying "raderad enligt artikel
        // 17" would let anyone correlate the two and infer that the recruiter named in that ad
        // exercised a right. The erasure would then broadcast the very fact it exists to protect.
        result.Error.Code.ShouldBe("JobAd.Gone");
        result.Error.Message.ShouldNotContain("artikel");
        result.Error.Message.ShouldNotContain("raderat");
    }

    // ── #842 PR4 — the recruiter contact block on the detail projection ──────────────────────────

    /// <summary>
    /// An IMPORTED ad (JobAd.Import runs the scrub/promote funnel; JobAd.Create does not) carrying a
    /// single DECLARED contact and a clean body, so Contacts holds exactly that one contact.
    /// <c>ChangeTracker.Clear()</c> forces the read to re-materialise the jsonb VO through its value
    /// converter — the round-trip a read handler must survive when it re-hydrates the persisted VO.
    /// </summary>
    private static async Task<JobAd> ImportJobAdWithDeclaredContactAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct,
        ApplicationUrl? applicationUrl = null, DateTimeOffset? expiresAt = null)
    {
        var externalId = TestIds.ExternalId();
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var declared = AdContact.TryCreate(
            "Anna Karlsson", "Rekryterare", "anna@acme.se", "070-123 45 67",
            AdContactOrigin.Declared)!;

        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Klarna").Value,
            description: "Vi söker en backend-utvecklare.",
            url: "https://jobs.klarna.com/job/1",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            declaredContacts: [declared],
            publishedAt: FakeDateTimeProvider.Default.UtcNow,
            expiresAt: expiresAt,
            clock: FakeDateTimeProvider.Default, applicationUrl: applicationUrl, extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return jobAd;
    }

    // ── #2191 — the employer's application page on the detail projection ─────────────────────────

    private const string EmployerPage = "https://career.example.se/jobs/4567890";

    [Fact]
    public async Task Handle_WhenActiveImportedAdHasAnApplicationUrl_ReturnsIt()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = await ImportJobAdWithDeclaredContactAsync(
            db, CancellationToken.None, ApplicationUrl.FromSource(EmployerPage));

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.ApplicationUrl.ShouldBe(EmployerPage,
            "the round-trip through the value converter yields the address the ACL mapped.");
        result.Value.Url.ShouldBe("https://jobs.klarna.com/job/1", "Url stays the ad's own page.");
    }

    [Fact]
    public async Task Handle_WhenTheAdWasArchived_ReturnsNoApplicationUrl()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = await ImportJobAdWithDeclaredContactAsync(
            db, CancellationToken.None, ApplicationUrl.FromSource(EmployerPage));
        var tracked = await db.JobAds.FirstAsync(j => j.Id == jobAd.Id, CancellationToken.None);
        tracked.Archive(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.ApplicationUrl.ShouldBeNull("an archived ad no longer accepts applications.");
    }

    [Fact]
    public async Task Handle_WhenManualAd_ReturnsNoApplicationUrl()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd();
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenImportedAdHasDeclaredContact_PopulatesContactsAsNotDerived()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = await ImportJobAdWithDeclaredContactAsync(db, CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var contact = result.Value.Contacts.ShouldHaveSingleItem();
        contact.IsDerived.ShouldBeFalse(
            "a declared contact is the advertiser's own declaration, never our inference (R1(b)).");
        contact.Name.ShouldBe("Anna Karlsson");
        contact.Email.ShouldBe("anna@acme.se");
    }

    [Fact]
    public async Task Handle_WhenManualAdHasNoContacts_ReturnsEmptyContactsNotNull()
    {
        // JobAd.Create (the manual path) never populates Contacts, so the column is null. The DTO's
        // ListFrom(null) must project to [] — the wire is never null (parity with the empty case; the
        // null-vs-empty distinction is a retention concern, not a display one).
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd();
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Contacts.ShouldNotBeNull("Contacts is never null on the wire — ListFrom yields [].");
        result.Value.Contacts.ShouldBeEmpty();
    }

    // ── #2214 PR 3 — IsListed on the detail projection (ADR 0183 D6) ─────────────────────────────
    // IsListed is JobAdPublication.Classify(..) == Listed, computed from the IMPORT provenance
    // (External?.Source), NEVER the user-settable JobAd.Source. The signed-in "Dela annonsen" row
    // renders only when true. Each state below is produced by a real src path (JobAd.Import /
    // JobAd.Create / Archive / the handler's clock), never a hand-seeded column (AGENTS.md §5 Tests).
    // The Classify ≡ IsListed-SQL agreement is pinned separately (JobAdPublicationIsListedAgreementTests);
    // these pin that the DTO carries Classify's verdict for the state the handler read.

    [Fact]
    public async Task Handle_WhenImportedPlatsbankenAdIsActiveAndNotExpired_IsListedTrue()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = await ImportJobAdWithDeclaredContactAsync(db, CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.IsListed.ShouldBeTrue(
            "an imported Platsbanken ad that is Active and not expired is what the public surface lists.");
    }

    [Fact]
    public async Task Handle_WhenImportedAdWasArchived_IsListedFalse()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = await ImportJobAdWithDeclaredContactAsync(db, CancellationToken.None);
        var tracked = await db.JobAds.FirstAsync(j => j.Id == jobAd.Id, CancellationToken.None);
        tracked.Archive(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.IsListed.ShouldBeFalse("an archived ad is Ended — the public surface 410s it.");
    }

    [Fact]
    public async Task Handle_WhenImportedAdIsActiveButPastItsDeadline_IsListedFalse()
    {
        // The archiver (ExpireJobAdsJob) has not run, so the row is still Active in the DB; the
        // deadline boundary is the handler's clock. Import with a deadline, then read past it.
        await using var db = TestAppDbContextFactory.Create();
        var published = FakeDateTimeProvider.Default.UtcNow;
        var jobAd = await ImportJobAdWithDeclaredContactAsync(
            db, CancellationToken.None, expiresAt: published.AddDays(30));
        var afterDeadline = new FakeDateTimeProvider(published.AddDays(31));

        var result = await new GetJobAdQueryHandler(db, afterDeadline)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.IsListed.ShouldBeFalse(
            "past its deadline the ad is Ended even while still Active in the row (Classify reads the clock).");
    }

    [Fact]
    public async Task Handle_WhenAdHasSourcePlatsbankenButNoExternal_IsListedFalse()
    {
        // POST /api/v1/job-ads accepts Source="Platsbanken" and stores an ownerless row with NO
        // External (JobAd.Create sets Source, never External). Listedness reads External?.Source, so
        // such an ad is NotPublic — the user-settable Source can never make it listed (SA-2 M-2).
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd(source: JobSource.Platsbanken);
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.IsListed.ShouldBeFalse(
            "Source is user-settable; without an import (External) the ad was never public (NotPublic).");
    }

    [Fact]
    public async Task Handle_WhenManualAd_IsListedFalse()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd();
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new GetJobAdQueryHandler(db, FakeDateTimeProvider.Default)
            .Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.Value.IsListed.ShouldBeFalse("a manual ad is never on the public surface.");
    }
}
