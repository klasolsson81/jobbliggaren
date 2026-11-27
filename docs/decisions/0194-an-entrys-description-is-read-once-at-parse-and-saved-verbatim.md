# ADR 0194 — An entry's description is read once at parse and saved verbatim

**Date:** 2026-11-27
**Status:** Accepted 2026-11-27 (the rulings of `senior-cto-advisor` named under *Deciders*, under Klas's directive of 2026-11-26). One question is routed to `security-auditor` in the PR's review and is open (*Open*).
**Deciders:** Klas Olsson (controller and product owner), by his directive of 2026-11-26 that a defect with the same root cause or in the same area is fixed in the same PR and not filed ·
`senior-cto-advisor` (ruling D11 of 2026-11-26, `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md`; points 3 and 6 of its pre-code ruling of 2026-11-27, `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md`; report-only, local and gitignored; the rulings' numbers are their own and are not this ADR's D1–D5) ·
`dotnet-architect` (pre-code pass of 2026-11-27, point 9, `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md`; report-only, local and gitignored)
**Amends:** nothing formally. It retires the premise of the 2026-07-17 CTO bind §1, fork (a), as `AutoPromoteContentMapper`'s docblock names it (D5).
**Related:** PR #2353 · ADR 0192 (the extractor and the provenance) · ADR 0193 (the vocabularies; the header line the description starts after) · ADR 0191 (D8: a saved CV carries its parse's language, with no backfill; the same rule here) · ADR 0071 (deterministic CV engines, no AI) · ADR 0074 · ADR 0093 (§D8, the unified review view)
**Measured against:** branch `fix/cv-user-feedback` in the worktree `C:/tmp/jbl-cv-user`, head `ef2d6584f`, read on 2026-11-27. The measurement under *Evidence* is the driving session's of the same day, taken on synthetic CVs.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. The code facts were read at the head named above; the probe and the two 404 readings are the driving session's and were not re-run for this ADR. The ruling files are gitignored local files, so what they decided is restated here and they are cited as local provenance. The test user's CV is never quoted.
>
> **Numbering.** 0194 was verified free on 2026-11-27 by the driving session (`origin/main`, the main copy, every worktree, open PRs). The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0190, 0191).

---

## Context

The review is deterministic and uses no AI (ADR 0071). A real test user's English PDF CV was imported and auto-saved, and the saved CV's review then said the tool had not read the bullets: A1, A2 and A6 were not assessed (`InsufficientData`) on every probe file. The same area and the same symptom as the import defects of ADRs 0192 and 0193, so the directive of 2026-11-26 puts it in the same PR.

1. **The saved CV carried no description.** The auto-promote mapper projected a parsed entry to a canonical one with `Description = null`. The segmenter's `RawText` for an entry is the whole block, header, period and organisation lines included, so promoting it as the description would double those lines in the render and feed them to the review as description-only prose. And the 2026-07-17 CTO bind (§1, fork (a)) held that computing the residual "would be re-parsing the engine never does". A saved CV therefore had experience entries with a role and a company and no bullets, and the canonical review had nothing to assess.
2. **The review already computed the residual, on the staging arm.** `ReviewText.DescriptionBullets` filtered an entry's block into bullets with a function of its own; the segmenter did not. Two definitions of the header line existed too: the field reader's, which knows a template can render the period cell first, and the review filter's, which took the first line as the header. For a period-first entry the real header line was therefore scored as a bullet.
3. **The premise is gone.** The segmenter reads each entry once, with its header, period and organisation lines in hand. Computing the residual there is not re-parsing; it is the parse.

## Decision

**The segmenter stores `ParsedExperience.Description`: the entry's block without its header line, its period lines and its organisation line, each remaining line trimmed and otherwise verbatim with its list marker kept, joined with a newline, and null when there is none. It lives in the encrypted JSON shadow as a trailing optional member, so an older row reads as none, with no migration and no backfill. The review and the improve transforms read the description when it is present and filter the block with the same function when it is not. The auto-promote mapper promotes the description verbatim, and none when it is longer than `Experience.DescriptionMaxLength`, so a description never moves an entry from Promoted to Blocked.**

### D1 — What is stored

- `ParsedExperience(Title, Organization, Period, RawText, Description = null)` (`Domain/Resumes/Parsing/`). `Description` is the entry's description lines joined with `'\n'`, or null.
- The lines are the entry block's trimmed, non-empty lines, from the line after the **header line** onward, minus every period line and the organisation line when it stands alone. The header line is `EntryHeader.LineIndex`: the first line, or the second when the first carries nothing but a period (#1060 β-1, a template that renders the period cell first). Nothing is rewritten, summarised or synthesised (AGENTS.md §5, the CV-engine rules): a line is the author's line.
- It is a trailing, optional member of a record that is persisted only in the encrypted JSON shadow (CV-PII). An older row deserialises with none: no migration, no backfill (the same bound as ADR 0191 D8: a backfill would rewrite saved CVs without the owner acting).

### D2 — One function, one header line

- `EntryDescription.Lines(entryText, organization)` (`Infrastructure/Resumes/Parsing/`) is the review's former staging filter, moved into Parsing. The segmenter calls it once, at parse, and stores the result.
- `EntryHeader.LineIndex` is extracted so that the field reader (`SplitTitleOrganization`) and the description read the same header line. Moving the filter fixed a leak: a period-first entry's header line was scored as a bullet.

### D3 — The review and the improve transforms read one unit

`ReviewableExperience.FromParsed(ParsedExperience)` (`Application/Resumes/Review/Abstractions/`) gives the entry's description with `TextIsDescriptionOnly: true` when the segmenter read one, and otherwise the verbatim block with `false`, which `ReviewText.DescriptionBullets` filters through `EntryDescription.Lines`. A new entry and an older one therefore give the same bullets, and the improve engine's `WeakVerbTransform` scores the same unit as A2 (#487 review side, #534 improve side). Null reads alike for "no description lines" and "an entry parsed before the segmenter read them": by design the review cannot tell them apart and does not need to.

### D4 — Auto-promote promotes it verbatim, and never over the cap

- `AutoPromoteContentMapper.ToContentDto` maps `Description` to the canonical entry's description unchanged. It never maps `RawText`.
- **The cap.** `Experience.DescriptionMaxLength` is now a constant (2,000, the client form's own limit, #855; precedent `Skill.NameMaxLength`; its own constant, not coupled to the summary's), and `ResumeEntryBuildability` reads it. Over the cap the mapper promotes **no** description, so a description never moves an entry from Promoted to Blocked. The entry is saved without it, and the original file is kept with the CV.

### D5 — This retires the premise of fork (a)

The 2026-07-17 CTO bind §1, fork (a), promoted no description because the residual would have to be computed by re-parsing. The residual is now computed once, at parse. The bind's other bounds stand: the mapper stays a pure verbatim projection with no synthesis, the buildability gate stays the one validation authority, and entries are never filtered or dropped.

## Not built

Two conditional clauses of ruling D11 were withdrawn on 2026-11-27 because their premises measured false (senior-cto-advisor, points 3 and 6):

- **A gap-fill in the approve step** ("if user-promote drops descriptions, carry them in the detail DTO and show them read-only"). It assumed an approve step the user can reach; both routes answer `notFound()` (measured by the driving session, 2026-11-27). No DTO field was added for it.
- **Stripping a doubled list marker at promotion** ("if markers come out doubled"). They do not: the composer prints a description verbatim (`CvDocumentModel.cs:118`), the generated PDF has no live viewer, and the ATS-text tab shows the glyph once. The storage contract is therefore **the author's markers are kept verbatim**, and the marker set keeps its one owner in Infrastructure (`CvLineShapes`).

## Evidence

Dated 2026-11-27, the driving session's; not re-run for this ADR.

| What | Reading |
|---|---|
| The saved CV's review on the synthetic copies of the test user's shape | A1, A2 and A6 are assessed, where they were not assessed (probe 2026-11-27) |

## Residuals

- **An entry parsed before this change has no description and promotes with none.** Nothing is re-parsed or backfilled.
- **A description over 2,000 characters is dropped at promotion.** The entry is saved without it, so the review does not assess that entry's bullets; the text stays in the original file.
- **Null is ambiguous by design** (D3).
- **The author's markers are kept verbatim,** including a doubled one if an exporter produced it (D4's measurement saw none).

## Open

- **The personnummer guard at auto-promote** (`ResumeContentPersonnummerGuard`, in `AutoPromoteGate`) now reads description lines that no longer have the header and period lines between them. If its gap profile can bridge a line end, a Promoted-to-Blocked move becomes possible that the 2,000-character cap does not cover; it fails toward the safe direction (dotnet-architect, point 9). This is routed to `security-auditor` in the PR's review as a question. This ADR records no verdict on it.
- **GDPR.** The description is a subset of text the same encrypted JSON already holds in `RawText`, and the original file is kept with the CV; the flow adds no new category of data. `security-auditor` grades the flow. If she grades the promotion of descriptions GDPR-implicated, that is Klas's decision before `agents-done` (senior-cto-advisor 2026-11-26, escalation 3, conditional).

## Alternatives considered

### Alt A — The chosen design (D1–D5)
**For:** the residual is computed once, where the header, period and organisation lines are known; the review, the improve transforms and the promotion read one unit; an older row needs nothing; the cap is respected.
**Against:** see *Residuals*.

### Promoting `RawText` as the description
Rejected: it would double the header, period and organisation lines in the render and feed them to the review as description-only prose (D4).

### Computing the residual at promotion by re-parsing
The original fork (a). Its premise is retired (D5).

### Promoting an over-cap description whole and letting the buildability gate refuse it
Rejected: it would move an entry from Promoted to Blocked, which this change must never do (senior-cto-advisor D11).

### A gap-fill in the approve step; stripping a doubled marker
Not built (*Not built*).

### Backfilling older rows
Not done (D1; ADR 0191 D8).

## Consequences

### Positive
- A saved CV's review reads its bullets: A1, A2 and A6 assess it instead of reporting that the tool could not read it.
- The review, the improve transforms and the promotion read one unit of text, from one function and one header line.
- A period-first entry's header line is no longer scored as a bullet.
- No migration, no new endpoint, no new DTO field.

### Negative and accepted
- **Older saved CVs and over-cap descriptions stay without a description** (*Residuals*).
- **The open question above** until `security-auditor` rules.

## Implementation

Part of PR #2353. Domain: `ParsedExperience.Description`, `Experience.DescriptionMaxLength`, `ResumeEntryBuildability`. Infrastructure: `EntryDescription`, `EntryHeader`, `HeadingDrivenResumeSegmenter.ParseExperiences`, `ReviewText.DescriptionBullets`. Application: `ReviewableExperience.FromParsed`, `AutoPromoteContentMapper`. The diff is the record of the rest.

## References

- **Local reports (gitignored):** `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md` (D11, escalation 3) · `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md` (points 3, 6) · `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md` (point 9).
- **ADRs:** ADR 0071 · ADR 0074 · ADR 0093 · ADR 0191 · ADR 0192 · ADR 0193.
- **Code:** `src/Jobbliggaren.Infrastructure/Resumes/Parsing/EntryDescription.cs` · `EntryHeader.cs` · `src/Jobbliggaren.Application/Resumes/Common/AutoPromoteContentMapper.cs` · `src/Jobbliggaren.Application/Resumes/Review/Abstractions/ReviewableCv.cs` · `src/Jobbliggaren.Domain/Resumes/Parsing/ParsedExperience.cs`.
