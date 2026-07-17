using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 PR4 — the recruiter contact block on the two DETAIL read surfaces, pinned END TO END over
/// HTTP through the real Api host on real Postgres (Testcontainers via <see cref="ApiFactory"/>).
/// Two guarantees the handler/unit tests cannot see because they never touch the serializer:
/// <list type="bullet">
/// <item>the ad-detail wire carries <c>contacts[]</c> with camelCase <c>isDerived</c> flags, one
/// per entry (R1(b) surfaced to the FE);</item>
/// <item>the LIST/search wire carries NO contact surface at all — the R2/K1 bulk-harvest refusal,
/// one level below the L4 type lock (<c>RecruiterContactFtsLockTests</c>): the split makes ~37k
/// recruiters' structured contacts on the search wire structurally impossible, and this pins the
/// actual bytes, not just the CLR type.</item>
/// </list>
/// Every ad is built through <see cref="JobAd.Import"/> (the production funnel endpoint), never a
/// hand-seeded column.
/// </summary>
[Collection("Api")]
public sealed class RecruiterContactReadSurfaceTests(ApiFactory factory)
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private (AppDbContext Db, IServiceScope Scope) NewScope()
    {
        var scope = factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    private static IDateTimeProvider ClockAt(DateTimeOffset at)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(at);
        return clock;
    }

    // A unique, company-shaped 10-digit org.nr (starts 55 = AB). Uniqueness matters more than
    // realism: the employer facet is an exact string match, and the shared [Collection("Api")] DB
    // may hold other tests' ads — a unique org.nr guarantees the list page is exactly this ad.
    private static string NewOrgNr() => $"55{Random.Shared.NextInt64(10_000_000, 99_999_999)}";

    /// <summary>
    /// Seeds an ACTIVE imported ad with one DECLARED contact (never on the body) and, optionally, a
    /// body email that is UNCOVERED by it and so gets promoted (Origin=ExtractedFromBody). The
    /// externalId prefix is deliberately free of the substring "contact" — it rides the ad Url onto
    /// the list wire, where the bulk-harvest pin scans the raw bytes.
    /// </summary>
    private static async Task<JobAd> SeedImportedAdAsync(
        AppDbContext db, IDateTimeProvider clock, string title, string orgNr, string? bodyEmail,
        CancellationToken ct)
    {
        var externalId = $"readsurface-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var declared = AdContact.TryCreate(
            "Anna Rekryterare", "Rekryterare", "anna.declared@example.com", null,
            AdContactOrigin.Declared)!;

        var description = bodyEmail is null
            ? "Vi söker en backend-utvecklare till vårt team."
            : $"Vi söker en backend-utvecklare. Kontakta {bodyEmail} för frågor om tjänsten.";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Rekrytering AB").Value,
            description: description,
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.From(organizationNumber: orgNr),
            declaredContacts: [declared],
            publishedAt: clock.UtcNow,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd;
    }

    private static bool HasNonNull(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null;

    // ────────────────────────────────────────────────────────────────────────────────
    // 1. Ad detail — contacts[] on the wire, camelCase isDerived, one flag per entry
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ad_detail_serialises_contacts_with_camelCase_isDerived_per_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        Guid adId;
        var (db, scope) = NewScope();
        using (scope)
        {
            var ad = await SeedImportedAdAsync(
                db, ClockAt(T0), "Detaljvy backend-utvecklare", NewOrgNr(),
                bodyEmail: "jobb.extra@example.com", ct);
            adId = ad.Id.Value;

            // Counterfactual: the seeded ad holds BOTH a declared and a promoted contact in the DB.
            ad.Contacts.ShouldNotBeNull();
            ad.Contacts!.Contacts.Count.ShouldBe(2,
                "one declared + one uncovered body hit promoted (ExtractedFromBody).");
        }

        var response = await _client.GetAsync($"/api/v1/job-ads/{adId}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // camelCase wire (parity with ListJobAdsTests' totalCount/pageSize).
        json.TryGetProperty("contacts", out var contacts).ShouldBeTrue(
            "the DETAIL DTO serialises its recruiter block as camelCase `contacts`.");
        contacts.ValueKind.ShouldBe(JsonValueKind.Array);
        contacts.GetArrayLength().ShouldBe(2);

        // Declared sorts first in the VO's canonical order (Origin rank 0 before 1).
        var declared = contacts[0];
        declared.GetProperty("isDerived").GetBoolean().ShouldBeFalse(
            "the advertiser's declared contact is NOT derived (R1(b)); camelCase `isDerived`.");
        declared.GetProperty("name").GetString().ShouldBe("Anna Rekryterare");
        declared.GetProperty("email").GetString().ShouldBe("anna.declared@example.com");
        declared.GetProperty("role").GetString().ShouldBe("Rekryterare");

        var promoted = contacts[1];
        promoted.GetProperty("isDerived").GetBoolean().ShouldBeTrue(
            "the body-extracted hit is OUR inference — fail-closed IsDerived (R1(b)).");
        HasNonNull(promoted, "name").ShouldBeFalse(
            "a promoted body hit never carries a guessed name (no NER, ADR 0106 D5).");
        promoted.GetProperty("email").GetString().ShouldBe("jobb.extra@example.com");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // 2. Archived ad — still 200 (only Erased is 410), and contacts cleared to []
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ad_detail_returns_200_with_empty_contacts_for_an_archived_ad()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        Guid adId;
        var (db, scope) = NewScope();
        using (scope)
        {
            var clock = ClockAt(T0);
            var ad = await SeedImportedAdAsync(
                db, clock, "Arkiverad backend-utvecklare", NewOrgNr(), bodyEmail: null, ct);
            adId = ad.Id.Value;

            // Counterfactual: the Active ad HELD a contact (absence proves a gate only against a
            // prior presence), then Archive() clears it — retention (b1 §4.1).
            ad.Contacts.ShouldNotBeNull("the Active ad held the declared contact before archival.");
            ad.Archive(clock).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);

            var reloaded = await db.JobAds.AsNoTracking().SingleAsync(j => j.Id == ad.Id, ct);
            reloaded.Status.ShouldBe(JobAdStatus.Archived);
            reloaded.Contacts.ShouldBeNull("Archive() cleared the contacts column (retention, b1 §4.1).");
        }

        var response = await _client.GetAsync($"/api/v1/job-ads/{adId}", ct);

        // Only an ERASED ad is 410; an archived ad is still readable — its detail page renders the
        // frozen text and, now, an empty contact block.
        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            "an archived ad is still readable on the detail surface — only Erased is 410 (#842).");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("contacts").GetArrayLength().ShouldBe(0,
            "retention cleared the contacts, and ListFrom(null) projects to [] — never null on the wire.");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // 3. LIST wire-form pin — the R2/K1 bulk-harvest refusal (one level below the L4 type lock)
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_job_ads_list_never_serialises_a_contact_surface_even_when_the_ad_has_contacts()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var orgNr = NewOrgNr();
        Guid adId;
        var (db, scope) = NewScope();
        using (scope)
        {
            // Declared contact only, clean body — so the ad PROVABLY holds contacts in the DB while
            // its title/description/url carry no "contact" substring for the raw scan below.
            var ad = await SeedImportedAdAsync(
                db, ClockAt(T0), "Lista backend-utvecklare", orgNr, bodyEmail: null, ct);
            adId = ad.Id.Value;

            ad.Contacts.ShouldNotBeNull();
            ad.Contacts!.IsEmpty.ShouldBeFalse(
                "counterfactual: this Active ad holds a contact in the DB — so a contact-free list "
                + "wire is a structural refusal, not an empty corpus.");
        }

        // Filter to exactly this ad by its unique org.nr so the returned page is non-vacuous.
        var response = await _client.GetAsync($"/api/v1/job-ads?employer={orgNr}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync(ct);

        // The ad IS on this page (proves the pin is not vacuous)...
        raw.ShouldContain(adId.ToString(),
            customMessage: "the seeded ad (which holds contacts in the DB) must be on the returned page.");

        // ...yet the raw wire carries NO contact surface ANYWHERE — not a key, not a value. The list
        // DTO (JobAdDto) is structurally contact-incapable (FTS lock L4); this pins the bytes.
        raw.IndexOf("contact", StringComparison.OrdinalIgnoreCase).ShouldBe(-1,
            "the /job-ads list wire must never carry a contact surface — ~37k recruiters' structured "
            + "contacts pre-parsed on the search wire is the exact bulk-harvest hazard the split ends.");

        // ...and structurally: no item object carries a contact-ish property name.
        using var doc = JsonDocument.Parse(raw);
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            foreach (var prop in item.EnumerateObject())
            {
                prop.Name.Contains("contact", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                    $"list item property '{prop.Name}' must not be a contact surface (FTS lock L4).");
            }
        }
    }
}
