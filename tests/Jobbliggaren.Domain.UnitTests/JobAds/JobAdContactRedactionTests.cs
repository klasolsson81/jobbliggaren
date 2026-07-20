using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// THE Tier-A aggregate invariant (#842, ADR 0106 D4, re-bind R1): an imported ad never holds a
/// detected recruiter email/phone in Title/Description/RawPayload — every detected span is
/// scrubbed, and the promote step surfaces email/phone from the VISIBLE surfaces plus email-only
/// from the payload into <see cref="JobAd.Contacts"/> (while Active; the asymmetric promote gate,
/// ADR 0106 amendment 2026-07-17). The body keeps only the marker.
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
            clock: Clock, extractTerms: TestKeywordExtraction.None).Value;

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
            expiresAt: null, extractTerms: TestKeywordExtraction.None).IsSuccess.ShouldBeTrue();

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
            expiresAt: null, extractTerms: TestKeywordExtraction.None).IsSuccess.ShouldBeTrue();

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

    // ---- the asymmetric promote gate (ADR 0106 amendment 2026-07-17, CTO Verdict 3) ---------
    //
    // Scrub for safety, promote for truth: the RawPayload surface is scrubbed recall-biased
    // (unchanged), but a PHONE span found there is never promoted — a quoted id/reference
    // number starting 0 + 6-12 digits is phone-shaped to the detector, and promoting it
    // invents a user-visible "derived contact" that never existed (a precision failure the
    // over-redaction posture never priced). Emails cannot be id-shaped by accident, so the
    // payload's email spans keep promoting. Title/Description are the ad's VISIBLE text — a
    // phone there still promotes, which is why the gate costs almost nothing: those surfaces
    // are payload-derived, so a real recruiter phone reaches the user through them.

    [Fact]
    public void A_phone_shaped_payload_id_is_scrubbed_but_never_promoted()
    {
        // The counterfactual pair in one witness: detection-YES (the id is scrubbed — the
        // recall posture is intact and the oracle is alive) + promotion-NO (no phantom).
        var ad = Import(
            description: "Vi söker en utvecklare.",
            title: "Backend-utvecklare",
            rawPayload: """{"id":"0123456789","description":{"text":"Vi söker en utvecklare."}}""");

        ad.RawPayload!.ShouldNotContain("0123456789",
            customMessage: "detection must still fire — the scrub is recall-biased and unchanged");
        ad.RawPayload!.ShouldContain(RecruiterContactRedactor.Marker);

        ad.Contacts.ShouldNotBeNull();
        ad.Contacts.Contacts.ShouldBeEmpty(
            "a phone-shaped payload id must never become a user-visible derived contact");
    }

    [Fact]
    public void A_phone_living_only_in_a_payload_field_is_scrubbed_and_not_promoted()
    {
        // The PRICED recall cost, pinned so it is a decision and not an accident: a real phone
        // in a payload-only field (never mirrored into Title/Description) is scrubbed for
        // safety but not surfaced as a contact — an inference from text the user cannot see
        // fails the "promote for truth" bar (CTO Verdict 3, V1).
        var ad = Import(
            description: "Vi söker en utvecklare.",
            title: "Backend-utvecklare",
            rawPayload: """{"id":"ext-1","application_details":{"via":"Ring 070-123 45 67"}}""");

        ad.RawPayload!.ShouldNotContain("070-123 45 67");
        ad.Contacts!.Contacts.ShouldBeEmpty();
    }

    [Fact]
    public void An_email_living_only_in_the_payload_still_promotes()
    {
        // The gate is PHONE-ONLY: an email cannot be id-shaped by accident (it needs an @ and
        // a domain), so the payload's email spans keep their promote path.
        var ad = Import(
            description: "Vi söker en utvecklare.",
            title: "Backend-utvecklare",
            rawPayload: """{"id":"ext-1","application_details":{"email":"jobb@acme.se"}}""");

        ad.RawPayload!.ShouldNotContain("jobb@acme.se");
        var only = ad.Contacts!.Contacts.ShouldHaveSingleItem();
        only.Origin.ShouldBe(AdContactOrigin.ExtractedFromBody);
        only.Email.ShouldBe("jobb@acme.se");
    }

    [Fact]
    public void A_phone_in_the_visible_title_still_promotes_exactly_once()
    {
        // The gate is scoped to the PAYLOAD surface: a phone in the ad's visible title promotes
        // as always (and its payload mirror dedups on the recogniser's normalized form). An
        // over-rotation of the filter onto title.Found would go red here, not silently green
        // via the description-based sibling specs (test-writer m2+m3).
        var ad = Import(
            description: "Vi söker en utvecklare.",
            title: "Ring 070-123 45 67 om jobbet",
            rawPayload: """{"id":"ext-1","title":"Ring 070-123 45 67 om jobbet"}""");

        ad.Title.ShouldNotContain("070-123 45 67");
        var only = ad.Contacts!.Contacts.ShouldHaveSingleItem();
        only.Origin.ShouldBe(AdContactOrigin.ExtractedFromBody);
        only.Phone.ShouldBe("070-123 45 67");
    }

    [Fact]
    public void A_declared_phone_survives_beside_a_phone_shaped_payload_id()
    {
        // The declared path is untouched by the gate: the advertiser's own declaration
        // promotes as always, and the phantom does not ride in beside it.
        var declared = AdContact.TryCreate(
            "Anna Karlsson", "Rekryterare", email: null, "070-123 45 67",
            AdContactOrigin.Declared)!;

        var ad = Import(
            description: "Vi söker en utvecklare.",
            title: "Backend-utvecklare",
            rawPayload: """{"id":"0123456789","description":{"text":"Vi söker en utvecklare."}}""",
            declaredContacts: [declared]);

        var only = ad.Contacts!.Contacts.ShouldHaveSingleItem();
        only.Origin.ShouldBe(AdContactOrigin.Declared);
        only.Phone.ShouldBe("070-123 45 67");
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
