<!--
  COMMITTED BASELINE for the layout corpus (#1060 PR K; four spaced rows and appendix
  sections B and C added by PR E).

  Why this file is tracked while the live artifact is not: PR K exists to establish the BASE that
  PR E's delta is measured against (CTO R4 — "measure the base, then the delta"). A baseline that
  lives only in a gitignored artifact and a PR body is not a baseline, because nobody can diff it.
  This copy is tracked so `git diff` answers the question directly.

  ENCODING: UTF-8, no BOM, LF in the blob (checked out as CRLF on Windows via core.autocrlf,
  which is what the emitter writes there). An earlier revision committed CRLF, making this the
  only tracked markdown in the repo that did — on Linux the emitter writes LF and the file's own
  "diff against a fresh artifact" instruction then reported every line changed. The PR K revision of this file was MIXED - the emitted core
  was UTF-8 and the hand-written appendix was cp1252, leaving four bytes (0x97, 0xC5) that no
  UTF-8 reader can decode and that render as mojibake on GitHub. PR E rewrote the whole file
  as UTF-8 (CLAUDE.md 10 - UTF-8 everywhere). Append to it with a UTF-8 writer only.

  It was NOT placed under docs/reviews/: that directory is gitignored (.gitignore:123) under the
  ADR 0072 docs-privacy rule, so a file there could not be committed at all. The content here is
  synthetic throughout — invented person, employers and schools — and carries no personnummer.

  REGENERATE, then diff this file against the fresh artifact. STEP 0 IS MANUAL AND HAS
  ALREADY BEEN MISSED ONCE - PR B regenerated at a72c77e7 and left the constant reading
  7a5496fe, publishing post-B numbers under a pre-B provenance string:

    0. bump LayoutCorpusReportTests.BaseCommit to the commit you are regenerating at
    dotnet build tests/Jobbliggaren.QA.Corpus/Jobbliggaren.QA.Corpus.csproj
    cd tests/Jobbliggaren.QA.Corpus/bin/Debug/net10.0
    ./Jobbliggaren.QA.Corpus.exe -class "Jobbliggaren.QA.Corpus.LayoutCorpusReportTests"
    diff tests/Jobbliggaren.QA.Corpus/{baseline/layout-corpus-report.baseline.md,artifacts/layout-corpus-report.md}

  TWO HAND-WRITTEN BLOCKS LIVE IN THIS FILE AND THE EMITTER PRODUCES NEITHER: this comment
  and the "Appendix - measurements this suite deliberately does NOT reproduce" at the end.
  Regenerating and copying the artifact over this file DELETES BOTH. Splice instead: keep
  this header, take the artifact whole, re-append the appendix.

  AND NO GATE CHECKS THIS FILE AGAINST THE EMITTER. A hand-edit to the emitter-owned MIDDLE
  is caught by nothing automated and is then silently reverted by the next regeneration -
  measured 2026-07-28, when a glossary fix lived here and not in LayoutCorpusReport.cs,
  leaving a tracked file its own producer could not produce. It was found only by a reviewer
  reading the diff file by file; do not rely on that. Edit the EMITTER and regenerate; never
  edit the middle.

  NO AUTOMATED GUARD EXISTS DELIBERATELY, and both variants are ruled out rather than one.
  A byte-for-byte check would assert every count and gate verdict in the 21 measurement rows
  - exactly what OBSERVE-ONLY forbids and what §2.5 reserves for an explicit ratchet. A
  prose-only check avoids that but passes while the MEASUREMENTS are stale, which is the
  likelier failure and the one this PR exists for: row 17's block reason sat wrong in this
  tracked file from PR C's merge (0aecfbca) until this PR - the baseline was last
  regenerated at a72c77e7, PR C then changed what the product returns, and nothing
  regenerated it. That value lives on an interpolated row a prose-only sweep skips by
  construction, so the guard would have stood green straight through row 17's divergence -
  the tracked signature of the very product change this PR was opened to handle. Note the
  precision: NEITHER variant would have caught the ladder gap itself, which is an EMITTER
  defect - after a regeneration both sides carry the same wrong cells. A drift guard bounds
  DRIFT, never emitter correctness, and reading it as "this PR would not have been needed"
  is the wrong ground to revisit the build-or-not decision on.

  And "likelier" needs no count to stand: measurements go stale whenever the product moves
  and nobody regenerates - every product PR that touches the chain - while a prose divergence
  needs someone to hand-edit the middle. Green on the common case is the fail-open shape the
  mutation harness refused twice in a single session (its third refusal, on an uncommitted
  tree, guards destroyed evidence rather than a false green).

  THE CLOSING "-->" BELOW WAS MISSING FROM PR K UNTIL 2026-07-28, and the consequence was
  total: under CommonMark an unterminated HTML block runs to end of document, so every line
  of this file was inside the comment. Measured through GitHub's own renderer (POST /markdown)
  rather than argued: as committed it produced 0 bytes, no headings and no tables; with the
  "-->" added, 124367 bytes and 12 tables. A baseline exists so a reader can diff it and read
  it on GitHub; this one rendered as a blank page for its whole life.
-->

# Jobbliggaren — CV layout corpus, from bytes (#1060 PR K)

> Machine-generated by `LayoutCorpusReportTests`. The EMITTER is the deliverable; this
> artifact is regenerated on demand. Regenerate with:
>
> ```
> dotnet build tests/Jobbliggaren.QA.Corpus/Jobbliggaren.QA.Corpus.csproj
> cd tests/Jobbliggaren.QA.Corpus/bin/Debug/net10.0
> ./Jobbliggaren.QA.Corpus.exe -class "Jobbliggaren.QA.Corpus.LayoutCorpusReportTests"
> ```
>
> Base commit: `5456e784`.
> Deterministic; NO AI/LLM anywhere in the measured chain (ADR 0071).

## Claim discipline (ADR 0109 §4)

> Every row below is a PER-CASE BOOLEAN over ONE authored synthetic document. N = 1 per case. This corpus contains no sample of real CVs and licenses no frequency claim: not a percentage, not "most", not "commonly", not "typically". The only sentence form this data supports is: "the authored document <case> exhibits <property>". Every count in this file is a count of authored fixtures or of items inside a single fixture; none is a count of anything in the world.

> VENDOR DISCIPLINE: every case is named for its MECHANIC. No genuine Canva, Word, InDesign, LaTeX or Europass export was run through the extractor for this report. "docx" names a container format, not a producer.

> OBSERVE-ONLY: nothing in the tables below is asserted. A value that moves is a finding to read against the committed baseline, not a build failure. Ratcheting requires an explicit Klas ratchet (CLAUDE.md §2.5), and any ratchet must be stated as a positive verdict over named case ids, never as a negation.

## Divergence disclosure

What this run is NOT, stated up front rather than left for a reader to discover:

- EF InMemory: no database server, no container, no network, not the shared dev DB. It
  IS an EF provider with real change tracking and real global query filters — never
  read this as "no persistence layer".
- No DEK envelope round-trip, no SQL translation, no SmartEnum translation. Those stay
  proven by `AutoPromoteParsedResumeEncryptionTests` and the integration suites.
- No Mediator pipeline: no logging, validation, authorization or UnitOfWork behavior.
- The `IncompleteContent` sub-reason IS readable, and §5's `Domain code` column is where
  it is published. `AutoPromoteGate` carries `created.Error.Code` verbatim on that arm
  and the handler emits it as the `BlockDetail` property; this harness reads that
  property off the real log line, because `AutoPromoteGateVerdict` is `internal` to
  `Jobbliggaren.Application` and this assembly is not in its `InternalsVisibleTo` list.
  Nothing is re-typed: the code is the FIRST evaluation's output, not a second opinion.
- Substituted ports (none of them feeds an auto-promote gate):
  - IOccupationCodeDeriver (empty candidates)
  - IOccupationExperienceDeriver (empty years)
  - ISkillResolver (empty proposals)
  - IBinaryFieldSealer (identity passthrough)
  - ICurrentUser / ICorrelationIdProvider / IRequestContextProvider / IFailedAccessLogger
  - IResumeReviewReconciler (no-op)

## 0. Instrument integrity

- **byte proofs held:** `pdf-sidebar-emitted-first`, `pdf-interleaved-baseline-fusion`, `pdf-zero-xgap-concat`, `pdf-single-column-sv`, `pdf-single-column-spaced`, `pdf-single-column-intra-block-spaced`, `pdf-single-column-intra-block-spaced-tight-list`, `pdf-sidebar-spaced`, `pdf-single-column-en`, `pdf-nonsequential-decorative`, `pdf-headingless`, `pdf-unknown-heading-after-profile`, `pdf-known-heading-after-profile`, `pdf-decorated-heading-glue`, `pdf-two-page-seam`, `pdf-pnr-bearing`, `pdf-clean-body-pnr-in-account-name`, `docx-table-label-first-no-blanks`, `docx-flat-label-first-no-blanks`, `docx-table-label-first-with-blanks`, `docx-role-first-with-blanks`
- **byte proofs FAILED:** none
- **crashed:** none
- **fixture invalid:** none
- **gate ladder malformed:** none
- **block detail unreadable:** none

## 1. Fidelity ledger

Provenance is per SECTION, not per case. The 2026-07-26 spike measured extraction and
segmentation only — it produced no gate verdict, no promote boolean and no delta on a
promoted CV. So the "promote measured?" column reads `no` for every row until this
suite runs. There is deliberately no per-row promote-provenance column: it would be the
literal "no" on every row forever, which is a decoration rather than a measurement.

| # | Case id | CTO class | Container | One-variable step from | Extract+segment spike-measured? | Byte proof |
|---|---|---|---|---|---|---|
| 1 | `pdf-sidebar-emitted-first` | (a) two-column/sidebar — answered | pdf | — | yes | a vertical gutter of at least 15 pt exists (a single-column render cannot produce it), AND within each column every inter-baseline gap is within 2 pt of every other (uniform leading — no authored block spacing) |
| 2 | `pdf-interleaved-baseline-fusion` | (a) two-column/sidebar — the only shape whose two-column-ness the extractor makes visible | pdf | — | yes | at least 8 baselines carry words from both columns |
| 3 | `pdf-zero-xgap-concat` | (a) two-column/sidebar — the negative-x-gap defect the CTO named as known-remaining | pdf | — | yes | a word fuses two cells (a digit immediately followed by a letter) |
| 4 | `pdf-single-column-sv` | (b) single-column chronological — answered | pdf | — | yes | no vertical gutter of 15 pt or more exists (not multi-column), AND every inter-baseline gap is within 2 pt of every other (uniform leading — no authored block spacing) |
| 5 | `pdf-single-column-spaced` | (b) single-column chronological — the SPACED arm, which the class was missing | pdf | pdf-single-column-sv | no | no vertical gutter of 15 pt or more exists (still one column), AND at least eight inter-baseline gaps exceed the tightest by 6 pt or more (the authored block spacing) |
| 6 | `pdf-single-column-intra-block-spaced` | (b) single-column chronological — the arm that exhibits an intra-ENTRY paragraph gap | pdf | pdf-single-column-spaced | no | no vertical gutter of 15 pt or more exists (still one column), at least eight inter-baseline gaps exceed the tightest by 6 pt or more, AND one of those gaps falls INSIDE an employment (between its period line and its description line) — the distinction no other case can make |
| 7 | `pdf-single-column-intra-block-spaced-tight-list` | (b) single-column chronological — the second knob, isolated from the first | pdf | pdf-single-column-intra-block-spaced | no | the same form as its predecessor, with a longer tightly-leaded list — so the two knobs the withdrawn boundary rule failed on are separated into two measured rows instead of asserted together in prose |
| 8 | `pdf-sidebar-spaced` | (a) two-column/sidebar — the SPACED arm; carries a LIMIT, not a fix | pdf | pdf-sidebar-emitted-first | no | a vertical gutter of at least 15 pt exists (still two columns), AND at least eight inter-baseline gaps exceed the tightest by 6 pt or more — so any failure to recover entries here is NOT the document withholding the boundary |
| 9 | `pdf-single-column-en` | (e) English headings — answered as a RECOGNITION class, not a layout class | pdf | — | yes | no vertical gutter of 15 pt or more exists |
| 10 | `pdf-nonsequential-decorative` | (d) Canva-style — answered PARTIALLY, as a mechanic; no vendor export was measured | pdf | — | yes | the identity block sits in the top quarter of the text area although it is emitted last |
| 11 | `pdf-headingless` | (f) headingless — answered | pdf | — | yes | no vertical gutter of 15 pt or more exists |
| 12 | `pdf-unknown-heading-after-profile` | pin P7 — position is load-bearing | pdf | — | yes | no vertical gutter of 15 pt or more exists |
| 13 | `pdf-known-heading-after-profile` | pin P7 control | pdf | pdf-unknown-heading-after-profile | yes | no vertical gutter of 15 pt or more exists |
| 14 | `pdf-decorated-heading-glue` | recognition axis — its falsifier is a SOURCE edit where P7's is a DATA edit | pdf | — | no | no vertical gutter of 15 pt or more exists |
| 15 | `pdf-two-page-seam` | extraction axis — covers PdfPigOpenXmlCvTextExtractor.cs:118, half the cited defect | pdf | — | no | the document has exactly 2 physical pages |
| 16 | `pdf-pnr-bearing` | gate axis — a personnummer in the CV BODY, which blocks at the parse-level rung | pdf | — | no | no vertical gutter of 15 pt or more exists |
| 17 | `pdf-clean-body-pnr-in-account-name` | gate axis — the only route to the DQ6 rung on the composed DTO | pdf | pdf-single-column-sv | no | no vertical gutter of 15 pt or more exists |
| 18 | `docx-table-label-first-no-blanks` | (c) table-based Word template — answered as a CONTAINER fact; table-ness is invisible | docx | — | yes | the package contains a w:tbl and no self-closing w:p |
| 19 | `docx-flat-label-first-no-blanks` | (c) table-based Word template — the twin that proves table-ness is invisible | docx | docx-table-label-first-no-blanks | no | the package contains no w:tbl |
| 20 | `docx-table-label-first-with-blanks` | (c) table-based Word template — one-variable step | docx | docx-table-label-first-no-blanks | no | blank paragraphs use Word's <w:p><w:pPr /></w:p> form, never the self-closing <w:p /> |
| 21 | `docx-role-first-with-blanks` | (c) table-based Word template — the arm that exonerates the segmenter | docx | docx-table-label-first-with-blanks | yes | blank paragraphs use Word's <w:p><w:pPr /></w:p> form |

**Mechanics**

- `pdf-sidebar-emitted-first` — two geometric columns, emitted column-sequentially (sidebar block before main block)
- `pdf-interleaved-baseline-fusion` — row-interleaved two-column generator: sidebar and main cells share every baseline
- `pdf-zero-xgap-concat` — right-aligned period cell abutting a left-aligned company cell, zero padding
- `pdf-single-column-sv` — single-column chronological, blocks in document order
- `pdf-single-column-spaced` — single-column chronological, authored as blocks with paragraph spacing between them (the way a word processor lays a CV out)
- `pdf-single-column-intra-block-spaced` — single-column chronological, paragraph spacing between AND inside employments
- `pdf-single-column-intra-block-spaced-tight-list` — the same, with the skills list lengthened so bare leading is the page's MEDIAN gap
- `pdf-sidebar-spaced` — two geometric columns emitted column-sequentially, WITH paragraph spacing between blocks — the shape the real CV in #1060 was measured to have
- `pdf-single-column-en` — single-column chronological, English heading vocabulary (same renderer, same order)
- `pdf-nonsequential-decorative` — decorative layered page: watermark text in the stream, identity block emitted LAST while positioned at the page top
- `pdf-headingless` — no headings at all — the HONEST-FAILURE control
- `pdf-unknown-heading-after-profile` — a heading the lexicon does not know, placed IMMEDIATELY AFTER the profile block
- `pdf-known-heading-after-profile` — the same slot with the KNOWN synonym — the paired control for P7
- `pdf-decorated-heading-glue` — a known heading defeated by decorative glue (a leading bullet)
- `pdf-two-page-seam` — a page break MID-EXPERIENCE — the only case touching the page-seam newline
- `pdf-pnr-bearing` — single column carrying a synthetic personnummer in the contact block
- `pdf-clean-body-pnr-in-account-name` — a CLEAN CV body whose ACCOUNT display name carries a synthetic personnummer
- `docx-table-label-first-no-blanks` — Word table, period cell before role cell, no blank paragraphs
- `docx-flat-label-first-no-blanks` — identical content and order with NO table — the table-invisibility probe
- `docx-table-label-first-with-blanks` — the same table body with Word's own blank-paragraph form added — isolates BLANK LINES
- `docx-role-first-with-blanks` — blank paragraphs AND role-first header lines — the PROMOTE-level control

## 2. Fidelity verdict

`With period` counts PROMOTED experiences carrying a non-blank `RawPeriod`, and nothing
else. It was once called `Well-formed` and also tested Role and Company — both REQUIRED by
`Resume.ValidateContent`, so on a promoted row they are true by invariant and the count was
period-presence wearing a validity name. It has equalled `Promoted exp` in every baseline
published so far: no fixture yet distinguishes them, which is a fact about the fixtures.

| # | Case | Verdict | GT emp | Parsed exp | Promoted exp | With period | GT edu | Parsed edu | Promoted edu | First blocking gate |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `pdf-sidebar-emitted-first` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 2 | `pdf-interleaved-baseline-fusion` | **PromotedLossy** | 5 | 0 | 0 | 0 | 3 | 1 | 1 | — |
| 3 | `pdf-zero-xgap-concat` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 4 | `pdf-single-column-sv` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 5 | `pdf-single-column-spaced` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 6 | `pdf-single-column-intra-block-spaced` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 7 | `pdf-single-column-intra-block-spaced-tight-list` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 8 | `pdf-sidebar-spaced` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 9 | `pdf-single-column-en` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 10 | `pdf-nonsequential-decorative` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 11 | `pdf-headingless` | **PromotedLossy** | 5 | 0 | 0 | 0 | 3 | 0 | 0 | — |
| 12 | `pdf-unknown-heading-after-profile` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 13 | `pdf-known-heading-after-profile` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 14 | `pdf-decorated-heading-glue` | **PromotedLossy** | 5 | 0 | 0 | 0 | 3 | 1 | 1 | — |
| 15 | `pdf-two-page-seam` | **PromotedLossy** | 5 | 1 | 1 | 1 | 3 | 1 | 1 | — |
| 16 | `pdf-pnr-bearing` | **Blocked** | 5 | 1 | — | — | 3 | 1 | — | PersonnummerPresent |
| 17 | `pdf-clean-body-pnr-in-account-name` | **Blocked** | 5 | 1 | — | — | 3 | 1 | — | PersonnummerInAccountName |
| 18 | `docx-table-label-first-no-blanks` | **Blocked** | 5 | 1 | — | — | 3 | 1 | — | IncompleteContent |
| 19 | `docx-flat-label-first-no-blanks` | **Blocked** | 5 | 1 | — | — | 3 | 1 | — | IncompleteContent |
| 20 | `docx-table-label-first-with-blanks` | **Blocked** | 5 | 5 | — | — | 3 | 3 | — | IncompleteContent |
| 21 | `docx-role-first-with-blanks` | **PromotedFaithful** | 5 | 5 | 5 | 5 | 3 | 3 | 3 | — |

## 3. Marker trace

One row per authored employment and education. A count says five became one; this says
WHICH four vanished and where each was last seen. `RetainedButOrphaned` on a promoted
row is the finding: the product said the CV was saved and this employment is gone.

| Case | Kind | Marker | In bytes | In parsed artifact | In promoted section | Found in other section | Verdict |
|---|---|---|---|---|---|---|---|
| `pdf-sidebar-emitted-first` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-sidebar-emitted-first` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-emitted-first` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-emitted-first` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-emitted-first` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-emitted-first` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-sidebar-emitted-first` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-emitted-first` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Employment | Klarna AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-interleaved-baseline-fusion` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-interleaved-baseline-fusion` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Employment | Klarna AB | yes | yes | yes | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-zero-xgap-concat` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-zero-xgap-concat` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-single-column-sv` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-single-column-sv` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-sv` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-single-column-spaced` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-single-column-spaced` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-spaced` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-single-column-intra-block-spaced` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-single-column-intra-block-spaced` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-single-column-intra-block-spaced-tight-list` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-single-column-intra-block-spaced-tight-list` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-intra-block-spaced-tight-list` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-sidebar-spaced` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-sidebar-spaced` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-sidebar-spaced` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-single-column-en` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Employment | Region Vastra Gotaland | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Education | Chalmers University of Technology | yes | yes | yes | — | **Survived** |
| `pdf-single-column-en` | Education | University of Gothenburg | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-single-column-en` | Education | Hvitfeldtska Upper Secondary | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-nonsequential-decorative` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-nonsequential-decorative` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-nonsequential-decorative` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Employment | Klarna AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-headingless` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-unknown-heading-after-profile` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-unknown-heading-after-profile` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-unknown-heading-after-profile` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-known-heading-after-profile` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-known-heading-after-profile` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-known-heading-after-profile` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-decorated-heading-glue` | Employment | Klarna AB | yes | yes | no | Summary | **AbsorbedIntoOtherSection** |
| `pdf-decorated-heading-glue` | Employment | Volvo Cars | yes | yes | no | Summary | **AbsorbedIntoOtherSection** |
| `pdf-decorated-heading-glue` | Employment | Västra Götalandsregionen | yes | yes | no | Summary | **AbsorbedIntoOtherSection** |
| `pdf-decorated-heading-glue` | Employment | Consid AB | yes | yes | no | Summary | **AbsorbedIntoOtherSection** |
| `pdf-decorated-heading-glue` | Employment | Sigma IT | yes | yes | no | Summary | **AbsorbedIntoOtherSection** |
| `pdf-decorated-heading-glue` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-decorated-heading-glue` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-decorated-heading-glue` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `pdf-two-page-seam` | Employment | Volvo Cars | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Employment | Consid AB | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Employment | Sigma IT | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `pdf-two-page-seam` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-two-page-seam` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedButOrphaned** |
| `pdf-pnr-bearing` | Employment | Klarna AB | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Employment | Volvo Cars | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Employment | Consid AB | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Employment | Sigma IT | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-pnr-bearing` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Employment | Klarna AB | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Employment | Volvo Cars | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Employment | Consid AB | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Employment | Sigma IT | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedNotPromoted** |
| `pdf-clean-body-pnr-in-account-name` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Employment | Klarna AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Employment | Volvo Cars | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Employment | Consid AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Employment | Sigma IT | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-no-blanks` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Employment | Klarna AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Employment | Volvo Cars | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Employment | Consid AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Employment | Sigma IT | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-flat-label-first-no-blanks` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Employment | Klarna AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Employment | Volvo Cars | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Employment | Västra Götalandsregionen | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Employment | Consid AB | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Employment | Sigma IT | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Education | Chalmers tekniska högskola | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Education | Göteborgs universitet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-table-label-first-with-blanks` | Education | Hvitfeldtska gymnasiet | yes | yes | no | — | **RetainedNotPromoted** |
| `docx-role-first-with-blanks` | Employment | Klarna AB | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Employment | Volvo Cars | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Employment | Västra Götalandsregionen | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Employment | Consid AB | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Employment | Sigma IT | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Education | Chalmers tekniska högskola | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Education | Göteborgs universitet | yes | yes | yes | — | **Survived** |
| `docx-role-first-with-blanks` | Education | Hvitfeldtska gymnasiet | yes | yes | yes | — | **Survived** |

## 4. Extraction and form

| # | Case | Kind resolved | Status | Chars | Lines | BLANK LINES | Segment ran | Language | Headings | Preamble |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `pdf-sidebar-emitted-first` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 2 | `pdf-interleaved-baseline-fusion` | yes | Extracted | 1269 | 28 | **0** | yes | Sv | 1 | 1055 chars |
| 3 | `pdf-zero-xgap-concat` | yes | Extracted | 844 | 22 | **0** | yes | Sv | 2 | null |
| 4 | `pdf-single-column-sv` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 5 | `pdf-single-column-spaced` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 6 | `pdf-single-column-intra-block-spaced` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 7 | `pdf-single-column-intra-block-spaced-tight-list` | yes | Extracted | 1680 | 62 | **0** | yes | Sv | 5 | null |
| 8 | `pdf-sidebar-spaced` | yes | Extracted | 1653 | 59 | **0** | yes | Sv | 5 | null |
| 9 | `pdf-single-column-en` | yes | Extracted | 1557 | 48 | **0** | yes | En | 5 | null |
| 10 | `pdf-nonsequential-decorative` | yes | Extracted | 1537 | 49 | **0** | yes | Sv | 5 | 7 chars |
| 11 | `pdf-headingless` | yes | Extracted | 1112 | 26 | **0** | yes | Sv | 0 | 1047 chars |
| 12 | `pdf-unknown-heading-after-profile` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 13 | `pdf-known-heading-after-profile` | yes | Extracted | 1521 | 48 | **0** | yes | Sv | 6 | null |
| 14 | `pdf-decorated-heading-glue` | yes | Extracted | 1225 | 40 | **0** | yes | Sv | 3 | null |
| 15 | `pdf-two-page-seam` | yes | Extracted | 1223 | 40 | **0** | yes | Sv | 4 | null |
| 16 | `pdf-pnr-bearing` | yes | Extracted | 1235 | 41 | **0** | yes | Sv | 4 | 11 chars |
| 17 | `pdf-clean-body-pnr-in-account-name` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 18 | `docx-table-label-first-no-blanks` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 19 | `docx-flat-label-first-no-blanks` | yes | Extracted | 1529 | 48 | **0** | yes | Sv | 5 | null |
| 20 | `docx-table-label-first-with-blanks` | yes | Extracted | 1543 | 62 | **14** | yes | Sv | 5 | null |
| 21 | `docx-role-first-with-blanks` | yes | Extracted | 1543 | 62 | **14** | yes | Sv | 5 | null |

### 4b. Product-side observables

Each row above pairs a byte proof (what the AUTHORED document is) with what the product
actually emitted. A byte proof alone restates the generator; an observable alone does not
say which shape produced it. The digest is a per-case value — two rows sharing one is a
reader's inference, never an emitted ratio.

| # | Case | Text digest | Fused period+role line | A line carries both columns | First extracted line |
|---|---|---|---|---|---|
| 1 | `pdf-sidebar-emitted-first` | `F8D2FF82DFDE` | no | no | `Anna Andersson` |
| 2 | `pdf-interleaved-baseline-fusion` | `5921426B6D89` | no | yes | `Anna Andersson PROFIL` |
| 3 | `pdf-zero-xgap-concat` | `4729D90AC94E` | yes | no | `Anna Andersson` |
| 4 | `pdf-single-column-sv` | `05CD8018BF8A` | no | no | `Anna Andersson` |
| 5 | `pdf-single-column-spaced` | `05CD8018BF8A` | no | no | `Anna Andersson` |
| 6 | `pdf-single-column-intra-block-spaced` | `05CD8018BF8A` | no | no | `Anna Andersson` |
| 7 | `pdf-single-column-intra-block-spaced-tight-list` | `F2BBB87DDE72` | no | no | `Anna Andersson` |
| 8 | `pdf-sidebar-spaced` | `F4AE38C36604` | no | no | `Anna Andersson` |
| 9 | `pdf-single-column-en` | `1EF60B042871` | no | no | `Anna Andersson` |
| 10 | `pdf-nonsequential-decorative` | `B25148E1CC6B` | no | no | `CV 2026` |
| 11 | `pdf-headingless` | `6AA911571D21` | no | no | `Anna Andersson` |
| 12 | `pdf-unknown-heading-after-profile` | `4BA9EB7A1A94` | no | no | `Anna Andersson` |
| 13 | `pdf-known-heading-after-profile` | `151E7C68EC39` | no | no | `Anna Andersson` |
| 14 | `pdf-decorated-heading-glue` | `E8752B1B7FE7` | no | no | `Anna Andersson` |
| 15 | `pdf-two-page-seam` | `58436A6451A4` | no | no | `Anna Andersson` |
| 16 | `pdf-pnr-bearing` | `EBB3668C7BA1` | no | no | `Anna Andersson` |
| 17 | `pdf-clean-body-pnr-in-account-name` | `05CD8018BF8A` | no | no | `Anna Andersson` |
| 18 | `docx-table-label-first-no-blanks` | `1F86611223AB` | no | no | `Anna Andersson` |
| 19 | `docx-flat-label-first-no-blanks` | `1F86611223AB` | no | no | `Anna Andersson` |
| 20 | `docx-table-label-first-with-blanks` | `DCF6058705F8` | no | no | `Anna Andersson` |
| 21 | `docx-role-first-with-blanks` | `9858965A707E` | no | no | `Anna Andersson` |

**Twin comparisons** — the only honest sentence this corpus can emit about tables. The
DOCX extractor handles `w:t` and `w:p` only, with no `w:tbl`/`w:tr`/`w:tc` handling, so a
table and a flat paragraph sequence in the same order should produce identical text. An
ordering assertion would restate our own writer; equal digests are a fact about the
extractor.

- `pdf-single-column-spaced` vs `pdf-single-column-sv` — digests **EQUAL** (`05CD8018BF8A` / `05CD8018BF8A`)
- `pdf-single-column-intra-block-spaced` vs `pdf-single-column-spaced` — digests **EQUAL** (`05CD8018BF8A` / `05CD8018BF8A`)
- `pdf-single-column-intra-block-spaced-tight-list` vs `pdf-single-column-intra-block-spaced` — digests differ (`F2BBB87DDE72` / `05CD8018BF8A`)
- `pdf-sidebar-spaced` vs `pdf-sidebar-emitted-first` — digests differ (`F4AE38C36604` / `F8D2FF82DFDE`)
- `pdf-known-heading-after-profile` vs `pdf-unknown-heading-after-profile` — digests differ (`151E7C68EC39` / `4BA9EB7A1A94`)
- `pdf-clean-body-pnr-in-account-name` vs `pdf-single-column-sv` — digests **EQUAL** (`05CD8018BF8A` / `05CD8018BF8A`)
- `docx-flat-label-first-no-blanks` vs `docx-table-label-first-no-blanks` — digests **EQUAL** (`1F86611223AB` / `1F86611223AB`)
- `docx-table-label-first-with-blanks` vs `docx-table-label-first-no-blanks` — digests differ (`DCF6058705F8` / `1F86611223AB`)
- `docx-role-first-with-blanks` vs `docx-table-label-first-with-blanks` — digests differ (`9858965A707E` / `DCF6058705F8`)

## 5. Gate ladder

No predicate expression is re-typed anywhere in this corpus; the states are derived from
what the real handler returned. **TWO** predicates still collapse onto one
`PersonnummerPresent` token, and each is settled by its own POSITIVE discriminator — the
aggregate's own flag for the parse rung, the two PUBLIC calls the handler makes for the
label rung. The DQ6 guard is no longer among them: #1060 PR C gave it its own
`PersonnummerInAccountName` token, so that rung is reached by name.

Earlier revisions said these were resolved "by ELIMINATION — whatever remains IS the DQ6
guard, there is no fourth site". That reasoning was sound only while the site list was
known complete, and PR C is the measured proof that such knowledge expires. Nothing is
inferred from a remainder now; what falls past both guards is reported as `unresolved`.

A gate cell renders one of six words. `passed` and `**BLOCKED**` say what they say; the
FOUR below are the confusable ones, and conflating two of them is what this section was
corrected for (2026-07-28):

- `not evaluated` — an earlier GATE stopped control, so this rung was never asked.
- `no verdict` — the handler returned a genuine FAULT, so no gate decided anything.
- `unresolved` — THE INSTRUMENT has no arm for the token the handler returned. It is an
  integrity failure, listed in §0 and red in the suite, never a statement about the
  product. Before it existed, this case rendered as `no verdict` — publishing an honest
  block as a handler fault, on the one case that exercises the DQ6 rung.
- `—` — no ladder exists at all: the case CRASHED before any gate was reached, so there is
  nothing for the rungs to report. §0 names it. Distinct from `no verdict`, which is a
  statement about the handler; here the handler was never asked.

`Domain code` is the constraint `Resume.CreateFromParsed` refused on, carried verbatim
out of the buildability rung and read off the handler's own `BlockDetail` log property
(#1060 D3(β) PR 2). It is what makes `**BLOCKED**` on that rung legible: the token
`IncompleteContent` covers every code `Resume.CreateFromParsed` can return: thirty-two
declared by `Resume.ValidateContent`, plus `JobSeekerIdRequired` and `ValidateName`'s
three, so thirty-six. They do not share a fix — a per-entry failure like
`Resume.ExperienceCompanyRequired` is routable, while a whole-document one like
`Resume.SummaryTooLong` is not, and a design that assumed the first would spend a
Domain refactor against a failure it cannot touch.

Read `—` in that column as **"no Domain refusal produced a code on this row"**, never
as "no constraint failed": a personnummer block and a promote both print it, and
neither asked the Domain the question. A row whose code could not be READ prints
`INSTRUMENT: unreadable` instead and is named in §0 — the two are never one em-dash.

| # | Case | G1 pnr(parse) (pnr on parse) | G2 confidence (confidence) | G2b pnr(label) (pnr in label) | G3a pnr(DQ6) (pnr DQ6) | G3b buildability (buildability) | FIRST BLOCK | Domain code | Promote fault | Promoted |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `pdf-sidebar-emitted-first` | passed | passed | passed | passed | passed | — | — | — | yes |
| 2 | `pdf-interleaved-baseline-fusion` | passed | passed | passed | passed | passed | — | — | — | yes |
| 3 | `pdf-zero-xgap-concat` | passed | passed | passed | passed | passed | — | — | — | yes |
| 4 | `pdf-single-column-sv` | passed | passed | passed | passed | passed | — | — | — | yes |
| 5 | `pdf-single-column-spaced` | passed | passed | passed | passed | passed | — | — | — | yes |
| 6 | `pdf-single-column-intra-block-spaced` | passed | passed | passed | passed | passed | — | — | — | yes |
| 7 | `pdf-single-column-intra-block-spaced-tight-list` | passed | passed | passed | passed | passed | — | — | — | yes |
| 8 | `pdf-sidebar-spaced` | passed | passed | passed | passed | passed | — | — | — | yes |
| 9 | `pdf-single-column-en` | passed | passed | passed | passed | passed | — | — | — | yes |
| 10 | `pdf-nonsequential-decorative` | passed | passed | passed | passed | passed | — | — | — | yes |
| 11 | `pdf-headingless` | passed | passed | passed | passed | passed | — | — | — | yes |
| 12 | `pdf-unknown-heading-after-profile` | passed | passed | passed | passed | passed | — | — | — | yes |
| 13 | `pdf-known-heading-after-profile` | passed | passed | passed | passed | passed | — | — | — | yes |
| 14 | `pdf-decorated-heading-glue` | passed | passed | passed | passed | passed | — | — | — | yes |
| 15 | `pdf-two-page-seam` | passed | passed | passed | passed | passed | — | — | — | yes |
| 16 | `pdf-pnr-bearing` | **BLOCKED** | not evaluated | not evaluated | not evaluated | not evaluated | PersonnummerPresent | — | — | no |
| 17 | `pdf-clean-body-pnr-in-account-name` | passed | passed | passed | **BLOCKED** | not evaluated | PersonnummerInAccountName | — | — | no |
| 18 | `docx-table-label-first-no-blanks` | passed | passed | passed | passed | **BLOCKED** | IncompleteContent | `Resume.ExperienceRoleRequired` | — | no |
| 19 | `docx-flat-label-first-no-blanks` | passed | passed | passed | passed | **BLOCKED** | IncompleteContent | `Resume.ExperienceRoleRequired` | — | no |
| 20 | `docx-table-label-first-with-blanks` | passed | passed | passed | passed | **BLOCKED** | IncompleteContent | `Resume.ExperienceRoleRequired` | — | no |
| 21 | `docx-role-first-with-blanks` | passed | passed | passed | passed | passed | — | — | — | yes |

**Observed Domain state** (this is aggregate state, NOT a gate verdict). The personnummer
column prints the AUTHORED declaration and the OBSERVED aggregate flag side by side: if
extraction ever loses an authored personnummer, that divergence is itself the finding, and
a column printing only the declaration would hide it behind the very content loss this
corpus measures. The value itself is never printed.

| Case | Confidence overall | Preamble on parse | Preamble ON THE PROMOTED CV | pnr authored (body / account) | pnr OBSERVED on parse |
|---|---|---|---|---|---|
| `pdf-sidebar-emitted-first` | Confident | no | no | none | no |
| `pdf-interleaved-baseline-fusion` | Confident | yes | yes | none | no |
| `pdf-zero-xgap-concat` | Confident | no | no | none | no |
| `pdf-single-column-sv` | Confident | no | no | none | no |
| `pdf-single-column-spaced` | Confident | no | no | none | no |
| `pdf-single-column-intra-block-spaced` | Confident | no | no | none | no |
| `pdf-single-column-intra-block-spaced-tight-list` | Confident | no | no | none | no |
| `pdf-sidebar-spaced` | Confident | no | no | none | no |
| `pdf-single-column-en` | Confident | no | no | none | no |
| `pdf-nonsequential-decorative` | Degraded | yes | yes | none | no |
| `pdf-headingless` | Degraded | yes | yes | none | no |
| `pdf-unknown-heading-after-profile` | Confident | no | no | none | no |
| `pdf-known-heading-after-profile` | Confident | no | no | none | no |
| `pdf-decorated-heading-glue` | Confident | no | no | none | no |
| `pdf-two-page-seam` | Confident | no | no | none | no |
| `pdf-pnr-bearing` | Confident | yes | — | body (synthetic, not printed) | yes |
| `pdf-clean-body-pnr-in-account-name` | Confident | no | — | account name (synthetic, not printed) | no |
| `docx-table-label-first-no-blanks` | Confident | no | — | none | no |
| `docx-flat-label-first-no-blanks` | Confident | no | — | none | no |
| `docx-table-label-first-with-blanks` | Confident | no | — | none | no |
| `docx-role-first-with-blanks` | Confident | no | no | none | no |

## 6. Section confidence, verbatim

The segmenter's own evidence strings, quoted exactly and never paraphrased, with the
authored ground truth beside them. `Confident — heading matched, 1 entries` next to
`ground truth: 5 employments` IS the finding.

**`pdf-sidebar-emitted-first`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 2 entries`

**`pdf-interleaved-baseline-fusion`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: NotFound — no heading detected; 21 unclassified line(s) carried from above the first heading`
- `Experience: NotFound — no heading detected`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: NotFound — no heading detected`
- `Languages: NotFound — no heading detected`

**`pdf-zero-xgap-concat`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: NotFound — no heading detected; text dropped from 1 line(s) as contact-block material`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: NotFound — no heading detected`
- `Languages: NotFound — no heading detected`

**`pdf-single-column-sv`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`pdf-single-column-spaced`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`pdf-single-column-intra-block-spaced`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`pdf-single-column-intra-block-spaced-tight-list`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 21 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`pdf-sidebar-spaced`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 14 entries`
- `Languages: Confident — heading 'språk' matched; 10 entries`

**`pdf-single-column-en`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'summary' matched; summary text present`
- `Experience: Confident — heading 'experience' matched; 1 entries`
- `Education: Confident — heading 'education' matched; 1 entries`
- `Skills: Confident — heading 'skills' matched; 7 entries`
- `Languages: Confident — heading 'languages' matched; 8 entries`

**`pdf-nonsequential-decorative`** — ground truth: 5 employments, 3 educations

- `Contact: Degraded — email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 12 entries`

**`pdf-headingless`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: NotFound — no heading detected; 22 unclassified line(s) carried from above the first heading; text dropped from 1 line(s) as contact-block material`
- `Experience: NotFound — no heading detected`
- `Education: NotFound — no heading detected`
- `Skills: NotFound — no heading detected`
- `Languages: NotFound — no heading detected`

**`pdf-unknown-heading-after-profile`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 2 entries`

**`pdf-known-heading-after-profile`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 2 entries`

**`pdf-decorated-heading-glue`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: NotFound — no heading detected`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: NotFound — no heading detected`

**`pdf-two-page-seam`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: NotFound — no heading detected`

**`pdf-pnr-bearing`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: NotFound — no heading detected`

**`pdf-clean-body-pnr-in-account-name`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`docx-table-label-first-no-blanks`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`docx-flat-label-first-no-blanks`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 1 entries`
- `Education: Confident — heading 'utbildning' matched; 1 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`docx-table-label-first-with-blanks`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 5 entries`
- `Education: Confident — heading 'utbildning' matched; 3 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

**`docx-role-first-with-blanks`** — ground truth: 5 employments, 3 educations

- `Contact: Confident — name extracted; email extracted; phone extracted`
- `Profile: Confident — heading 'profil' matched; summary text present`
- `Experience: Confident — heading 'arbetslivserfarenhet' matched; 5 entries`
- `Education: Confident — heading 'utbildning' matched; 3 entries`
- `Skills: Confident — heading 'tekniska kompetenser' matched; 7 entries`
- `Languages: Confident — heading 'språk' matched; 8 entries`

## 7. Cross-section contamination

An authored string turning up in a section that is not its declared home. Measured as
membership of the corpus's own declarations, never by re-typing what counts as a language,
and only against the project heading the case ACTUALLY rendered. This SURVIVES the
blank-line fix — it is present in the correct-count control arm — so a reader must not
read a zero content-loss delta as "clean".

Precision limit, stated: an entry that is a proper fragment of an authored project line is
reported AS a fragment (the list parser atomises prose on commas). An entry that is neither
an authored line nor a fragment of one is invisible here — this sweep can under-report, and
it never over-reports.

| Case | Receiving section ← foreign string |
|---|---|
| `pdf-sidebar-emitted-first` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-single-column-sv` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-single-column-sv` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-single-column-sv` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-sv` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-sv` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-single-column-sv` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-single-column-sv` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-single-column-spaced` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-single-column-spaced` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-single-column-spaced` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-spaced` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-spaced` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-single-column-spaced` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-single-column-spaced` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced-tight-list` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-single-column-intra-block-spaced-tight-list` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-sidebar-spaced` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-sidebar-spaced` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-sidebar-spaced` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-sidebar-spaced` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-sidebar-spaced` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-sidebar-spaced` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-sidebar-spaced` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-sidebar-spaced` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-single-column-en` | Languages ← 'SELECTED PROJECTS (SHORTLIST)' (declared home: projects) |
| `pdf-single-column-en` | Languages ← 'Jobbliggaren - a deterministic CV reviewer i…' (declared home: projects) |
| `pdf-single-column-en` | Languages ← 'Kartkollen - open data on municipal decision…' — a FRAGMENT of the authored project line 'Kartkollen - open data on municipal decision…' (the list parser atomised it) |
| `pdf-single-column-en` | Languages ← 'built on PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - open data on municipal decision…' (the list parser atomised it) |
| `pdf-single-column-en` | Languages ← 'Turlistan - a public transport journey plann…' (declared home: projects) |
| `pdf-single-column-en` | Languages ← 'Bokhyllan - a catalogue service for public l…' (declared home: projects) |
| `pdf-single-column-en` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - a deterministic CV reviewer i…' (the list parser atomised it) |
| `pdf-nonsequential-decorative` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-nonsequential-decorative` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-nonsequential-decorative` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-nonsequential-decorative` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-nonsequential-decorative` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-nonsequential-decorative` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-nonsequential-decorative` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-unknown-heading-after-profile` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-known-heading-after-profile` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-decorated-heading-glue` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-two-page-seam` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-pnr-bearing` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `pdf-clean-body-pnr-in-account-name` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `pdf-clean-body-pnr-in-account-name` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `docx-table-label-first-no-blanks` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `docx-table-label-first-no-blanks` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `docx-table-label-first-no-blanks` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-table-label-first-no-blanks` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-table-label-first-no-blanks` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `docx-table-label-first-no-blanks` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `docx-table-label-first-no-blanks` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `docx-flat-label-first-no-blanks` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `docx-flat-label-first-no-blanks` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `docx-flat-label-first-no-blanks` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-flat-label-first-no-blanks` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-flat-label-first-no-blanks` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `docx-flat-label-first-no-blanks` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `docx-flat-label-first-no-blanks` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `docx-table-label-first-with-blanks` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `docx-table-label-first-with-blanks` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `docx-table-label-first-with-blanks` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-table-label-first-with-blanks` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-table-label-first-with-blanks` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `docx-table-label-first-with-blanks` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `docx-table-label-first-with-blanks` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |
| `docx-role-first-with-blanks` | Languages ← 'PROJEKT (URVAL)' (declared home: projects) |
| `docx-role-first-with-blanks` | Languages ← 'Jobbliggaren - deterministisk CV-granskare i…' (declared home: projects) |
| `docx-role-first-with-blanks` | Languages ← 'Kartkollen - öppen data om kommunala beslut' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-role-first-with-blanks` | Languages ← 'byggd på PostGIS.' — a FRAGMENT of the authored project line 'Kartkollen - öppen data om kommunala beslut,…' (the list parser atomised it) |
| `docx-role-first-with-blanks` | Languages ← 'Turlistan - reseplanerare för kollektivtrafi…' (declared home: projects) |
| `docx-role-first-with-blanks` | Languages ← 'Bokhyllan - katalogtjänst för folkbiblioteke…' (declared home: projects) |
| `docx-role-first-with-blanks` | Skills ← '.NET' — a FRAGMENT of the authored project line 'Jobbliggaren - deterministisk CV-granskare i…' (the list parser atomised it) |

## 8. Pin P7 (unknown heading) and pin P5 (English non-difference)

P7 is a three-part predicate: the promoted Summary contains the unknown heading verbatim,
AND no parsed section is headed by it, AND the Summary stays under the 2000-char domain
cap (above it `Resume.ValidateContent` refuses on `SummaryTooLong` and the row would
silently measure `IncompleteContent` under P7's name). Position is load-bearing: measured
2026-07-26, the same heading after UTBILDNING is swallowed by Education and after SPRÅK
by the Languages list.

Each row is measured against the heading THAT case renders, not against a fixed constant:
the control renders the known synonym, so measuring it against the unknown one made both
its cells read "no" unconditionally — a control that cannot fall is not a control.

| Case | Heading rendered | In promoted Summary | Is its own parsed section | Parsed free sections | Summary chars |
|---|---|---|---|---|---|
| `pdf-unknown-heading-after-profile` | `PROJEKT (URVAL)` | yes | no | none | 548 |
| `pdf-known-heading-after-profile` | `PROJEKT` | no | yes | `PROJEKT` | 288 |

P5 compares the STRUCTURAL tuple of the Swedish and English single-column cases. Text-
bearing fields are excluded by name: they differ by construction between a document and
its translation and would drown a real regression in permanent noise. The one field
permitted to differ is the detected language.

| Field | Swedish | English |
|---|---|---|
| detected language (permitted to differ) | Sv | En |
| parsed experience | 1 | 1 |
| parsed education | 1 | 1 |
| headings detected | 5 | 5 |
| first blocking gate | — | — |
| promoted | yes | yes |
| fidelity verdict | PromotedLossy | PromotedLossy |

## 9. Known precision gaps and deliberate omissions

- **No genuine vendor export.** The CTO's class (d) is answered PARTIALLY: the mechanic
  is reproduced, the vendor claim is not made.
- **Table-ness is invisible to the DOCX extractor** (it handles `w:t` and `w:p` only —
  no `w:tbl`/`w:tr`/`w:tc`), so class (c) is answered as a container fact with its
  invisibility shipped as a measurement, not as a distinct extraction mechanic.
- **Scanned / `NoTextLayer` documents are absent**, so the `ParseConfidence.Failed`
  branch of the import handler's segment conditional is unexercised.
- **Kerning-driven word splits, ligature artefacts, rotated text and foreign-producer
  ToUnicode tables are absent.** Every PDF here round-trips through a QuestPDF-embedded
  subset font and its own CMap — a real mechanism, but QuestPDF's.
- **The `addDoubleNewline: true` counterfactual is NOT reproduced by this suite.** Doing
  it faithfully would mean hand-copying the extractor's whole page-assembly loop (the
  page cap, the char budget with its truncate-and-break arm, the seam, `Normalize`) —
  exactly the re-typed-production defect this corpus exists to avoid. The measurement
  lives in the committed baseline's appendix.
- **No case is asserted.** Every falsifier named in the PR body produces a silently
  different artifact and a green build. This file helps only a reader who diffs it
  against the committed baseline. That is what observe-only means.

## Appendix — measurements this suite deliberately does NOT reproduce

Measured by throwaway session spikes over the same real extractor and segmenter, and recorded here
because the numbers bear on the entry-boundary work while reproducing them inside this suite would
mean hand-copying the extractor's whole page-assembly loop — exactly the re-typed-production defect
this corpus exists to avoid.

### A. `addDoubleNewline: true` is not the fix (measured 2026-07-26, PR K)

| Form | blank lines | parsed experience (ground truth 5) | parsed education | confidence |
|---|---|---|---|---|
| two-column sidebar, flag `false` (production today) | 0 | 1 | 1 | Confident |
| two-column sidebar, flag `true` | 42 | **15** | 7 | Confident |
| single-column, flag `false` (production today) | 0 | 1 | 1 | Confident |
| single-column, flag `true` | 35 | **15** | 4 | Confident |

The flag inserts a blank line after EVERY visual line, so one employment explodes into three
entries (header / bare period / bullet); one produced entry was literally
`title=<null> org=<null> period="2021 - 2026"`. It replaces under-splitting (5 to 1) with
over-splitting (5 to 15), and the confidence surface reports Confident in BOTH directions.

**CORRECTION (2026-07-27, PR E).** PR K's session read these numbers as refuting the asymmetry
argument the flag's bind rested on — that over-splitting yields a malformed entry which fails
`ExperienceCompanyRequired` and therefore an honest `IncompleteContent` block — and wrote here that
"it did not" happen. **That reading was wrong, and this corpus contains its own counter-example.**
`AutoPromoteContentMapper` never filters entries (its docblock says so), so a `title=<null>
org=<null>` fragment projects as `Company: ""`; `Resume.ValidateContent` iterates experiences and
returns on the FIRST blank Company. One malformed fragment out of fifteen fails the aggregate.
The row `docx-table-label-first-with-blanks` is the measured proof: correct counts (5 and 3) and
still `Blocked` on `IncompleteContent`. The block does occur.

**SECOND CORRECTION (2026-08-01, #1060 D3(β) PR 2) — the citation above is true of its evidence
and wrong about its subject, and the new `Domain code` column is what measured it.** Row
`docx-table-label-first-with-blanks` blocks on **`Resume.ExperienceRoleRequired`**, not on
`ExperienceCompanyRequired`. So it does prove the sentence it was cited for — an
`IncompleteContent` block occurs with correct entry counts — and it does **not** prove the blank-
COMPANY mechanism the paragraph is arguing, because on that row the Company is present and the
Role is what is missing. The `addDoubleNewline` spike's own fifteen-fragment case may still fail
on Company; that is the spike, and it was never re-run through this instrument, so it stays
unmeasured rather than being read off a row that turned out to fail somewhere else.

All three rows fall on the same code, which supports TWO invariances — and the first version of
this paragraph cited the wrong pair for one of them. Rows 18 and 19
(`docx-table-label-first-no-blanks`, `docx-flat-label-first-no-blanks`) differ on **table versus
flat**, so their agreement says the arm does not depend on the container shape. The **blank-line**
variable is the one-variable step between rows **18 and 20**
(`docx-table-label-first-with-blanks`), and their agreement is what says the arm does not depend
on the entry counts being right — row 20 parses 5/5 and 3/3, row 18 parses 1.

**What actually disqualified the flag** is that it makes the block UNIVERSAL. Every PDF employment
whose period sits on its own line yields such a fragment, so every row that promotes today would
stop promoting — an honest block is the better outcome for ONE document, not a policy for a whole
container format. The flag is also a LINE signal where `SplitEntries` needs a PARAGRAPH one.

### B. The corpus authored no paragraph spacing at all (measured 2026-07-27, PR E)

Every PDF case that existed before PR E emits **exactly one inter-baseline gap value — 12.0 pt —
on every page**, measured over the rendered bytes of all thirteen:

```
pdf-sidebar-emitted-first p1: gaps: 12    pdf-single-column-sv  p1: gaps: 12
pdf-interleaved-baseline-fusion p1: 12    pdf-single-column-en  p1: gaps: 12
pdf-zero-xgap-concat      p1: gaps: 12    pdf-headingless       p1: gaps: 12
pdf-unknown-heading-after-profile: 12     pdf-known-heading-after-profile: 12
pdf-decorated-heading-glue p1: gaps: 12   pdf-two-page-seam  p1/p2: gaps: 12
pdf-pnr-bearing           p1: gaps: 12    pdf-clean-body-pnr-in-account-name: 12
pdf-nonsequential-decorative p1: gaps: 12, 25, 53   (layered/offset, not paragraph spacing)
```

The cause was in the generator, not the extractor: `QuestPdfCvRenderer`'s `Identity`, `Section`,
`Experience`/`EmploymentLines` and `Education` emitted every line as a bare `col.Item().Text(...)`
with no padding and no column spacing anywhere.

**Why this mattered.** "Zero blank lines in the extracted text" had two independent candidate
causes — the extractor call suppressing a boundary, and the document never carrying one — and a
fixture set with no spacing variation could only ever observe the pair. PR K controlled the first
(the counterfactual in section A) and never the second. This is the same defect class PR K's own
design named F6 and fixed on the DOCX arm with the blank/no-blank twins; the PDF arm never received
its twin. `pdf-single-column-spaced` and `pdf-sidebar-spaced` are that twin.

### C. A geometry-derived boundary rule was built, measured, and WITHDRAWN (2026-07-27, PR E)

Recorded in full because the next attempt must not re-derive it. The rule inserted a blank line
where a page's inter-baseline gap exceeded **its own median gap plus half its median line height**;
both reference values read from the page, one authored number (0.5).

**It worked on the documents that existed.** `pdf-single-column-spaced` went `PromotedLossy` →
`PromotedFaithful`, experience 1 → 5, education 1 → 3, blank lines 0 → 18; all pre-existing rows
byte-identical; a 1.5x ratio form was measured wrong first (at 1.35 line spacing with 6 pt gaps the
body pitch is 14.9 pt and the paragraph gaps 20.8 pt, while 1.5x is 22.35 pt — it catches nothing).

**It was withdrawn because the fixture set could not exhibit the shape that breaks it.** Every
spaced case rendered an employment as ONE block, with spacing only BETWEEN blocks, so no fixture
carried a paragraph gap INSIDE an entry. Swept against a document that does — five employments as
`[role - company / period / bullet]`, 14 pt between employments, `intra` pt above the period and
bullet lines, and an N-line tightly-leaded skills list:

| intra pt | tight list lines | blank lines | parsed experience (GT 5) | fragments with null org | outcome |
|---|---|---|---|---|---|
| 0 / 4 / 6 / 8 | 10 | 7-9 | 5 | 0 | correct |
| 10 | 10 | 3 | 1 | 0 | silent no-op |
| 8 | 25 | 15 | 12 | 7 | **BLOCKS** |
| 8 | 40 | 10 | 7 | 2 | **BLOCKS** |
| 6 | 40 | 10 | 7 | 2 | **BLOCKS** |
| 10 | 40 | 7 | 4 | 2 | **BLOCKS** |
| 8 | 60 / 100 | 7-8 | 5 | 0 | correct |

The blocking rows produce fragments shaped `title=<null> org=<null> period="2021 - 2026"` — the
same shape that disqualified `addDoubleNewline` in section A — which project as an empty Company and
fail `Resume.ValidateContent` on the first one. On the pre-PR extractor that document promotes
(lossily, 1 of 5); under the rule it blocked. A `Promoted* -> Blocked` transition.

**Three properties worth carrying forward:**

1. **The margin is smaller than the spacing it must discriminate against.** The cut's whole
   discriminating power is `0.5 x median line height` ~ 4 pt, while a word processor's default
   paragraph spacing is 8 pt. PdfPig's `BoundingBox.Height` is **ascender height above the
   baseline**, not a full line box, so 0.5 x it is roughly 28 % of line pitch and not "half a line"
   — a correction to how that constant was justified.
2. **It is a window, not a threshold.** 25 and 40 tight lines block; 10, 60 and 100 do not. The
   median wanders with document composition, so no scalar cut can be reasoned about by "how much
   spacing is safe".
3. **A scalar cut cannot separate three populations.** A word processor produces bare leading,
   paragraph spacing *within* an entry, and block spacing *between* entries, in proportions that
   vary with composition. That is the structural reason the two review findings — false boundaries
   when the median sits low, and a silent no-op when it sits high — are one defect seen from both
   sides.

`pdf-single-column-intra-block-spaced` and `pdf-single-column-intra-block-spaced-tight-list` are the
corpus arms added so this is measurable rather than re-argued. They are a single-variable CHAIN:
the first adds spacing inside entries only, the second lengthens the tight-leaded list. That split
is deliberate — it turns "neither knob alone reproduces it" from a sentence into two rows, and the
first arm's digest is EQUAL to `pdf-single-column-spaced`'s, which is itself the measurement that
intra-entry spacing is invisible to the extractor today. And the extractor suite's `Extract_PdfSpacingIsInvisibleInTheExtractedText_TodaysBase`
pins today's behaviour on all three spacings.

**What a future boundary rule must do to those rows, per row.** `BetweenEntries` and
`BetweenAndInsideEntries` must go red — those documents state a boundary and a fix must find it.
**`UniformLeading` must stay green, PERMANENTLY:** that document authors no boundary at all, so a
rule that emits a blank line there has INVENTED one. The withdrawn rule did preserve that property,
and it is the one property that must never be traded for recall. And red on
`BetweenAndInsideEntries` is necessary, NOT sufficient — the withdrawn rule turned it red by
splitting entries apart, which is the regression itself. Judge that row by the parsed-entry counts
of `pdf-single-column-intra-block-spaced` and `pdf-single-column-intra-block-spaced-tight-list`,
which say whether the boundaries landed between entries or inside them.

### D. Position dependence of the unknown-heading pin (measured 2026-07-26, PR K)

`PROJEKT (URVAL)` lands in `Summary` only when it directly follows the profile block. After
`UTBILDNING` it is swallowed into the last education entry's raw text; after `SPRÅK` it lands in
the Languages list. The fixture encodes the position.
