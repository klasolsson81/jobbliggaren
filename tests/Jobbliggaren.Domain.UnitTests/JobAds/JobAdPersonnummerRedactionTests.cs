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

    private static JobAd Import(string title, string description) =>
        JobAd.Import(
            title: title,
            company: Company.Create("Enskild Firma").Value,
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
    public void A_title_at_the_column_limit_stays_within_it()
    {
        // Masking is length-preserving, so the varchar(300) clamp is unaffected.
        var title = new string('x', 280) + " " + Personnummer;

        var ad = Import(title, "Neutral text.");

        ad.Title.Length.ShouldBe(title.Length);
        ad.Title.ShouldNotContain(UnmaskedDigits);
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
