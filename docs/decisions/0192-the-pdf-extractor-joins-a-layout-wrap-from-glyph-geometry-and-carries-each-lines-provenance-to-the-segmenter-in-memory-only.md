# ADR 0192 — The PDF extractor joins a layout wrap from glyph geometry, and carries each line's provenance to the segmenter in memory only

**Date:** 2026-11-27
**Status:** Accepted 2026-11-27 (the rulings of `senior-cto-advisor` named under *Deciders*, under Klas's directive of 2026-11-26).
**Deciders:** Klas Olsson (controller and product owner), by his directive of 2026-11-26 that a defect with the same root cause or in the same area is fixed in the same PR and not filed ·
`senior-cto-advisor` (rulings D4 and D5 of 2026-11-26, `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md`; points 1 and 2 of its pre-code ruling of 2026-11-27, `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md`; both report-only, local and gitignored; the rulings' numbers are their own and are not this ADR's D1–D6) ·
`dotnet-architect` (pre-code pass of 2026-11-27, points 1–5 and 14, `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md`; report-only, local and gitignored)
**Amends:** nothing formally. What `CvExtractionResult.RawText` holds for a PDF changes (D1): the author's lines, not the page's.
**Related:** [#2342](https://github.com/klasolsson81/jobbliggaren/issues/2342) (a wrapped PDF bullet is scored as two) · [#2349](https://github.com/klasolsson81/jobbliggaren/issues/2349) (a wrap before a capital, a digit or punctuation still splits a bullet) · [#2350](https://github.com/klasolsson81/jobbliggaren/issues/2350) (a wrapped sentence is two sentences to A9 and C3) · [#2351](https://github.com/klasolsson81/jobbliggaren/issues/2351) (the tail of a wrapped entry header becomes a bullet) · PR #2353 · ADR 0071 (deterministic CV engines, no AI) · ADR 0074 (Invariant 3) · ADR 0093 · ADR 0191 (the review side, PR #2354) · ADR 0045 (budgets)
**Measured against:** branch `fix/cv-user-feedback` in the worktree `C:/tmp/jbl-cv-user` (it pushes to `fix/2342-pdf-wrapped-bullets`), head `ef2d6584f`, read on 2026-11-27. The measurements under *Evidence* are the driving session's of the same day, taken on synthetic CVs.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. The code facts were read at the head named above. The measurements (the probe, the corpus run, the exporter geometry) are the driving session's and the rulings' of 2026-11-27 and were not re-run for this ADR. The ruling files are gitignored local files, so what they decided is restated here and they are cited as local provenance. The test user's CV is never quoted: every example is synthetic.
>
> **Numbering.** 0192 was verified free on 2026-11-27 by the driving session (`origin/main`, the main copy, every worktree, open PRs). The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0190, 0191).

---

## Context

The review is deterministic and uses no AI (ADR 0071). A real test user's English single-column PDF CV was scored badly because the import misread it. ADR 0191 (PR #2354, merged) fixed the review side. This ADR is the import side, the first of three (the vocabularies are ADR 0193, the description is ADR 0194).

1. **The extractor ends a line at every layout wrap.** `PdfPigOpenXmlCvTextExtractor` takes a PDF's text from PdfPig's `ContentOrderTextExtractor.GetText` (0.1.16), which starts a new line whenever the baseline moves. A bullet that wraps on the page is two lines of `RawText`, so the review scored the wrap tail as a bullet of its own. On the synthetic copies of the test user's shape: 15–17 scoring units for 7 bullets, A9 Fail, A6 Warn (probe 2026-11-27).
2. **A review-side join did not fit the user's form and was removed.** The first fix joined wraps in the review (`LineWrapMeasure`), measuring in characters against the document's longest line. On the test user's shape it fired zero times (senior-cto-advisor 2026-11-26, correction 1). The definition of a wrap was right, the next word did not fit, but a character count is a proxy for the one thing the PDF knows exactly, where each glyph sits. That join lived on this PR's earlier commits and never reached `main`; it is deleted (D6).
3. **A wrap is the adapter's business.** A layout wrap says nothing about what the author wrote. It belongs behind the anti-corruption layer (Evans 2003, ch. 14), so that `RawText` becomes the author's lines and every consumer reads the same text. Persisting geometry and keeping the join in the review was rejected: every consumer would have to find its own blocks inside `RawText` to apply it.
4. **Two constraints.** The text must stay `GetText`'s, so that a DOCX and every corpus arm are unaffected and the personnummer scan reads what it read. And a measurement that cannot be made must leave today's text, not a guess.

## Decision

**The PDF extractor reads the glyph geometry of the same letters `GetText` reads, groups them into the physical lines `GetText` emits, and replaces a line end by one space only where the next line's first word demonstrably did not fit before the document's right edge, with four further conditions. The text is `GetText`'s with only those seams' newlines changed. Each `RawText` line carries a `CvLineProvenance` (the length of its first physical segment when it was joined, where it starts, its own space width) in a `CvTextProvenance` that travels from the extractor through the import to the segmenter in memory only. The entry-boundary reader reads a header's shape on its first physical segment and its fields on the whole line.**

### D1 — The text stays `GetText`'s; the geometry is read beside it (`PdfPageLines`)

- `PdfPageLines` (`Infrastructure/Resumes/Parsing/`) takes the page's `Letters` and mirrors `GetText`'s own newline rule: a non-space letter starts a new line when its baseline differs from the previous non-whitespace letter's by more than 0.9 times the smaller of the two rounded point sizes. It then aligns each output line's non-whitespace characters, in order, to those letters. A line gets its words' left and right x, from which come `Left`, `Right`, `FirstWordWidth` and `SpaceWidth` (the line's smallest positive gap between two words; none for a one-word line).
- **Fail-safe, all or nothing per document.** A page whose lines or characters do not align, a document truncated at `MaxOutputChars`, or an exception in the geometry pass (caught as `Exception` other than `OperationCanceledException`, which still propagates) leaves the whole document unjoined, with `CvTextProvenance.None`. That is today's text, byte for byte. The geometry never changes what `GetText` returned.
- **Pin against a package bump.** The mirror is a copy of a private rule. `PdfPageLinesTests` reads the PdfPig package's informational version and fails on anything but 0.1.16, so a changed `GetText` is re-read instead of every join silently stopping.

### D2 — A line end is a wrap when the next first word did not fit; the edge is the document's (`PdfLineJoiner`)

A line end is a layout wrap when `previous.Right + previous.SpaceWidth + next.FirstWordWidth > edge`. The line's own space width is the unit; nothing is tuned and no constant is introduced.

**The edge is the furthest line end in the whole document, not on the page** (senior-cto-advisor 2026-11-27, point 1, which corrects its own D4 wording). On a page shorter than the column the page's widest line is always a "wrap", because nothing on that page reaches further. Measured on the synthetic Word export (2026-11-27): page 2's widest line ends at x 330.2 against a document edge of 517.7, and under the page edge two authored line ends counted as wraps (slack −14.4 and −69.4); only the lower-case condition kept them from joining. The document edge is at least every page edge, so the joins under it are a subset of the joins under the page edge: it only narrows, the fail-safe direction. It changes nothing on any single-page file measured, on the Word export's page 1, or on the corpus's 16 PDF arms.

### D3 — What becomes a space

A seam between two lines becomes one space instead of a newline when all of these hold:

- both lines are on the same page, the previous line has a space width (a previous line of one word has none, so it never joins), and the line end is a wrap (D2);
- the two lines overlap in x;
- the next line does not open with a list-item glyph and a space (`CvLineShapes.TryLeadMarker`, the shared marker set);
- **and** either (a) the line continues a list item at its hanging indent (#2349), **or** (b) the next line opens with a lower-case letter and the previous line ends with none of `. ! ? : ; …`.

**(a), #2349.** An item is a line that opens with a marker glyph and a space and has at least two words; its text indent is its second word's left. A wrap whose next line starts at that indent, within the item's own space width, continues the item whatever it opens with. A joined continuation keeps the item; a seam that is not joined starts over from the next line, which is an item only if it opens with a marker. The tolerance is the item's own space width, not a constant. A line at the marker's indent never qualifies, because the text indent minus the marker's left is the marker's width plus its gap, which is more than the gap, which is at least the space width. Measured 2026-11-27: in the synthetic Google Docs and Word exports the glyph sits at x 90.0 and the text and its continuation at 108.0 (difference 0.0); in the Chrome glyph list there is no hanging indent, the difference is 6.3–6.9 against a space width of 2.45–3.05, and (b) governs, as the ruling requires. A list with no glyphs, or glyphs drawn by the renderer, keeps (b).

Only a joined seam's `\n` becomes `' '`; every character is kept.

### D4 — Provenance: per line, in memory, never persisted

- `CvLineProvenance(int? FirstSegmentLength, double? Left, double? SpaceWidth)` (`Application/Resumes/Abstractions/`). `FirstSegmentLength` is the trimmed length of the line's first physical segment when the extractor joined a wrap into it, else null; `Joined` is derived from it, so there is one truth. `Left` and `SpaceWidth` are in PDF points. A line the extractor did not measure, every DOCX line included, is `Unmeasured`.
- `CvTextProvenance` holds one entry per line of `RawText`, indexed as the text split on `'\n'`; `For` refuses a different count, and `None` has no line measured.
- **Transport.** `CvExtractionResult.Provenance` is a **non-positional `init` member** defaulting to `None`; only the PDF success branch sets it. A positional parameter would have broken the twelve constructions and a text match in `ScanOnlyExtractionChannelTests`. `IResumeSegmenter` has **one** method, `Segment(rawText, provenance)`. `Segment(string)` exists only on the concrete `HeadingDrivenResumeSegmenter` and delegates with `None`, so a production caller cannot drop provenance by choosing an overload on the port. `ImportResumeCommandHandler` passes `extraction.Provenance`. Inside the segmenter a line is `CvLine(Text, Provenance)`.
- **Why not a second port method or a sibling return type.** Text and provenance come from one pass; a second method needs a second PdfPig parse (ADR 0045) or hidden state, and a sibling return type breaks every construction and stub (dotnet-architect point 1).
- **Never persisted, never logged.** The types are CV-derived (ADR 0074 Invariant 3). `ExtractionProvenanceStaysAtImportTests` fails the build when any source file outside three folders (`Application/Resumes/Abstractions`, `Application/Resumes/Commands/ImportResume`, `Infrastructure/Resumes/Parsing`) names `CvTextProvenance` or `CvLineProvenance`, so "never persisted" is structural. The segmenter reads provenance in `EntryBoundaries` (D5 here; rule D of ADR 0193).
- **A DOCX carries none.** The seam takes a DOCX source later without touching a segmentation rule (OCP, Martin 2017, ch. 8); that is a fact about the seam, not a promise (senior-cto-advisor 2026-11-27, point 2).

### D5 — #2351: shape on the first segment, fields on the whole line

`EntryBoundaries` judges whether a joined line is header-shaped on its first physical segment (`FirstSegmentLength`), which is exactly what those predicates read before the join, and reads the header's fields on the whole line. Joined, a measured wrapped header is 131 characters, over `MaxHeaderLength` (120), and judging the joined line would drop an anchor that works today. So the organisation is the whole second field, the wrap tail is no bullet of its own, and the anchor stays in a two-entry run. Rejected: hiding the tail on the review side (it leaves a truncated employer in the saved CV) and a segmenter-side wrap rule (a second definition of a wrap).

### D6 — What is deleted

The review-side join and its plumbing: `LineWrapMeasure`, the measure on `CvReviewEngine`, both review contexts, B5, `WeakVerbTransform` and `ClicheTransform`; the second consumer of `CvFileSignature.IsMediaType` (`main`'s `CvFileSignature` is restored); A6's separator set goes back to `[' ', ',', ';', '/']`. The `DescriptionBullets` rename stays. A search for `LineWrapMeasure` finds no code in `src` or `tests` (2026-11-27).

## Evidence

All dated 2026-11-27 and the driving session's; none was re-run for this ADR.

| What | Reading |
|---|---|
| Probe on the synthetic copies of the test user's shape | scoring units 15–17 → 7; A9 Fail → Pass; A6 → Pass |
| The layout corpus's 16 PDF arms | each measured whole (every page aligned); their extracted text is unchanged; zero joins |
| Exporter geometry, eleven synthetic PDFs from headless Chrome, Google Docs and Word (the driving session's probe) | every document aligned (`notAligned=0`); the Word export's page 2 as in D2 |

## Residuals

Stated as facts about the seam, never as promises.

- **A continuation that opens with a capital or a digit, outside a list item at a hanging indent, stays split.**
- **An authored line end within one word of the edge, followed by a lower-case line and no terminator, joins** (the one accepted false join the ruling pinned). In a list with a hanging indent, an authored line end within one word of the edge followed by an unmarked line at the text indent joins whatever that line opens with.
- **A hyphenated wrap joins with a space.**
- **A wrap across a page break stays split.**
- **A sidebar or second column never joins the main column.**
- **A page narrower than the document's widest line never joins** (the new residual of D2, today's behaviour).
- **A PDF whose pages do not align, or that is truncated, is not joined at all** (D1).
- **A DOCX gets no provenance** (D4); ADR 0193 names what that costs.
- **Exporters other than the measured ones are not verified:** headless Chrome print, and Google Docs and Word exports of a synthetic CV. A different producer's PDF may align differently or not at all, and then reads as today.
- **`RawText` changes meaning for a PDF.** Anything that located a layout wrap in it no longer can. The personnummer scan reads the same text apart from a joined seam's newline.
- **No timing of the geometry pass is recorded here.**

## Alternatives considered

### Alt A — The chosen design (D1–D6)
**For:** one definition of a wrap, made where the geometry is known; `RawText` is the author's lines for every consumer; the text is `GetText`'s, so a DOCX and the corpus are unchanged; a measurement that cannot be made leaves today's text.
**Against:** see *Residuals*.

### The review-side character join (the first fix)
Rejected: a character count against the document's longest line is a proxy, and it was inert on the user's form. Deleted (D6).

### A character width per indentation class
Rejected (senior-cto-advisor D4): still a character proxy, and it needs geometry anyway.

### Reading a glyph-led list alone
Rejected: it cannot tell the last bullet from the next header.

### Keeping the character join beside the geometric rule
Rejected: two definitions of a wrap, one of them inert on the user's form.

### Persisting geometry and joining in the review
Rejected: every consumer would have to locate its blocks inside `RawText`.

### Leaving the wrap as a named residual
Rejected: it is the test user's form.

### The page's furthest line end as the edge
Corrected (senior-cto-advisor 2026-11-27, point 1): it reads the edge too small on a short page (D2).

### `w:numPr` provenance in the DOCX reader
Not built (senior-cto-advisor 2026-11-27, point 2): a second provenance source inside the hardened reader of attacker-controlled input, and inline `w:numPr` misses numbering inherited from a style.

## Consequences

### Positive
- A wrapped bullet is one scoring unit, a wrapped header keeps its whole organisation, and a wrapped sentence is one sentence for A9 and C3, all from one rule at the adapter.
- The join is deterministic, has no tuned constant and cannot widen what a measurement does not show: a page that does not align, or a document narrower than a page, joins nothing.
- Provenance reaches the segmenter without a second parse, without touching the port's other callers and without a persisted trace.

### Negative and accepted
- **`RawText` changes meaning for a PDF** (*Residuals*).
- **A mirror of a private PdfPig rule** is part of the code, held by one test; a package bump needs a human read.
- **The join is blind to a DOCX,** which the extractor does not measure.
- **The residuals above are accepted and known.**

## Implementation

Part of PR #2353 (`fix/2342-pdf-wrapped-bullets`). Infrastructure: `PdfPageLines`, `PdfLineJoiner`, `CvLine`, `CvLineShapes` (the shared marker set), the PDF branch of `PdfPigOpenXmlCvTextExtractor`, `EntryBoundaries`. Application: `CvLineProvenance`, `CvTextProvenance`, `CvExtractionResult.Provenance`, `IResumeSegmenter`, `ImportResumeCommandHandler`. Architecture: `ExtractionProvenanceStaysAtImportTests`. The diff is the record of the rest.

## References

- **Local reports (gitignored):** `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md` (D4, D5) · `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md` (points 1, 2) · `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md` (points 1–5).
- **Issues:** #2342, #2349, #2350, #2351.
- **ADRs:** ADR 0045 · ADR 0071 · ADR 0074 · ADR 0093 · ADR 0191.
- Martin, *Clean Architecture* (2017), ch. 8 (OCP) · Evans, *Domain-Driven Design* (2003), ch. 14 (Anti-Corruption Layer) · PdfPig 0.1.16, `ContentOrderTextExtractor` (the rule D1 mirrors; links rot).
