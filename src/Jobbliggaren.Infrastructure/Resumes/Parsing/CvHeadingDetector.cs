using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// "Which lines of this CV are section headings?" — asked by the SEGMENTER (which splits the
/// document on them) and by <c>SectionOrderAnalyzer</c> (which reads the document ORDER off them),
/// answered in ONE place.
///
/// <para><b>Why it was extracted (8b.4b, both review gates, independently).</b> The order analyzer
/// first re-implemented detection as "normalise the whole line, look it up". That is only HALF of
/// what the segmenter recognises: it also detects the <b>inline</b> form
/// (<c>"Kompetenser: C#, PostgreSQL, Docker"</c> — #421, boundary-gated, typed-only), which real
/// Swedish CVs write and which <see cref="CvParsingLexiconLoader.NormalizeHeading"/> cannot match
/// (it strips only a TRAILING colon). The consequence was not a crash but a <b>silent green light</b>:
/// the segmenter parsed the section, the analyzer did not see it, so a CV whose order genuinely
/// deviated came back "sektionerna står i rekommenderad ordning" and the reorder transform stayed
/// quiet. That is the exact defect 8b.4b exists to remove, reproduced one layer down — a
/// recognition rule with two homes that disagree.</para>
///
/// <para>Sharing the NORMALISER was not enough, and the old comment saying so was the tell: the
/// drift was never in the normaliser, it was in the <b>detection rule</b>. One knowledge piece, one
/// home (ADR 0108 doctrine, applied to the layer that actually carries it).</para>
/// </summary>
internal static class CvHeadingDetector
{
    /// <summary>
    /// Every heading line in the document, in order. <see cref="DetectedHeading.Kind"/> is the typed
    /// section, or <c>null</c> for a FREE section (whose canonical id is
    /// <see cref="DetectedHeading.FreeId"/>) — the two id-spaces are distinct and exactly one is set.
    /// </summary>
    internal static List<DetectedHeading> Detect(string[] lines, CvParsingLexiconData lexicon)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(lexicon);

        var headings = new List<DetectedHeading>();

        for (var i = 0; i < lines.Length; i++)
        {
            // Whole-line heading ("Kompetenser" / "Kompetenser:" — NormalizeHeading strips a
            // trailing colon). Position-independent: a bare heading token is a heading anywhere.
            if (TryMatch(lines[i], lexicon, out var kind, out var freeId, out var matched))
            {
                headings.Add(new DetectedHeading(i, kind, freeId, matched, HeadingTextOf(lines[i])));
                continue;
            }

            // #421 (#252-class): a heading may carry its content inline on the same line after a
            // colon ("Kompetenser: C#, PostgreSQL, Docker"). NormalizeHeading strips only a
            // TRAILING colon, so the inline form would otherwise register no heading and silently
            // drop the whole section (CLAUDE.md §5). Split on the FIRST colon only; when the left
            // part is a known heading and real content follows, register the heading with that
            // remainder as its first content line.
            //
            // Gate the inline split to a SECTION BOUNDARY (senior-cto-advisor 2026-07-01): a prose
            // line whose first word happens to be a heading token ("Erfarenhet: över 10 år inom
            // IT.", "Språk: flytande svenska") must NOT hijack and truncate the section it sits in
            // into a phantom section — the mirror risk of the silent-drop fix. Content shape cannot
            // distinguish the wanted inline "Profil: <prose>" from unwanted prose, so we gate on
            // document POSITION with the one robust single-pass invariant: a line starts a section
            // only if it is the document's first line or is preceded by a blank line. Prose sitting
            // directly under a heading is therefore that heading's content, never a new section
            // (adjacency without a blank line is a deliberate, safe miss). Every other colon line
            // (URLs, times, "Ansvarig för: …" prose) is left untouched.
            var atSectionBoundary = i == 0 || lines[i - 1].Trim().Length == 0;

            var colon = lines[i].IndexOf(':', StringComparison.Ordinal);
            if (!atSectionBoundary || colon <= 0)
            {
                continue;
            }

            var inlineContent = lines[i][(colon + 1)..].Trim();

            // #815: the inline split is restricted to TYPED headings. A free heading may only be
            // recognised as a whole line.
            //
            // Why: every entry in Erfarenhet/Utbildning is separated by a blank line, so an entry's
            // FIRST line always satisfies the boundary gate above. A label-shaped free token would
            // then hijack it — "Kurs: Databaser 7,5 hp" inside an education entry would TERMINATE
            // Utbildning and degrade the remaining entries into free-section text. That is the
            // engine inventing structure the user did not write, which is the one thing it must
            // never do (ADR 0071). The typed vocabulary is small, curated and already lives with
            // this gate (2026-07-01 bind); the free vocabulary is open and label-shaped, so it does
            // not get the same privilege.
            //
            // Cost, accepted: "Projekt: A, B" written inline is not recognised as a section. Its
            // content stays in the section above — visible, editable, lossless. That is the honest
            // failure mode, and it is strictly better than a fabricated section boundary.
            if (inlineContent.Length > 0
                && TryMatch(lines[i][..colon], lexicon, out var inlineKind, out _, out var inlineMatched)
                && inlineKind is not null)
            {
                headings.Add(new DetectedHeading(
                    i, inlineKind, FreeId: null, inlineMatched,
                    HeadingTextOf(lines[i][..colon]), inlineContent));
            }
        }

        return headings;
    }

    /// <summary>
    /// The heading line as the USER wrote it — trimmed, trailing colon/period removed, nothing else
    /// touched. Free sections show this back to the user, and the order analyzer quotes it as
    /// evidence, so "PROJEKT" must not come back as "projekt" (that is the normalised form, which is
    /// structural evidence only).
    /// </summary>
    internal static string HeadingTextOf(string line) => line.Trim().TrimEnd(':', '.', ' ', '\t');

    /// <summary>
    /// True when the line normalises to a known section heading, out-ing the typed kind OR the free
    /// section id (exactly one is non-null) plus the matched normalised form. THE single lookup for
    /// both the whole-line and the inline path.
    /// </summary>
    private static bool TryMatch(
        string line,
        CvParsingLexiconData lexicon,
        out ParsedSectionKind? kind,
        out string? freeId,
        out string matched)
    {
        kind = null;
        freeId = null;
        matched = CvParsingLexiconLoader.NormalizeHeading(line);

        if (matched.Length == 0)
        {
            return false;
        }

        if (lexicon.HeadingMap.TryGetValue(matched, out var typed))
        {
            kind = typed;
            return true;
        }

        // #815: a FREE heading ("Projekt", "Referenser") is still a heading. It terminates the
        // preceding section — which is the whole fix, since before this a section ran until the next
        // TYPED heading and swallowed everything in between.
        //
        // 8b.4a: the lexicon returns the canonical sectionId rather than a bare bool. The SEGMENTER
        // deliberately discards it — a free section's identity is CONTENT there, and
        // ParsedSection.Heading must stay the user's own line. The order analyzer (8b.4b) DOES need
        // it: it ranks sections by identity, and two synonyms of one section must rank alike.
        return lexicon.FreeSectionIdByHeading.TryGetValue(matched, out freeId);
    }
}

/// <summary>
/// One heading line the detector found. Exactly one of <paramref name="Kind"/> (a typed section)
/// and <paramref name="FreeId"/> (a free section's canonical id) is non-null.
/// </summary>
/// <param name="Line">0-based line index in the document — the position the ORDER is read from.</param>
/// <param name="Matched">The normalised form (structural evidence only — never shown to the user).</param>
/// <param name="Heading">The heading AS THE USER WROTE IT (shown back to her; quoted as evidence).</param>
/// <param name="InlineContent">The content that followed a colon on the same line (#421), if any.</param>
internal readonly record struct DetectedHeading(
    int Line,
    ParsedSectionKind? Kind,
    string? FreeId,
    string Matched,
    string Heading,
    string? InlineContent = null);
