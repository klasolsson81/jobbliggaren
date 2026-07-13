using System.Text.Json;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #815 / 8b.4a — the free-section vocabulary must stay DISJOINT from the typed headings and from
/// the CV-title name banners. An overlap is not cosmetic: a token in both maps would make a typed
/// section (Erfarenhet) resolve as a free one, or turn "Meritförteckning" from a banner into a
/// section, silently changing how every CV is segmented.
///
/// <para>8b.4a reshaped <c>freeSectionHeadings</c> (a flat token set) into <c>freeSections</c> — a
/// <c>sectionId → synonyms</c> map — so a recommendation asset can key on a canonical id instead of
/// forking the synonyms into a second file. These tests moved with it, and gained the invariants the
/// new shape makes possible (a synonym may not be claimed by two ids; the ids must be stable slugs)
/// plus the one it makes necessary (the version must be BOUND — before v4 the in-file <c>version</c>
/// was read by nothing, so a version pin would have been vacuous).</para>
///
/// <para><b>Every assertion here is guarded against vacuity.</b> The v3 suite deserialised the
/// lexicon into a record whose fields were nullable and defaulted to empty — so had the JSON key
/// ever been renamed, `free` would have been `[]`, every "no overlap" assertion would have passed
/// over an empty set, and the suite would have gone green on a lexicon it could no longer see. That
/// is exactly what the rename in this PR would have done. The <c>ShouldNotBeEmpty</c> preflights
/// below are not decoration.</para>
/// </summary>
public class CvParsingLexiconIntegrityTests
{
    private sealed record Lexicon(
        int Version,
        Dictionary<string, string[]> Headings,
        string[]? NameBanners,
        Dictionary<string, string[]>? FreeSections);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static Lexicon Load()
    {
        // The lexicon is embedded in Infrastructure; read it from that assembly.
        var assembly = typeof(Jobbliggaren.Infrastructure.DependencyInjection).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json");

        stream.ShouldNotBeNull("Det inbäddade parsing-lexikonet saknas.");

        return JsonSerializer.Deserialize<Lexicon>(stream, JsonOptions).ShouldNotBeNull();
    }

    private static string Normalize(string value) =>
        value.Trim().TrimEnd(':', '.', ' ', '\t').ToLowerInvariant();

    /// <summary>Every free-section synonym, flattened across all sectionIds.</summary>
    private static List<string> FreeSynonyms(Lexicon lexicon) =>
        (lexicon.FreeSections ?? [])
            .SelectMany(pair => pair.Value)
            .Select(Normalize)
            .ToList();

    [Fact]
    public void FreeSectionSynonyms_AreDisjointFromTheTypedHeadings()
    {
        var lexicon = Load();

        var typed = lexicon.Headings.Values.SelectMany(v => v).Select(Normalize).ToHashSet();
        var free = FreeSynonyms(lexicon);

        // Vacuity guard: a renamed/absent JSON key would leave `free` empty and make the overlap
        // assertion below pass without ever being able to fail.
        free.ShouldNotBeEmpty("freeSections är tomt — testet skulle passera utan att kunna falla.");

        var overlap = free.Where(typed.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE typade och fria rubriker: {string.Join(", ", overlap)}. " +
            "En sådan krock ändrar tyst hur varje CV segmenteras.");
    }

    [Fact]
    public void FreeSectionSynonyms_AreDisjointFromTheNameBanners()
    {
        var lexicon = Load();

        var banners = (lexicon.NameBanners ?? []).Select(Normalize).ToHashSet();
        var free = FreeSynonyms(lexicon);

        banners.ShouldNotBeEmpty("nameBanners är tomt — testet skulle passera utan att kunna falla.");
        free.ShouldNotBeEmpty("freeSections är tomt — testet skulle passera utan att kunna falla.");

        var overlap = free.Where(banners.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE namn-banners och fria rubriker: {string.Join(", ", overlap)}.");
    }

    [Fact]
    public void FreeSectionSynonyms_AreNonEmptyAndNormalized()
    {
        var lexicon = Load();
        var freeSections = lexicon.FreeSections.ShouldNotBeNull(
            "freeSections saknas i lexikonet (8b.4a-formen: sectionId → synonymer).");

        freeSections.ShouldNotBeEmpty();

        var free = FreeSynonyms(lexicon);
        free.ShouldNotBeEmpty();

        // Lagras normaliserat (gemener, trimmat) — annars matchar de aldrig, tyst.
        free.ShouldAllBe(h => h == Normalize(h));
        free.Distinct().Count().ShouldBe(free.Count, "Dubbletter i freeSections synonymer.");
    }

    /// <summary>
    /// 8b.4a: a synonym claimed by TWO sectionIds is not a last-one-wins — it would make "which
    /// section is this?" depend on JSON key order, and the recommendation side would suppress a
    /// suggestion for one id while the CV was recognised as the other. The loader throws on it;
    /// this pins the shipped data so the throw is never reached in production.
    /// </summary>
    [Fact]
    public void NoFreeSectionSynonym_IsClaimedByTwoSectionIds()
    {
        var lexicon = Load();
        var freeSections = lexicon.FreeSections.ShouldNotBeNull();

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
    /// name, and they may reach the wire. Keep them stable, lowercase ASCII slugs — an id carrying
    /// "ä"/"ö" invites the encoding drift that silently breaks a lookup across a JSON/C#/TS boundary
    /// (repo precedent: the lexicon itself carries BOTH "utmärkelser" and "utmarkelser" as synonyms
    /// precisely because that drift is real).
    /// </summary>
    [Fact]
    public void SectionIds_AreStableLowercaseAsciiSlugs()
    {
        var lexicon = Load();
        var ids = lexicon.FreeSections.ShouldNotBeNull().Keys.ToList();

        ids.ShouldNotBeEmpty();

        var offenders = ids.Where(id => !IsLowercaseAsciiSlug(id)).ToList();

        offenders.ShouldBeEmpty(
            $"Dessa sectionIds är inte stabila gemena ASCII-slugs: {string.Join(", ", offenders)}.");
    }

    private static bool IsLowercaseAsciiSlug(string id) =>
        id.Length > 0 && id.All(c => c is >= 'a' and <= 'z');

    /// <summary>
    /// 8b.4a: the in-file "version" is now BOUND (a consumer asset pins it to fail loud on drift).
    /// Before v4 it was read by nothing — and a version nobody reads cannot detect a reshape. This
    /// asserts the value is both present and the one the code was built against.
    /// </summary>
    [Fact]
    public void LexiconVersion_IsBoundAndCurrent()
    {
        var lexicon = Load();

        lexicon.Version.ShouldBe(
            4,
            "Lexikonets version ändrades utan att konsumenterna följde med. En konsument som pinnar " +
            "en gammal version ska fela högljutt — inte degradera tyst.");
    }
}
