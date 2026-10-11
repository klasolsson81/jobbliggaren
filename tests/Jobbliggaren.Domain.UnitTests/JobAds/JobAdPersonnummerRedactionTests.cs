using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobAds.Events;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// #2098 (ADR 0106 D5 amendment) — an imported ad never holds a personnummer in its Title or
/// Description. A sole trader's org.nr written into the ad text IS the holder's personnummer, and
/// <see cref="Privacy.RecruiterContactRedactor"/> detects only emails and phones. Exercised through
/// the real factories (Import / UpdateFromSource), never by hand-seeding (§5 <c>Tests:</c>).
/// All vectors are synthetic Luhn-valid test numbers (parity PersonnummerRedactorTests).
/// </summary>
public class JobAdPersonnummerRedactionTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    // 811218-9876 is a Luhn-valid personnummer; 811218-9877 is the same date with a broken check digit.
    private const string Personnummer = "811218-9876";
    private const string PersonnummerLong = "19811218-9876";
    private const string LuhnInvalid = "811218-9877";
    private const string UnmaskedDigits = "9876";

    private static JobAd Import(string title, string description, string companyName = "Enskild Firma") =>
        JobAd.Import(
            title: title,
            company: Company.Create(companyName).Value,
            description: description,
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: """{"id":"ext-1"}""",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock, extractTerms: TestKeywordExtraction.None).Value;

    private static void Resync(JobAd ad, string title, string description) =>
        ad.UpdateFromSource(
            title: title,
            description: description,
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            rawPayload: """{"id":"ext-1"}""",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Clock.UtcNow,
            expiresAt: null, extractTerms: TestKeywordExtraction.None).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Import_masks_a_personnummer_in_title_and_description_in_both_spellings()
    {
        var ad = Import(
            title: $"Städhjälp sökes, {Personnummer}",
            description: $"Enskild firma {PersonnummerLong} söker städare. Fakturera {Personnummer}.");

        ad.Title.ShouldNotContain(UnmaskedDigits);
        ad.Description.ShouldNotContain(UnmaskedDigits);
        ad.Title.ShouldContain("Städhjälp sökes");
        ad.Description.ShouldContain("söker städare");
    }

    [Fact]
    public void A_Luhn_invalid_digit_run_is_left_alone()
    {
        // Only REAL personnummer are touched — never an arbitrary digit run (an id, a reference).
        var ad = Import(title: "Referens " + LuhnInvalid, description: "Ärende " + LuhnInvalid);

        ad.Title.ShouldContain(LuhnInvalid);
        ad.Description.ShouldContain(LuhnInvalid);
    }

    [Fact]
    public void A_resync_carrying_a_personnummer_is_masked_too()
    {
        // The nightly snapshot and the 10-minute stream re-send the SOURCE text through
        // UpdateFromSource — the placement is what keeps the invariant durable.
        var ad = Import(title: "Städare", description: "Neutral text.");

        Resync(ad, title: $"Städare {Personnummer}", description: $"Ring firman {PersonnummerLong}.");

        ad.Title.ShouldNotContain(UnmaskedDigits);
        ad.Description.ShouldNotContain(UnmaskedDigits);
    }

    [Fact]
    public void Re_ingesting_the_same_source_is_a_fixed_point()
    {
        var title = $"Städare {Personnummer}";
        var description = $"Firma {PersonnummerLong}.";
        var ad = Import(title, description);
        var (titleAfterFirst, descriptionAfterFirst) = (ad.Title, ad.Description);

        Resync(ad, title, description);

        ad.Title.ShouldBe(titleAfterFirst);
        ad.Description.ShouldBe(descriptionAfterFirst);
    }

    [Fact]
    public void An_archived_ad_still_gets_a_personnummer_masked_on_resync()
    {
        var ad = Import(title: "Städare", description: "Neutral text.");
        ad.Archive(Clock).IsSuccess.ShouldBeTrue();

        Resync(ad, title: "Städare", description: $"Firma {Personnummer}.");

        ad.Description.ShouldNotContain(UnmaskedDigits);
    }

    [Fact]
    public void A_personnummer_the_contact_marker_pushes_across_the_title_clamp_leaves_no_digits()
    {
        // The phone near the start (13 chars) becomes the 62-char marker, so the personnummer at
        // index 242 moves to 291 and straddles position 300. Masking runs BEFORE the clamp, so the
        // cut only removes asterisks. Clamped first, "811218-98" would survive: the scanner needs
        // four trailing digits, so the truncated remainder is no longer detected.
        var title = "Ring 070-123 45 67 " + new string('x', 222) + " " + Personnummer + " " + new string('x', 46);
        title.Length.ShouldBe(300);

        var ad = Import(title, "Neutral text.");

        ad.Title.Length.ShouldBe(300);
        ad.Title.ShouldNotContain("811218");
        ad.Title.ShouldNotContain(UnmaskedDigits);
    }

    [Fact]
    public void A_zero_leading_personnummer_is_masked_and_never_promoted_into_contacts_as_a_phone()
    {
        // 050505-1235 (born 2005) is Luhn-valid AND phone-shaped to RecruiterContactRedactor.
        var ad = Import(title: "Städare", description: "Enskild firma 050505-1235 söker städare.");

        ad.Description.ShouldNotContain("050505");
        ad.Description.ShouldNotContain("1235");
        (ad.Contacts is null || ad.Contacts.IsEmpty).ShouldBeTrue(
            "a personnummer is removed, never moved into contacts under a recruiter-phone label.");
    }

    [Fact]
    public void A_contiguous_phone_number_that_is_also_a_valid_personnummer_is_masked_not_promoted()
    {
        // The two are indistinguishable: 0701234569 is a valid date plus a passing check digit. The
        // fail-safe direction is chosen on purpose — the number is removed and no contact is made.
        var ad = Import(title: "Städare", description: "Ring 0701234569 för frågor.");

        ad.Description.ShouldNotContain("0701234569");
        ad.Description.Any(char.IsDigit).ShouldBeFalse();
        (ad.Contacts is null || ad.Contacts.IsEmpty).ShouldBeTrue();
    }

    [Theory]
    [InlineData("8112189876")]
    [InlineData("811218 9876")]
    [InlineData("811218+9876")]
    [InlineData("198112189876")]
    [InlineData("811278-9873")] // samordningsnummer: day + 60
    public void Every_supported_spelling_is_masked_whole_and_the_backfill_probe_then_finds_nothing(string spelling)
    {
        var ad = Import(title: "Städare", description: $"Firma {spelling} söker städare.");

        ad.Description.Any(char.IsDigit).ShouldBeFalse(
            "no digit of the number, date part or check part, may survive the mask.");
        ad.ApplyContactScrubBackfill().Value.ShouldBeFalse(
            "the backfill probe and the write path use one detector — masked text is a fixed point.");
    }

    // ===============================================================
    // #2123 (ADR 0134 amendment 2026-10-11) — the separator forms an employer types into an ad,
    // exercised through the real factories. Platsbanken's nightly snapshot and the 10-minute
    // stream deliver the text; Import / UpdateFromSource is the only writer of it.
    // ===============================================================

    [Theory]
    [InlineData("811218 - 9876")]
    [InlineData("811218 \u2013 9876")]
    [InlineData("19811218 - 9876")]
    [InlineData("811278 - 9873")]
    [InlineData("811218.9876")]
    [InlineData("811218/9876")]
    [InlineData("811218_9876")]
    [InlineData("811218 / 9876")]
    public void The_separator_forms_of_2123_are_masked_in_title_and_description_on_import_and_on_resync(
        string spelling)
    {
        var imported = Import(title: $"Städare {spelling}", description: $"Firma {spelling} söker städare.");

        imported.Title.Any(char.IsDigit).ShouldBeFalse();
        imported.Description.Any(char.IsDigit).ShouldBeFalse();
        imported.Title.ShouldContain("Städare");
        imported.Description.ShouldContain("söker städare");

        var resynced = Import(title: "Städare", description: "Neutral text.");
        Resync(resynced, title: $"Städare {spelling}", description: $"Firma {spelling} söker städare.");

        resynced.Title.Any(char.IsDigit).ShouldBeFalse();
        resynced.Description.Any(char.IsDigit).ShouldBeFalse();
        resynced.ApplyContactScrubBackfill().Value.ShouldBeFalse(
            "the backfill probe and the write path use one detector — masked text is a fixed point.");
    }

    [Theory]
    [InlineData("050505 - 1235")]
    [InlineData("050505\t-\t1235")]
    [InlineData("050505\u00A0-\u00A01235")]
    [InlineData("050505 -\u00A01235")]
    [InlineData("050505 \u2013 1235")]
    [InlineData("050505.1235")]
    [InlineData("050505/1235")]
    [InlineData("050505_1235")]
    public void A_zero_leading_personnummer_in_a_2123_form_is_masked_and_never_promoted_into_contacts(
        string spelling)
    {
        // The arm the issue names: before #2123 a spaced form starting with 0 was not matched by the
        // scanner, so the phone detector (anchored on a leading 0, separators space/tab/NBSP/hyphen)
        // took it, the body got the marker, and the number was PROMOTED into Contacts. Both arms are
        // asserted, for the two visible surfaces and for both writers: the text AND the contact list.
        foreach (var viaResync in new[] { false, true })
        {
            var title = $"Städare {spelling}";
            var description = $"Enskild firma {spelling} söker städare.";
            JobAd ad;
            if (viaResync)
            {
                ad = Import(title: "Städare", description: "Neutral text.");
                Resync(ad, title, description);
            }
            else
            {
                ad = Import(title, description);
            }

            ad.Title.Any(char.IsDigit).ShouldBeFalse($"title, viaResync={viaResync}");
            ad.Description.Any(char.IsDigit).ShouldBeFalse($"description, viaResync={viaResync}");
            (ad.Contacts is null || ad.Contacts.IsEmpty).ShouldBeTrue(
                $"a personnummer is removed, never moved into contacts under a recruiter-phone label (viaResync={viaResync}).");
        }
    }

    [Theory]
    [InlineData("811218  - 9876")] // three visible columns: ADR 0134 D1/R1, the declared residual
    [InlineData("811218 -\n9876")] // a line break between the groups: deliberately not bridged
    public void The_declared_residuals_are_left_alone_rather_than_half_masked(string spelling)
    {
        var ad = Import(title: "Städare", description: $"Firma {spelling} söker städare.");

        ad.Description.ShouldContain(spelling);
    }

    [Theory]
    [InlineData("Anna Svensson 811218-9876", "Anna Svensson ******-****")]
    [InlineData("Svensson 811218 - 9876 Städ", "Svensson ****** - **** Städ")]
    [InlineData("Svensson 19811218.9876", "Svensson ********.****")]
    public void A_personnummer_in_the_company_name_is_masked_in_place_on_import(string name, string expected)
    {
        // The name is printed on the public job page, and an enskild firma's name IS a person's name,
        // so a number typed into it is that person's personnummer. Length-preserving, so the
        // Company.Create bounds (non-blank, <= 200) cannot be crossed by the mask.
        var ad = Import(title: "Städare", description: "Neutral text.", companyName: name);

        ad.Company.Name.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Enskild Firma")]
    [InlineData("Kontor 24 AB")] // digits that are not a personnummer
    [InlineData("Städ 556677-0001 AB")] // an organisation number: month 66 fails the date gate
    [InlineData("Byggfirman 811218-9877")] // Luhn-invalid
    public void A_company_name_without_a_personnummer_is_left_exactly_as_it_was(string name)
    {
        var ad = Import(title: "Städare", description: "Neutral text.", companyName: name);

        ad.Company.Name.ShouldBe(name);
    }

    [Fact]
    public void A_masked_company_name_is_a_fixed_point_across_the_nightly_resync()
    {
        var ad = Import(title: "Städare", description: "Neutral text.", companyName: "Svensson 811218-9876");
        var masked = ad.Company.Name;

        Resync(ad, title: "Städare", description: "Neutral text.");

        ad.Company.Name.ShouldBe(masked);
        masked.Any(char.IsDigit).ShouldBeFalse();
    }

    [Fact]
    public void The_company_name_of_a_manually_created_ad_is_never_rewritten()
    {
        // CLAUDE.md §5: a rule engine never rewrites user-typed text silently. Same scope as the title.
        var manual = JobAd.Create(
            title: "Eget spår",
            company: Company.Create($"Min firma {Personnummer}").Value,
            description: "Min egen anteckning.",
            url: "https://example.com/jobb",
            source: JobSource.Manual,
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock).Value;

        manual.Company.Name.ShouldContain(Personnummer);
    }

    [Fact]
    public void The_imported_event_carries_the_masked_title()
    {
        var ad = Import(title: $"Städare {Personnummer}", description: "Neutral text.");

        var imported = ad.DomainEvents.OfType<JobAdImportedDomainEvent>().ShouldHaveSingleItem();
        imported.Title.ShouldBe(ad.Title);
        imported.Title.ShouldNotContain(UnmaskedDigits);
    }

    [Fact]
    public void The_manual_path_is_never_scrubbed()
    {
        // CLAUDE.md §5: a rule engine never rewrites user-typed text silently.
        var manual = JobAd.Create(
            title: "Eget spår",
            company: Company.Create("Acme AB").Value,
            description: $"Min egen anteckning {Personnummer}.",
            url: "https://example.com/jobb",
            source: JobSource.Manual,
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock).Value;

        manual.Description.ShouldContain(Personnummer);
    }
}
