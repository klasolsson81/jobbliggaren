using System.Reflection;
using System.Text.Json;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #815 — the free-section vocabulary must stay DISJOINT from the typed headings and from the
/// CV-title name banners. An overlap is not cosmetic: a token in both maps would make a typed
/// section (Erfarenhet) resolve as a free one, or turn "Meritförteckning" from a banner into a
/// section, silently changing how every CV is segmented.
///
/// The original claim that this was "verified" rested on a one-off script. A script is not a
/// guarantee — the lexicon is data, and data grows. This is the guarantee.
/// </summary>
public class CvParsingLexiconIntegrityTests
{
    private sealed record Lexicon(
        Dictionary<string, string[]> Headings,
        string[]? NameBanners,
        string[]? FreeSectionHeadings);

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
        value.Trim().TrimEnd(':', '.', ' ', '	').ToLowerInvariant();

    [Fact]
    public void FreeSectionHeadings_AreDisjointFromTheTypedHeadings()
    {
        var lexicon = Load();

        var typed = lexicon.Headings.Values.SelectMany(v => v).Select(Normalize).ToHashSet();
        var free = (lexicon.FreeSectionHeadings ?? []).Select(Normalize).ToList();

        var overlap = free.Where(typed.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE typade och fria rubriker: {string.Join(", ", overlap)}. " +
            "En sådan krock ändrar tyst hur varje CV segmenteras.");
    }

    [Fact]
    public void FreeSectionHeadings_AreDisjointFromTheNameBanners()
    {
        var lexicon = Load();

        var banners = (lexicon.NameBanners ?? []).Select(Normalize).ToHashSet();
        var free = (lexicon.FreeSectionHeadings ?? []).Select(Normalize).ToList();

        var overlap = free.Where(banners.Contains).ToList();

        overlap.ShouldBeEmpty(
            $"Dessa tokens är BÅDE namn-banners och fria rubriker: {string.Join(", ", overlap)}.");
    }

    [Fact]
    public void FreeSectionHeadings_AreNonEmptyAndNormalized()
    {
        var lexicon = Load();
        var free = lexicon.FreeSectionHeadings.ShouldNotBeNull();

        free.ShouldNotBeEmpty();
        // Lagras normaliserat (gemener, trimmat) — annars matchar de aldrig, tyst.
        free.ShouldAllBe(h => h == Normalize(h));
        free.Distinct().Count().ShouldBe(free.Length, "Dubbletter i freeSectionHeadings.");
    }
}
