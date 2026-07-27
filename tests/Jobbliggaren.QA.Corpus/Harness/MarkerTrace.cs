using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>What became of one authored marker on its way through the chain.</summary>
public enum MarkerVerdict
{
    /// <summary>The marker is not even in the extracted text. The corpus cannot see content it
    /// authored, so no row downstream of this means anything.</summary>
    LostBeforeParse,

    /// <summary>Present in the extracted text but in no structured field — carried only in the
    /// preamble. Nothing is claimed about it and nothing was promoted: honest, not silent.</summary>
    CarriedInPreamble,

    /// <summary>Present in the parsed artifact but the CV did not promote. The content is
    /// retained on the staging artifact and the user was told; an honest block.</summary>
    RetainedNotPromoted,

    /// <summary>The CV PROMOTED and the marker is in the promoted CV's own section. Survived.</summary>
    Survived,

    /// <summary>The CV PROMOTED and the marker is nowhere in the promoted CV. This is the silent
    /// loss: the product said the CV was saved and this employment is gone.</summary>
    RetainedButOrphaned,

    /// <summary>The CV promoted and the marker IS in the promoted CV, but in the wrong section —
    /// swallowed into a summary or a list. Present, but not as what it is. Not survival.</summary>
    AbsorbedIntoOtherSection,
}

/// <summary>One marker's three-level trace, plus the verdict that reads them together.</summary>
public sealed record MarkerTrace(
    string Marker,
    string Kind,
    bool InExtractedBytes,
    bool InParsedArtifact,
    bool InPromotedSectionSpan,
    string? FoundInOtherSection,
    MarkerVerdict Verdict);

/// <summary>
/// The oracle. A count says five became one; a marker trace says WHICH four vanished and where
/// each was last seen — and that difference is the whole reason this corpus exists. A count-only
/// reading is also blind to the mutations that matter most: swapping Role and Company, or nulling
/// RawPeriod, leaves every count identical.
///
/// <para><b>Level 3 fails CLOSED, deliberately.</b> An ordinal <c>Contains</c> over the whole
/// linearized CV would report SURVIVED for a marker that was swallowed into the summary — present
/// in the text, absent as an employment — while the count column simultaneously reported it lost.
/// So level 3 is scoped to the promoted CV's own <c>Experience</c> (or <c>Education</c>) span AND
/// requires exact equality with some promoted company (or institution). A marker found anywhere
/// else gets its own verdict naming the section that absorbed it, which is a finding rather than a
/// survival.</para>
///
/// <para><c>ResumeContentLinearizer</c> is used rather than a hand-rolled flattener because it is
/// the Domain's own projection with a pinned losslessness contract — "what counts as being in the
/// CV" has one home, and it is not this file.</para>
/// </summary>
internal static class MarkerTracer
{
    internal static IReadOnlyList<MarkerTrace> Trace(
        IReadOnlyList<string> markers,
        string kind,
        string rawText,
        ParsedResume? parsed,
        Resume? promoted)
    {
        var linear = promoted?.MasterVersion.Content is { } content
            ? ResumeContentLinearizer.Linearize(content)
            : null;

        return [.. markers.Select(marker => TraceOne(marker, kind, rawText, parsed, promoted, linear))];
    }

    private static MarkerTrace TraceOne(
        string marker, string kind, string rawText, ParsedResume? parsed, Resume? promoted,
        LinearizedResume? linear)
    {
        var inBytes = rawText.Contains(marker, StringComparison.Ordinal);
        var inParsed = parsed is not null && InParsedArtifact(parsed, marker);

        var wantedKind = kind == "employment"
            ? LinearSectionKind.Experience
            : LinearSectionKind.Education;

        var inOwnSection = false;
        string? foundElsewhere = null;

        if (promoted?.MasterVersion.Content is { } content && linear is not null)
        {
            // Structural half: the marker must BE a promoted company/institution, not merely
            // appear somewhere in that section's prose.
            var isStructural = kind == "employment"
                ? content.Experiences.Any(e => string.Equals(e.Company, marker, StringComparison.Ordinal))
                : content.Educations.Any(e => string.Equals(e.Institution, marker, StringComparison.Ordinal));

            var inSpan = linear.Sections
                .Where(s => s.Kind == wantedKind)
                .Any(s => linear.Text.AsSpan(s.Start, s.Length)
                    .Contains(marker, StringComparison.Ordinal));

            inOwnSection = isStructural && inSpan;

            if (!inOwnSection)
            {
                foundElsewhere = linear.Sections
                    .Where(s => s.Kind != wantedKind
                        && linear.Text.AsSpan(s.Start, s.Length)
                            .Contains(marker, StringComparison.Ordinal))
                    .Select(s => s.Kind.ToString())
                    .FirstOrDefault();
            }
        }

        var verdict = Decide(inBytes, inParsed, inOwnSection, foundElsewhere, promoted is not null);
        return new MarkerTrace(marker, kind, inBytes, inParsed, inOwnSection, foundElsewhere, verdict);
    }

    private static MarkerVerdict Decide(
        bool inBytes, bool inParsed, bool inOwnSection, string? foundElsewhere, bool promoted)
    {
        if (!inBytes)
            return MarkerVerdict.LostBeforeParse;

        if (!promoted)
            return inParsed ? MarkerVerdict.RetainedNotPromoted : MarkerVerdict.CarriedInPreamble;

        if (inOwnSection)
            return MarkerVerdict.Survived;

        // Only reachable once the CV promoted — a not-promoted CV can never be "orphaned",
        // because nothing was claimed about it.
        return foundElsewhere is not null
            ? MarkerVerdict.AbsorbedIntoOtherSection
            : MarkerVerdict.RetainedButOrphaned;
    }

    /// <summary>Level 2 deliberately accepts the marker ANYWHERE on the staging artifact — an
    /// entry's raw text, the preamble, or a free section. Anchoring it to the first experience
    /// entry (the tempting shortcut) would label the headingless control as the worst outcome in
    /// the corpus, when it is in fact the one case that loses nothing.</summary>
    private static bool InParsedArtifact(ParsedResume parsed, string marker)
    {
        var content = parsed.Content;

        if (content.Experience.Any(e => Has(e.RawText, marker) || Has(e.Organization, marker)))
            return true;
        if (content.Education.Any(e => Has(e.RawText, marker) || Has(e.Institution, marker)))
            return true;
        if (Has(content.Preamble, marker) || Has(content.Profile, marker))
            return true;

        return content.Sections.Any(s => s.Entries.Any(
            e => Has(e.Title, marker) || e.Lines.Any(l => Has(l, marker))));
    }

    private static bool Has(string? text, string marker) =>
        text is not null && text.Contains(marker, StringComparison.Ordinal);
}
