using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// THE Tier-A aggregate invariant (#842, ADR 0106 D4, re-bind R1): an imported ad never holds a
/// detected recruiter email/phone in Title/Description/RawPayload — every detected span is
/// promoted into <see cref="JobAd.Contacts"/> (while Active) and the body keeps only the marker.
/// Exercised through the REAL factories (Import/UpdateFromSource), never by hand-seeding — the
/// V20/#843 rule.
/// </summary>
public class JobAdContactRedactionTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static JobAd Import(
        string description = "Kontakta anna@acme.se eller ring 070-123 45 67.",
        string title = "Backend-utvecklare",
        string rawPayload = """{"id":"ext-1","description":{"text":"Kontakta anna@acme.se eller ring 070-123 45 67."}}""",
        IReadOnlyList<AdContact>? declaredContacts = null) =>
        JobAd.Import(
            title: title,
            company: Company.Create("Acme AB").Value,
            description: description,
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: rawPayload,
            facets: TestFacets.None,
            declaredContacts: declaredContacts ?? [],
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock).Value;

    [Fact]
    public void Import_scrubs_body_and_payload_and_promotes_the_detected_contact()
    {
        var ad = Import();

        ad.Description.ShouldNotContain("anna@acme.se");
        ad.Description.ShouldNotContain("070-123 45 67");
        ad.Description.ShouldContain(RecruiterContactRedactor.Marker);
        ad.RawPayload!.ShouldNotContain("anna@acme.se");

        ad.Contacts.ShouldNotBeNull();
        ad.Contacts.Contacts.Count.ShouldBe(2);
        ad.Contacts.Contacts.ShouldAllBe(c => c.Origin == AdContactOrigin.ExtractedFromBody);
        ad.Contacts.Contacts.ShouldContain(c => c.Email == "anna@acme.se");
        ad.Contacts.Contacts.ShouldContain(c => c.Phone == "070-123 45 67");
    }

    [Fact]
    public void A_declared_contact_absorbs_its_own_body_hit()
    {
        var declared = AdContact.TryCreate(
            "Anna Karlsson", "Rekryterare", "anna@acme.se", "070-123 45 67",
            AdContactOrigin.Declared)!;

        var ad = Import(declaredContacts: [declared]);

        var only = ad.Contacts!.Contacts.ShouldHaveSingleItem();
        only.Origin.ShouldBe(AdContactOrigin.Declared);
        only.Name.ShouldBe("Anna Karlsson");
        ad.Description.ShouldNotContain("anna@acme.se"); // the body is still scrubbed
    }

    [Fact]
    public void The_imported_event_carries_the_scrubbed_title()
    {
        var ad = Import(title: "Ring 070-123 45 67 om jobbet");

        ad.Title.ShouldNotContain("070-123 45 67");
        var imported = ad.DomainEvents.OfType<Domain.JobAds.Events.JobAdImportedDomainEvent>()
            .ShouldHaveSingleItem();
        imported.Title.ShouldBe(ad.Title); // never the pre-scrub text into a logged event
    }

    [Fact]
    public void A_title_whose_marker_overflows_the_column_is_clamped_not_crashed()
    {
        // ValidateCore caps the title at 300 BEFORE the marker lands; without the clamp the save
        // dies on varchar(300) long after validation said yes.
        var title = new string('x', 290) + " anna@a.se";

        var ad = Import(title: title);

        ad.Title.Length.ShouldBeLessThanOrEqualTo(300);
        ad.Title.ShouldNotContain("anna@a.se");
    }

    [Fact]
    public void Re_ingesting_the_same_source_is_a_fixed_point()
    {
        var ad = Import();
        var descriptionAfterFirst = ad.Description;
        var contactsAfterFirst = ad.Contacts;

        // The nightly sync re-sends the SOURCE text (the funnel refetches from JobTech, not from
        // our scrubbed copy), so the invariant must converge to the same value.
        ad.UpdateFromSource(
            title: "Backend-utvecklare",
            description: "Kontakta anna@acme.se eller ring 070-123 45 67.",
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            rawPayload: """{"id":"ext-1","description":{"text":"Kontakta anna@acme.se eller ring 070-123 45 67."}}""",
            facets: TestFacets.None,
            declaredContacts: [],
            expiresAt: null).IsSuccess.ShouldBeTrue();

        ad.Description.ShouldBe(descriptionAfterFirst);
        ad.Contacts.ShouldBe(contactsAfterFirst);
    }

    [Fact]
    public void An_archived_ad_still_gets_its_body_scrubbed_but_never_repopulates_contacts()
    {
        // b1 §1's four-step failure scenario: ExpireJobAdsJob archives; JobTech still serves the
        // ad; the 02:00 snapshot re-ingests it through UpdateFromSource. The write-gate is what
        // makes retention durable BY PLACEMENT: without it, the funnel restores the contacts
        // retention just cleared, nightly, silently.
        var ad = Import();
        ad.Archive(Clock).IsSuccess.ShouldBeTrue();
        ad.Contacts.ShouldBeNull(); // Archive() cleared them (R4)

        ad.UpdateFromSource(
            title: "Backend-utvecklare",
            description: "Ny text: maila jobb@acme.se.",
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            rawPayload: """{"id":"ext-1"}""",
            facets: TestFacets.None,
            declaredContacts:
            [
                AdContact.TryCreate("Anna", null, "anna@acme.se", null, AdContactOrigin.Declared)!,
            ],
            expiresAt: null).IsSuccess.ShouldBeTrue();

        ad.Description.ShouldNotContain("jobb@acme.se"); // the body scrub runs in EVERY status
        ad.Contacts.ShouldBeNull(); // the Active-gate holds
    }

    [Fact]
    public void Erase_clears_the_contacts_on_a_still_active_ad()
    {
        var ad = Import();
        ad.Contacts.ShouldNotBeNull();

        ad.Erase(Clock).IsSuccess.ShouldBeTrue();

        ad.Contacts.ShouldBeNull();
    }

    [Fact]
    public void The_retention_backstop_refuses_a_live_ad_and_clears_an_archived_one()
    {
        var live = Import();
        live.ClearContactsRetentionBackstop().IsFailure.ShouldBeTrue();
        live.Contacts.ShouldNotBeNull(); // refused = untouched

        var archived = Import();
        archived.Archive(Clock).IsSuccess.ShouldBeTrue();
        archived.ClearContactsRetentionBackstop().IsSuccess.ShouldBeTrue();
        archived.Contacts.ShouldBeNull();
    }

    [Fact]
    public void The_manual_path_is_never_scrubbed()
    {
        // CLAUDE.md §5: a rule engine never rewrites user-typed text silently. The invariant is
        // scoped to IMPORTED ads; a manually tracked ad's text is the user's own record.
        var manual = JobAd.Create(
            title: "Eget spår",
            company: Company.Create("Acme AB").Value,
            description: "Jag mailade anna@acme.se om tjänsten.",
            url: "https://example.com/jobb",
            source: JobSource.Manual,
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock).Value;

        manual.Description.ShouldContain("anna@acme.se");
        manual.Contacts.ShouldBeNull();
    }
}
