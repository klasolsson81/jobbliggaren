using System.Text.Json;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// Fas 4b 8b.4a — <c>ICvParsingLexicon</c>, the port that lets a section-RECOMMENDATION asset ask
/// the RECOGNITION lexicon "which canonical section is this heading?" without forking its synonyms.
///
/// <para>The load-bearing test in this file is
/// <see cref="EveryFreeSectionSynonym_StillTerminatesThePrecedingSection"/>. The v4 reshape
/// (flat token array → sectionId map) touched the data structure every CV is segmented against, and
/// the ONLY thing that makes that reshape safe is that the flattened synonym union is unchanged.
/// Asserting that against the JSON would be circular (it would compare the file to itself); so it is
/// asserted against BEHAVIOUR — every one of the shipped synonyms is driven through the real
/// segmenter and must still terminate the preceding section, which is the #815 contract.</para>
/// </summary>
public class CvParsingLexiconProviderTests
{
    private readonly CvParsingLexiconProvider _sut = new();
    private readonly HeadingDrivenResumeSegmenter _segmenter = new();

    private sealed record Lexicon(Dictionary<string, string[]>? FreeSections);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>The shipped synonym → sectionId pairs, read from the embedded asset (the real
    /// vocabulary — a copy here would be the very fork this port exists to prevent).</summary>
    private static List<(string Synonym, string SectionId)> ShippedPairs()
    {
        var assembly = typeof(Jobbliggaren.Infrastructure.DependencyInjection).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json")
            ?? throw new InvalidOperationException("Det inbäddade parsing-lexikonet saknas.");

        var lexicon = JsonSerializer.Deserialize<Lexicon>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Lexikonet deserialiserade till null.");

        var freeSections = lexicon.FreeSections
            ?? throw new InvalidOperationException("freeSections saknas i lexikonet.");

        return freeSections
            .SelectMany(pair => pair.Value.Select(synonym => (Synonym: synonym, SectionId: pair.Key)))
            .ToList();
    }

    public static TheoryData<string, string> ShippedSynonyms()
    {
        var data = new TheoryData<string, string>();
        foreach (var (synonym, sectionId) in ShippedPairs())
            data.Add(synonym, sectionId);

        return data;
    }

    /// <summary>
    /// THE no-regression pin for the v4 reshape. For every shipped synonym: a CV whose profile is
    /// followed by that heading must (a) still split — the body must NOT be swallowed into the
    /// profile (the #815 bug this vocabulary exists to fix) — and (b) resolve to the sectionId the
    /// lexicon files it under.
    ///
    /// <para>Mutation-verified: dropping a synonym from the asset, or renaming the JSON key the
    /// segmenter reads, turns this red for the affected rows. A v3 → v4 union that silently lost a
    /// token could not survive it.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedSynonyms))]
    public void EveryFreeSectionSynonym_StillTerminatesThePrecedingSection(
        string synonym, string expectedSectionId)
    {
        // The heading as a real CV would carry it — the user's own casing, not the normalised form.
        var heading = synonym.ToUpperInvariant();
        var cv =
            $"""
            Profil
            Erfaren utvecklare med bakgrund inom offentlig sektor.

            {heading}
            Byggde en tjänst för ärendehantering.
            """;

        var result = _segmenter.Segment(cv);

        // (a) The heading TERMINATED the profile — the body did not get swallowed (#815).
        var profile = result.Content.Profile.ShouldNotBeNull();
        profile.ShouldNotContain(
            "ärendehantering",
            Case.Insensitive,
            $"Rubriken '{heading}' avslutade inte profilen — dess innehåll svaldes (#815-buggen).");

        // The free section exists, and carries the user's own heading verbatim (never the id).
        var section = result.Content.Sections.ShouldHaveSingleItem();
        section.Heading.ShouldBe(heading);

        // (b) RECOGNITION resolves it to the canonical id the RECOMMENDATION asset will key on.
        _sut.TryResolveSectionId(section.Heading).ShouldBe(expectedSectionId);
    }

    [Theory]
    // The user's own casing and punctuation — the port normalises, the caller must not.
    [InlineData("PROJEKT", "projekt")]
    [InlineData("projekt", "projekt")]
    [InlineData("  Utvalda projekt:  ", "projekt")]
    [InlineData("Selected Projects", "projekt")]
    [InlineData("Certifieringar", "certifikat")]
    [InlineData("Referenser.", "referenser")]
    public void TryResolveSectionId_ResolvesAHeadingAsTheUserWroteIt(string heading, string expected) =>
        _sut.TryResolveSectionId(heading).ShouldBe(expected);

    /// <summary>
    /// A TYPED heading is not a free section. Returning an id here would make a recommendation asset
    /// believe "Erfarenhet" is a suggestible free section — and the two id-spaces would collide.
    /// </summary>
    [Theory]
    [InlineData("Erfarenhet")]
    [InlineData("Utbildning")]
    [InlineData("Kompetenser")]
    [InlineData("Språk")]
    public void TryResolveSectionId_ReturnsNullForATypedHeading(string heading) =>
        _sut.TryResolveSectionId(heading).ShouldBeNull();

    [Theory]
    [InlineData("Min egen rubrik")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveSectionId_ReturnsNullForAnUnknownHeading(string heading) =>
        _sut.TryResolveSectionId(heading).ShouldBeNull();

    [Fact]
    public void TryResolveSectionId_ThrowsOnNull() =>
        Should.Throw<ArgumentNullException>(() => _sut.TryResolveSectionId(null!));

    /// <summary>
    /// The port must expose the SAME id-space the resolver returns. A SectionIds set that omitted an
    /// id the resolver can produce would make the asset's contract test (every referenced id exists)
    /// pass while the asset referenced an id it could never match.
    /// </summary>
    [Fact]
    public void SectionIds_ContainsEveryIdTheResolverCanReturn()
    {
        _sut.SectionIds.ShouldNotBeEmpty();

        var resolvable = ShippedPairs()
            .Select(pair => pair.SectionId)
            .Distinct()
            .ToList();

        resolvable.ShouldNotBeEmpty();
        foreach (var sectionId in resolvable)
            _sut.SectionIds.ShouldContain(sectionId);

        _sut.SectionIds.Count.ShouldBe(resolvable.Count);
    }

    /// <summary>The version a consumer asset pins. Unbound before v4 — see the integrity suite.</summary>
    [Fact]
    public void Version_IsTheShippedLexiconVersion() => _sut.Version.ShouldBe(4);
}
