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

    /// <summary>
    /// #893 (lexicon v6) — the shipped displayForms pinned against the loader's two invariants, so the
    /// fail-loud throws are never reached in production: every key is a KNOWN synonym (typed or free),
    /// and every value is a pure RE-CASING of its synonym (<c>NormalizeHeading(form) == key</c>), never
    /// a remap that would rewrite the user's word (synthesis, ADR 0108 §5 / CLAUDE.md §5).
    /// </summary>
    [Fact]
    public void DisplayForms_KeyKnownSynonyms_AndValuesRecaseNeverRemap()
    {
        var file = CvParsingLexiconFixture.ReadFile();

        var displayForms = file.DisplayForms.ShouldNotBeNull(
            "displayForms saknas i lexikonet — v6 ska bära minst it-kompetenser.");
        displayForms.ShouldNotBeEmpty("displayForms är tomt — testet skulle passera vakuöst.");

        // The v6 override that exists to exist — the acronym NormalizeCase could only degrade. An
        // explicit pin so an accidental deletion goes red here, not silently (parity V3Vocabulary).
        displayForms.ShouldContainKeyAndValue("it-kompetenser", "IT-kompetenser");

        var knownSynonyms = (file.Headings ?? []).Values.SelectMany(v => v).Select(Normalize)
            .Concat(RawFreeSynonyms().Select(Normalize))
            .ToHashSet();
        knownSynonyms.ShouldNotBeEmpty();

        foreach (var (rawKey, form) in displayForms)
        {
            var key = Normalize(rawKey);

            knownSynonyms.ShouldContain(key,
                $"displayForm-nyckeln '{rawKey}' är ingen känd synonym — en dinglande form kan aldrig föreslås.");

            Normalize(form).ShouldBe(key,
                $"displayForm '{form}' för '{key}' är en REMAP, inte en om-versalisering " +
                $"(Normalize('{form}')='{Normalize(form)}'). En display-form återversaliserar; den byter aldrig ord.");
        }
    }

    /// <summary>
    /// #893 — display-form KEYS must be stored normalized (parity <see cref="FreeSectionSynonyms_AreStoredNormalized"/>).
    /// An unnormalized key ("IT-KOMPETENSER") would resolve at load via <c>NormalizeHeading</c> but read
    /// as a raw un-normalized authoring mistake here — and would only differ from the loader's key by the
    /// very normalisation the lookup relies on, which is exactly the drift the shipped-data pin exists to catch.
    /// </summary>
    [Fact]
    public void DisplayFormKeys_AreStoredNormalized()
    {
        var displayForms = CvParsingLexiconFixture.ReadFile().DisplayForms.ShouldNotBeNull();
        displayForms.ShouldNotBeEmpty("displayForms är tomt — testet skulle passera vakuöst.");

        var unnormalized = displayForms.Keys.Where(key => key != Normalize(key)).ToList();

        unnormalized.ShouldBeEmpty(
            $"Dessa displayForm-nycklar lagras onormaliserade: {string.Join(", ", unnormalized)}. " +
            "En onormaliserad nyckel matchar aldrig en CV-rubrik via lexikonets normaliserare — tyst.");
    }
}
