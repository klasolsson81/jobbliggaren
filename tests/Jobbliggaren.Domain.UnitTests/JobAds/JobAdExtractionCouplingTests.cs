using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// #874 — the ingest transitions (<see cref="JobAd.Import"/> / <see cref="JobAd.UpdateFromSource"/>)
/// fold the deterministic keyword/skill extraction in as a REQUIRED
/// <see cref="JobAdTermExtraction"/> delegate, invoked over the POST-SCRUB Title/Description inside
/// <see cref="JobAd.SetSourcePayload"/>. A text write that forgets to refresh
/// <see cref="JobAd.ExtractedTerms"/> is therefore not expressible — the #841 guarantee applied to the
/// one derived value that can only be computed AFTER the aggregate scrubs its own text (F-B, #842 Tier
/// A). Exercised through the REAL factories (the V20/#843 rule), never by hand-seeding.
/// </summary>
public class JobAdExtractionCouplingTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private const string Url = "https://arbetsformedlingen.se/platsbanken/annonser/1";
    private const string Payload = """{"id":"ext-1"}""";

    private static ExtractedTerms OneKeyword(string lexeme) =>
        ExtractedTerms.From([new ExtractedTerm(
            Lexeme: lexeme, Display: lexeme, Kind: ExtractedTermKind.Keyword,
            Source: ExtractedTermSource.Description, MatchedOn: lexeme, ConceptId: null, Weight: 1)]);

    private static JobAd Import(string description, JobAdTermExtraction extractTerms) =>
        JobAd.Import(
            title: "Utvecklare",
            company: Company.Create("Acme AB").Value,
            description: description,
            url: Url,
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: Payload,
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock, extractTerms: extractTerms).Value;

    [Fact]
    public void Import_extracts_terms_from_the_post_scrub_description()
    {
        string? seenTitle = null;
        string? seen = null;
        var ad = Import(
            "C# och kontakta anna@acme.se",
            extractTerms: (title, description) => { seenTitle = title; seen = description; return OneKeyword("csharp"); });

        // The delegate ran over the POST-SCRUB text — the recruiter email is already gone (the
        // extraction never sees a contact span; #842 Tier A F-B). Ordering: ApplyContactRedaction
        // scrubs, THEN the fold-in extracts.
        seen.ShouldNotBeNull();
        seen!.ShouldNotContain("anna@acme.se");
        seen.ShouldContain(RecruiterContactRedactor.Marker);
        // ...and over the aggregate's OWN post-scrub/trimmed Title, not raw caller input.
        seenTitle.ShouldBe(ad.Title);

        // ...and its output is the ad's ExtractedTerms — folded in atomically, no separate call.
        ad.ExtractedTerms.ShouldNotBeNull();
        ad.ExtractedTerms!.Terms.ShouldHaveSingleItem().Lexeme.ShouldBe("csharp");
    }

    [Fact]
    public void UpdateFromSource_refreshes_the_terms_from_the_new_post_scrub_text()
    {
        // An imported ad whose terms were extracted from the ORIGINAL text.
        var ad = Import("gammal text", extractTerms: TestKeywordExtraction.Returning(OneKeyword("gammal")));
        ad.ExtractedTerms.ShouldNotBeNull();
        ad.ExtractedTerms!.Terms.ShouldHaveSingleItem().Lexeme.ShouldBe("gammal");

        // The nightly sync rewrites the text (JobTech is refetched, not our scrubbed copy). The
        // delegate derives terms from the NEW post-scrub text.
        string? seen = null;
        ad.UpdateFromSource(
            title: "Utvecklare",
            description: "ny text, ring 070-123 45 67",
            url: Url,
            rawPayload: Payload,
            facets: TestFacets.None,
            declaredContacts: [],
            expiresAt: null,
            extractTerms: (_, description) => { seen = description; return OneKeyword("ny"); })
            .IsSuccess.ShouldBeTrue();

        // MUTATION TARGET (#874 acceptance criterion 2): the terms are NOT stale — they track the
        // updated text. Removing the fold-in (`ExtractedTerms = extractTerms(...)`) from the shared
        // SetSourcePayload reddens this test — the terms are never refreshed (in fact, without the
        // fold-in the initial Import above would not set them either).
        ad.ExtractedTerms.ShouldNotBeNull();
        ad.ExtractedTerms!.Terms.ShouldHaveSingleItem().Lexeme.ShouldBe("ny");

        // Extraction still ran over POST-SCRUB text (the ordering the fold-in preserves).
        seen.ShouldNotBeNull();
        seen!.ShouldNotContain("070-123 45 67");
    }

    [Fact]
    public void Import_requires_an_extraction_delegate()
    {
        Should.Throw<ArgumentNullException>(() => JobAd.Import(
            title: "Utvecklare",
            company: Company.Create("Acme AB").Value,
            description: "text",
            url: Url,
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: Payload,
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock, extractTerms: null!));
    }

    [Fact]
    public void UpdateFromSource_requires_an_extraction_delegate()
    {
        var ad = Import("text", extractTerms: (_, _) => ExtractedTerms.Empty);

        Should.Throw<ArgumentNullException>(() => ad.UpdateFromSource(
            title: "Utvecklare",
            description: "ny text",
            url: Url,
            rawPayload: Payload,
            facets: TestFacets.None,
            declaredContacts: [],
            expiresAt: null,
            extractTerms: null!));
    }
}
