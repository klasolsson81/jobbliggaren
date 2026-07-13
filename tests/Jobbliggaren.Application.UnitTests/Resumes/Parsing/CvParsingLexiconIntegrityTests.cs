using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #815 / 8b.4a — invariants of the SHIPPED lexicon data. The free-section vocabulary must stay
/// DISJOINT from the typed headings and from the CV-title name banners: a token in both maps would
/// make a typed section (Erfarenhet) resolve as a free one, or turn "Meritförteckning" from a banner
/// into a section, silently changing how every CV is segmented.
///
/// <para><b>These tests use the production normalizer</b> (<c>CvParsingLexiconLoader.NormalizeHeading</c>),
/// not a local copy. The v3 suite carried its own — subtly WEAKER — normalizer (no internal-whitespace
/// collapse), and it was the copy guarding the data. Two consequences, both real: the
/// "stored normalised" assertion pre-normalised its own input and so could not fail, and the
/// disjointness guard normalised typed tokens more weakly than production does, so a typed
/// <c>"x  y"</c> and a free <c>"x y"</c> would have COLLIDED in production while the test saw no
/// overlap. Guarding data with a different function than the one that consumes it is not a test.
/// (dotnet-architect Major 5 / code-reviewer Major 1.)</para>
/// </summary>
public class CvParsingLexiconIntegrityTests
{
    private static string Normalize(string value) => CvParsingLexiconLoader.NormalizeHeading(value);

    /// <summary>Every free-section synonym, flattened across all sectionIds, AS AUTHORED (raw).</summary>
    private static List<string> RawFreeSynonyms() =>
        (CvParsingLexiconFixture.ReadFile().FreeSections ?? [])
            .SelectMany(pair => pair.Value)
            .ToList();

    [Fact]
    public void FreeSectionSynonyms_AreDisjointFromTheTypedHeadings()
    {
        var file = CvParsingLexiconFixture.ReadFile();

        var typed = (file.Headings ?? []).Values.SelectMany(v => v).Select(Normalize).ToHashSet();
        var free = RawFreeSynonyms().Select(Normalize).ToList();

        // Vacuity guards: a renamed or absent JSON key would leave either side empty and make the
        // overlap assertion below pass without ever being able to fail. (The v3 suite would have
        // gone green on exactly the rename this PR makes.)
        typed.ShouldNotBeEmpty("headings är tomt — testet skulle passera utan att kunna falla.");
        free.ShouldNotBeEmpty("freeSections är tomt — testet skulle passera utan att kunna falla.");

        var overlap = free.Where(typed.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE typade och fria rubriker: {string.Join(", ", overlap)}. " +
            "En sådan krock ändrar tyst hur varje CV segmenteras.");
    }

    [Fact]
    public void FreeSectionSynonyms_AreDisjointFromTheNameBanners()
    {
        var file = CvParsingLexiconFixture.ReadFile();

        var banners = (file.NameBanners ?? []).Select(Normalize).ToHashSet();
        var free = RawFreeSynonyms().Select(Normalize).ToList();

        banners.ShouldNotBeEmpty("nameBanners är tomt — testet skulle passera utan att kunna falla.");
        free.ShouldNotBeEmpty("freeSections är tomt — testet skulle passera utan att kunna falla.");

        var overlap = free.Where(banners.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE namn-banners och fria rubriker: {string.Join(", ", overlap)}.");
    }

    /// <summary>
    /// The synonyms must be stored in the form the normalizer produces. Asserted on the RAW authored
    /// tokens — the v3 suite normalised its input first, which reduced the assertion to "Normalize is
    /// idempotent": a property of the function, not of the data. It could not fail. (Verified: with
    /// the old shape, changing a shipped token to "CERTIFIKAT" left the suite green.)
    /// </summary>
    [Fact]
    public void FreeSectionSynonyms_AreStoredNormalized()
    {
        var raw = RawFreeSynonyms();

        raw.ShouldNotBeEmpty();

        var unnormalized = raw.Where(token => token != Normalize(token)).ToList();

        unnormalized.ShouldBeEmpty(
            $"Dessa synonymer lagras onormaliserade: {string.Join(", ", unnormalized)}. " +
            "En onormaliserad token matchar aldrig en CV-rubrik — tyst.");

        raw.Distinct().Count().ShouldBe(raw.Count, "Dubbletter i freeSections synonymer.");
    }

    /// <summary>
    /// The TYPED variants are now loaded through the same normalizer as everything else. They were
    /// previously loaded with a bare <c>ToLowerInvariant()</c> — so a typed variant added with a
    /// trailing colon or a double space would have been DEAD on arrival, in the very map that decides
    /// what "Erfarenhet" means. This asserts the shipped typed data is ALREADY normalised, which is
    /// what makes that loader change a behavioural no-op today and a guard tomorrow.
    /// </summary>
    [Fact]
    public void TypedHeadingVariants_AreStoredNormalized()
    {
        var typed = (CvParsingLexiconFixture.ReadFile().Headings ?? []).Values
            .SelectMany(v => v)
            .ToList();

        typed.ShouldNotBeEmpty();

        var unnormalized = typed.Where(token => token != Normalize(token)).ToList();

        unnormalized.ShouldBeEmpty(
            $"Dessa typade varianter lagras onormaliserade: {string.Join(", ", unnormalized)}.");
    }

    /// <summary>
    /// A synonym claimed by TWO sectionIds is not a last-one-wins — it would make "which section is
    /// this?" depend on JSON key order, and the recommendation side would suppress a suggestion for
    /// one id while the CV was recognised as the other. The loader throws on it; this pins the
    /// SHIPPED data so the throw is never reached in production.
    /// </summary>
    [Fact]
    public void NoFreeSectionSynonym_IsClaimedByTwoSectionIds()
    {
        var freeSections = CvParsingLexiconFixture.ReadFile().FreeSections.ShouldNotBeNull();

        freeSections.ShouldNotBeEmpty("freeSections är tomt — testet skulle passera vakuöst.");

        var claims = freeSections
            .SelectMany(pair => pair.Value.Select(synonym => (Synonym: Normalize(synonym), pair.Key)))
            .GroupBy(x => x.Synonym)
            .Where(g => g.Select(x => x.Key).Distinct().Count() > 1)
            .Select(g => $"{g.Key} → [{string.Join(", ", g.Select(x => x.Key).Distinct())}]")
            .ToList();

        claims.ShouldBeEmpty(
            $"Dessa synonymer gör anspråk på två sectionIds: {string.Join("; ", claims)}. " +
            "Rubrikens sektion skulle avgöras av JSON-nyckelordning.");
    }

    /// <summary>
    /// The sectionIds are a CONTRACT: a recommendation asset (SSYK→branschgrupp) references them by
    /// name. Keep them stable, lowercase ASCII slugs — an id carrying "ä"/"ö" invites the encoding
    /// drift that silently breaks a lookup across a JSON/C#/TS boundary. (The lexicon itself carries
    /// BOTH "utmärkelser" and "utmarkelser" as synonyms precisely because that drift is real.)
    /// </summary>
    [Fact]
    public void SectionIds_AreStableLowercaseAsciiSlugs()
    {
        var ids = CvParsingLexiconFixture.ReadFile().FreeSections.ShouldNotBeNull().Keys.ToList();

        ids.ShouldNotBeEmpty();

        var offenders = ids.Where(id => !IsLowercaseAsciiSlug(id)).ToList();

        offenders.ShouldBeEmpty(
            $"Dessa sectionIds är inte stabila gemena ASCII-slugs: {string.Join(", ", offenders)}.");
    }

    private static bool IsLowercaseAsciiSlug(string id) =>
        id.Length > 0 && id.All(c => c is >= 'a' and <= 'z');
}
