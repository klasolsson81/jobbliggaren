using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>Which authored list a marker came from. An enum rather than a magic string
/// (CLAUDE.md §5) — it selects the linearized section to scope the level-3 witness to.</summary>
public enum MarkerKind
{
    Employment,
    Education,
}

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

    /// <summary>The CV PROMOTED and the marker is NOT a promoted company/institution field. That
    /// is all this member asserts, and it deliberately does NOT distinguish two cases the corpus
    /// has now measured both of:
    /// <list type="bullet">
    /// <item>the marker is genuinely gone — the silent loss this corpus exists to expose;</item>
    /// <item>the marker is PRESENT inside the promoted section but not as the field it names —
    /// fused into another value (`pdf-zero-xgap-concat`, in the baseline since before #1060 β-1)
    /// or sitting in the other slot (`docx-company-first-header`, β-1).</item>
    /// </list>
    /// <para>The two rendered halves are what tell them apart: `In promoted section (span)` yes
    /// with `Promoted structural field` no is the second case. An earlier revision of this summary
    /// said "nowhere in the promoted CV … this employment is gone", which was false for every such
    /// row — and was false in the tree before β-1, not because of it.</para></summary>
    RetainedButOrphaned,

    /// <summary>The CV promoted and the marker IS in the promoted CV, but in the wrong section —
    /// swallowed into a summary or a list. Present, but not as what it is. Not survival.</summary>
    AbsorbedIntoOtherSection,

    /// <summary>The auto-promote handler returned a genuine FAILURE (unknown or foreign artifact,
    /// infrastructure) rather than a gate verdict. Nothing may be concluded about this marker.
    /// Deliberately distinct from RetainedNotPromoted, which asserts an HONEST BLOCK — reporting a
    /// fault as an honest block would be the mis-report this corpus exists to catch.</summary>
    PromoteFaulted,
}

/// <summary>One marker's three-level trace, plus the verdict that reads them together.</summary>
/// <summary>
/// One marker's trace. <see cref="IsPromotedStructuralField"/> and <see cref="InPromotedSectionSpan"/>
/// are carried SEPARATELY rather than pre-combined: the verdict needs both, but a reader needs to
/// see which half failed. That rationale is right, and it is no longer hypothetical — the two
/// halves DO diverge, by two measured mechanisms:
/// <list type="bullet">
/// <item><b>Fusion.</b> <c>pdf-zero-xgap-concat</c> concatenates cells, so the marker sits inside
/// the promoted section but is no company's exact value. Present in the committed baseline since
/// before #1060 β-1 — an earlier revision of this paragraph said "today they always agree", and
/// that was already false when it was written.</item>
/// <item><b>Wrong slot.</b> <c>docx-company-first-header</c> (β-1) puts the employer in the role
/// field and the role in the employer field. Every marker on that row diverges.</item>
/// </list>
/// <para>Neither arrived through <c>AutoPromoteContentMapper</c>'s <c>Description: null</c> policy,
/// which the old paragraph named as the only thing holding the halves together. A pre-combined
/// boolean would have left the report saying "this employment is gone" without a cell moving, and
/// for one marker on <c>pdf-zero-xgap-concat</c> it effectively did — the span half was rendered
/// and the structural half was not, so the table printed a verdict while hiding one of the two
/// inputs that produced it. Both halves are rendered now.</para>
/// </summary>
public sealed record MarkerTrace(
    string Marker,
    MarkerKind Kind,
    bool InExtractedBytes,
    bool InParsedArtifact,
    bool IsPromotedStructuralField,
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
        MarkerKind kind,
        string rawText,
        ParsedResume? parsed,
        Resume? promoted,
        bool promoteFaulted)
    {
        var linear = promoted?.MasterVersion.Content is { } content
            ? ResumeContentLinearizer.Linearize(content)
            : null;

        return
        [
            .. markers.Select(marker =>
                TraceOne(marker, kind, rawText, parsed, promoted, linear, promoteFaulted)),
        ];
    }

    private static MarkerTrace TraceOne(
        string marker, MarkerKind kind, string rawText, ParsedResume? parsed, Resume? promoted,
        LinearizedResume? linear, bool promoteFaulted)
    {
        var inBytes = rawText.Contains(marker, StringComparison.Ordinal);
        var inParsed = parsed is not null && InParsedArtifact(parsed, marker);

        var wantedKind = kind == MarkerKind.Employment
            ? LinearSectionKind.Experience
            : LinearSectionKind.Education;

        var isStructural = false;
        var inSpan = false;
        string? foundElsewhere = null;

        if (promoted?.MasterVersion.Content is { } content && linear is not null)
        {
            // Structural half: the marker must BE a promoted company/institution, not merely
            // appear somewhere in that section's prose.
            isStructural = kind == MarkerKind.Employment
                ? content.Experiences.Any(e => string.Equals(e.Company, marker, StringComparison.Ordinal))
                : content.Educations.Any(e => string.Equals(e.Institution, marker, StringComparison.Ordinal));

            inSpan = linear.Sections
                .Where(s => s.Kind == wantedKind)
                .Any(s => linear.Text.AsSpan(s.Start, s.Length)
                    .Contains(marker, StringComparison.Ordinal));

            if (!(isStructural && inSpan))
            {
                foundElsewhere = linear.Sections
                    .Where(s => s.Kind != wantedKind
                        && linear.Text.AsSpan(s.Start, s.Length)
                            .Contains(marker, StringComparison.Ordinal))
                    .Select(s => s.Kind.ToString())
                    .FirstOrDefault();
            }
        }

        var verdict = Decide(
            inBytes, inParsed, isStructural && inSpan, foundElsewhere, promoted is not null,
            promoteFaulted);

        return new MarkerTrace(
            marker, kind, inBytes, inParsed, isStructural, inSpan, foundElsewhere, verdict);
    }

    private static MarkerVerdict Decide(
        bool inBytes, bool inParsed, bool inOwnSection, string? foundElsewhere, bool promoted,
        bool promoteFaulted)
    {
        if (promoteFaulted)
            return MarkerVerdict.PromoteFaulted;

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
