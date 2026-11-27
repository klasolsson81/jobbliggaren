# ADR 0193 — An entry header is read against two vocabularies: SCB's places and a curated list of organisation forms

**Date:** 2026-11-27
**Status:** Accepted 2026-11-27 (the rulings of `senior-cto-advisor` named under *Deciders*, under Klas's directive of 2026-11-26). The corpus diff, which moves one arm's outcome (D7), is Klas's to approve at the corpus sitting before `agents-done`; this ADR records the behaviours that approval covers (*Consequences*).
**Deciders:** Klas Olsson (controller and product owner), by his directive of 2026-11-26 that a defect with the same root cause or in the same area is fixed in the same PR and not filed ·
`senior-cto-advisor` (rulings D7, D8 and D10 of 2026-11-26, `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md`; points 2–5 of its pre-code ruling of 2026-11-27, `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md`; report-only, local and gitignored; the rulings' numbers are their own and are not this ADR's D1–D7) ·
`dotnet-architect` (pre-code pass of 2026-11-27, points 6–8, `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md`; report-only, local and gitignored)
**Amends:** nothing formally. It changes, for one case, how far the 2026-06-23 rule that slot order is never guessed reaches (D5), and it replaces `MunicipalityLexicon` (D1).
**Related:** [#2343](https://github.com/klasolsson81/jobbliggaren/issues/2343) (a tätort in the contact block) · [#2346](https://github.com/klasolsson81/jobbliggaren/issues/2346) (a "Role" / "Employer, City" header fused with the entry above; re-scoped, not closed, D7) · [#2249](https://github.com/klasolsson81/jobbliggaren/issues/2249) (entry boundaries) · PR #2353 · ADR 0192 (the geometry that rule D reads) · ADR 0071 (deterministic CV engines, no AI) · ADR 0074 · ADR 0107 · ADR 0108 · ADR 0109 · ADR 0136 · ADR 0043 (embedded, versioned data; generators run by hand)
**Measured against:** branch `fix/cv-user-feedback` in the worktree `C:/tmp/jbl-cv-user`, head `ef2d6584f`, read on 2026-11-27. The measurements under *Evidence* are the driving session's of the same day.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. The code facts were read at the head named above; the asset facts (the tätort count, the form lists) were counted from the committed files. The measurements (the 290 = 290 equality, the mutants, the corpus) are the driving session's and were not re-run for this ADR. The ruling files and the research record are gitignored local files, so what they decided is restated here and they are cited as local provenance. The test user's CV is never quoted: every example is synthetic (invented employers and roles).
>
> **Numbering.** 0193 was verified free on 2026-11-27 by the driving session (`origin/main`, the main copy, every worktree, open PRs). The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0190, 0191).

---

## Context

The review is deterministic and uses no AI (ADR 0071). Two defects in how the import reads a CV's lines share a root: the segmenter had too little vocabulary to tell which side of a line is which.

1. **A place the CV parser did not know (#2343).** The parser's only place vocabulary was the kommun list read from the JobTech taxonomy snapshot (`MunicipalityLexicon`, ADR 0043; #815). A contact block that names a locality which is a tätort but not a kommun was therefore not read as the CV's place.
2. **A header the boundary reader could not read (#2346, finding 1).** The entry-boundary reader (#2249) cuts a run of lines into entries only where the document admits exactly one reading. A header written as a role line, an employer line "Employer, City" and a period, in a run that also holds one-line headers, was fused with the entry above. Fusion is not only lost structure: on a synthetic Chrome fixture of that shape the fused middle role made A4 assert a 25-month gap that does not exist. The cause is that "X, Y" has two readings. "Role, City" is a real one-line header ("Undersköterska, Göteborg"), and reading every "X, place" as an employer line would turn the previous entry's last bullet into a job title. Place data alone does not decide role versus employer; **organisation identity** does (senior-cto-advisor 2026-11-26, D8 and correction 2).
3. **The 2026-06-23 bind.** `senior-cto-advisor` ruled on 2026-06-23 that which side of a header line is the role is deliberately not guessed (`SplitTitleOrganization`'s comments carry it). The rules below add evidence that makes one reading unique, so they are the case that bind has no answer for (D5).

## Decision

**The import asks two vocabularies, each through one predicate. A place is a name in SCB's kommuner or in SCB's 2025 tätorter. An organisation is a field that ends in, or opens with, a form on a curated list of legal-form and public-body words. The entry-boundary reader and the field reader ask the same predicates, so a line has one reading: an organisation form beside a known place proves which side of "X, Y" is the organisation, and nothing else infers slot order.**

### D1 — The place lexicon: SCB's kommuner and tätorter (#2343)

- **`CvPlaceLexicon`** (`Infrastructure/Resumes/Parsing/`) is the union of SCB's 290 kommuner (`CriterionReferenceLoader.LoadKommuner`, `scb-kommuner-2026.v1.json`) and SCB's tätorter with a population in 2025. The tätort asset is `scb-tatorter-2025.v1.json` (`tatortVersion` "2025.v1", 2,009 tätorter): SCB's PxWeb table `MI0810A/LandarealTatortN`, `Tid` 2025, content Folkmängd, open data under CC0 1.0, fetched 2026-11-27. It is generated by hand, off the build, by `tools/scb-tatorter/generate.mjs` (ADR 0043 Beslut B), refuses a row of another year, a malformed code, a missing kommun, an empty name, a repeated code and fewer than 1,900 rows, and refuses to rewrite a changed list under an unchanged version. Names are stored verbatim.
- **The kommun source changes from the JobTech taxonomy to SCB** so that both halves come from one authority and a tätort's kommun code can be checked. The change is neutral: the taxonomy's 290 kommun labels and SCB's 290 kommun names are equal as sets (measured 2026-11-27T10:50Z).
- **Matching normalises a name and never changes the stored string:** trim, a trailing parenthesis removed ("(preliminär beteckning)" and SCB's year notes), split on `", "` and `" och "`, empties dropped, repeats dropped. A fragment matches when it equals a whole name fragment, case-insensitively. A substring never matches, so "Göteborgsvägen" is not Göteborg.
- **One predicate, three call sites.** `ContactPatterns.IsBarePlace` and `TryBarePlaceLine` carry the normalisation every place question needs. The contact block's location (`ContactLocationExtractor`), the preamble residue (`PreambleResidue`) and the entry-boundary reader (`EntryVocabulary`) all ask through them. `CvVocabularySingleOwnerGuardTests` fails the build when the lexicon's `Contains` is called from anywhere else.
- **Built at host start and registered as an instance,** so a broken asset stops the host and not a user's import. The loader refuses a missing resource, a missing version, no tätorter, a malformed code, a kommun code missing from the SCB catalogue, a repeated code and a nameless row. `MunicipalityLexicon` is deleted and its completeness test moves to the new type.

### D2 — The organisation forms: a curated list in the lexicon, version 8

- `cv-parsing-lexicon.v1.json` version 8 adds `organisationForms`: **41 suffixes** (the Swedish forms and their spelling variants, the public-body words "kommun" and "stad", and the common foreign forms) and **5 prefixes** (`Region`, `AB`, `Aktiebolaget`, `Handelsbolaget`, `Kommanditbolaget`). Curated by hand, not imported.
- **Sources, read 2026-11-27.** Bolagsverket's naming pages for aktiebolag, handelsbolag, kommanditbolag and ekonomisk förening: an aktiebolag's name contains "aktiebolag" or "AB", and a public one carries "(publ)"; a handelsbolag's or kommanditbolag's name contains the full word and may not contain "HB" or "KB", which stay listed because the ruling lists them and a CV is not a register; an ekonomisk förening's name contains "ekonomisk förening" or "ek. för.". The foreign forms were checked against GLEIF's ISO 20275 entity-legal-forms code list v1.6 (February 2026, the file dated 2026-02-19), read in part. **The list is not imported:** its licence is unverified (searched 2026-11-26; only GLEIF's software is stated as CC0).
- **`CvParsingLexiconData.IsOrganisation(field)`** reads a field's items. An item is an organisation when it ends in a listed suffix, as whole words and never as a string suffix, or when it opens with a listed prefix and goes on. Matching is case-sensitive, because "as", "se" and "sa" are common words. A form never matches alone: "Karlstad" does not end in the word "stad" and a lone "SE" names no company.
- **The loader and the predicate share one normaliser** (`CvParsingLexiconLoader.OrganisationWords`), so storing and asking cannot disagree (#898). The loader refuses a form that could never match (one that normalises to nothing, one holding a glyph a field is split on, a prefix of more than one word) and a form listed twice. `CvVocabularySingleOwnerGuardTests` keeps the raw lists asked only through the predicate.

### D3 — The four rules (`EntryBoundaries`, `EntryVocabulary`)

`EntryVocabulary(Lexicon, Places)` carries the two vocabularies and the four line predicates the boundary reader asks: `IsEmployerLine` (the first field names an organisation and the second is a known place), `IsRoleOrgLine` (the second field names an organisation and the first does not), `IsCommaPlaceLine` (a `", "` separator and a known place after it) and `IsOrganisationLine` (an organisation, or "X, known place"). Where the extractor measured a line (ADR 0192), rule D reads its provenance.

- **A — a role and its employer.** In "X ⟨sep⟩ Y", when Y is an organisation and X is not, the line is a one-line header read as role and employer, so the line above it is not part of the header.
- **B — an employer line.** "X, Y" with X an organisation and Y a known place is an employer line. With a line directly above it that can head the range, the two are a two-line header at any anchor, and the one-line reading is excluded; except in a period-first run, where the line above a date ends the previous entry. With no such line above it, the employer line heads the entry alone: no title, and the line verbatim as the organisation (D4). This relaxes #2249's amendment 1 and only here, because the data makes the reading unique: a role is neither an organisation nor a place (senior-cto-advisor 2026-11-26, D8).
- **C — a two-line run.** In a run whose first entry fixed the two-line layout ("Role" / "Employer" / period), "X, known place" is an organisation line, though it splits into fields.
- **D — what can head the range.** A line cannot head the range below it when the extractor joined it from a wrap, when it opens with a list glyph, or when it starts further right than the line below it by more than that line's space width, which is a list item's indent. D reads geometry; it applies only where there is some.

Everything else reads as before. Two readings the vocabularies now decide have pins of their own: an unpunctuated intro above a "Role — Org AB" header is routed and the entries are cut (rule A), and a later "Role" / "Beta AB, Stockholm" entry is cut whatever the first entry's layout (rule B). The #2249 pins whose fixtures no vocabulary decides still hold: a later entry whose employer has no organisation form and an unknown city, or whose employer line carries its dates, stays fused.

### D4 — The field reader asks the same predicates (point 4 of the 2026-11-27 ruling)

`SplitTitleOrganization` reads a line the way the boundary reader cut it, whether or not the run split:

- "role / organisation line / period", where the first line is not itself an organisation and the second is an organisation line, is two whole fields. The role is read whole, so a role with a comma ("Test Analyst, QA") is one role.
- An employer line beside its period, in either order, is read as no title and the line verbatim as the organisation. The company is never read as the title, since its form and its place say which side is which.
- "Employer, City" / "Role" / period, where the first line names an organisation itself, keeps the reading it had.

Rejected for an employer line with no acceptable role line above it: cutting nothing, which fuses the job into the one above or routes it when it is first, loses its employer and period, and lets A4 assert a gap that does not exist; and keeping today's reading, which saves a company name as a job title against the data.

### D5 — Which part of the 2026-06-23 bind this ratifies

An organisation form and a known place prove which side of "X, Y" is the organisation. That is evidence, not slot guessing, and it is the only change. **"Exempel AB - Backend-utvecklare"**, an organisation on the first side only, **reads as before**. A rule that read "X, Y" as an employer line whenever X is an organisation and Y is not was considered and rejected (2026-11-27, point 3 (b)): not being an organisation is no evidence of being a place, and it would admit "Exempel Cars AB, Projektledare", a complete company-first one-line header, as a role-less employer line, which either steals the line above as its role or drops a cut that works today. B stays organisation and place.

### D6 — No profile-link recogniser (D10)

`PersonalInfo` has no field for a link, so a recogniser would claim text nobody harvests: the silent loss `PreambleResidue` and `ContactLocationExtractor` exist to refuse. After D1 a link that sits before the contact rail's recognised place falls under the existing counted contact-block drop, and a link after the last recognised fragment stays in the carrier; both are pinned as accepted and known. A home for profile links (a contact-model, paused-builder and export question, so another area) is filed as its own issue, a directive exception named in the PR body; its number is there and not repeated here.

### D7 — The corpus

Three layout arms are new: `pdf-mixed-header-layouts-indented-list` (role-first and "Role" / "Employer, City" headers in one section, list items indented with no glyph and no full stop), `pdf-mixed-header-layouts-glyph-list` (its glyph-led twin) and `pdf-roleless-employer-line`. The last has an employment headed by "Employer, City" alone, and it moves **from a save to `Blocked` with `IncompleteContent`**: a job without a title stops the automatic save. The corpus gate is Klas's decision (senior-cto-advisor 2026-11-26, escalation 1): the diff is presented to him before `agents-done`. #2346 stays open with a dated re-scope to its second entry instead of closing (*Residuals*).

## Evidence

All dated 2026-11-27 and the driving session's unless a file says otherwise; none was re-run for this ADR.

| What | Reading |
|---|---|
| The kommun source switch | the taxonomy's 290 kommun labels and SCB's 290 kommun names are equal as sets (2026-11-27T10:50Z) |
| The tätort asset | 2,009 rows, counted from the committed file |
| The predicates of rules A–D (D3) and of the field reader (D4) | 35 of 35 mutants killed |
| The three new corpus arms | the last moves from a save to `Blocked`, `IncompleteContent` (D7) |

## Residuals

Stated as facts about the vocabularies and the seam, never as promises.

- **An employer with no organisation form and an unknown city** is not read by B or C, and reads as today.
- **An employer with an organisation form and a place outside SCB's Swedish data** ("Beta Ltd, Reading") is not an employer line under B and reads as today. So does an employer with no form and a foreign place ("Gamma, Dublin"). B stays organisation ∧ place (point 3 (a)).
- **A place below the tätort level is unknown.** Småorter, city districts and localities without a row of their own in 2025 are absent; the generator's README names Torslanda and Kållered as two measured examples. A job whose employer line names such a place keeps its fusion with the entry above, and #2346 is re-scoped to that, not closed (the ruling's correction 4).
- **A department-first employer line** ("Avdelning — Exempel AB") is read as role and employer. Accepted and known.
- **A dated employer line** ("Exempel AB, Göteborg 2016 – 2020") keeps today's reading.
- **An org-first one-line header keeps β-1's slot order,** and **a public body outside the form list** is not recognised.
- **Short place names that are also words** ("Ed", "Vi", "Bro") match only as a whole fragment.
- **With no geometry, as in a DOCX, an unmarked and unpunctuated last list item directly above a role-less "Org AB, Place" line is read as that job's title.** Rule D has no geometry to read there, so the item can head the range. Today the same run is not cut at all and the two jobs fuse, losing the second job's employer and period; under B the employer and period survive and one bullet becomes the title, visible and the user's own text. A PDF is protected by rule D (ADR 0192). The provenance seam can take a DOCX source later without touching a rule; that is a fact about the seam, not a promise (senior-cto-advisor 2026-11-27, point 2).
- **The tätort data is SCB's 2025 edition,** published every fifth year; a later edition is a new version of the asset.

## Alternatives considered

### Alt A — The chosen design (D1–D7)
**For:** two vocabularies, each from a source with a name, a date and a licence, each asked through one predicate; the boundary reader and the field reader cannot disagree about a line; a cut is made only where the evidence makes one reading unique.
**Against:** see *Residuals* and *Consequences*.

### Reading every "X, known place" as an employer line
Rejected (senior-cto-advisor 2026-11-26, correction 2): "Role, City" is a real one-line header, and it would turn the previous entry's last bullet into a title.

### Rule B as "X an organisation and Y not an organisation"
Rejected (2026-11-27, point 3 (b)); see D5.

### Cutting nothing for an employer line with no role above it
Rejected (point 4 (i)); see D4.

### Importing GLEIF's ISO 20275 list
Not done: its licence is unverified. The list is curated by hand against it.

### A hand-written place list in C#
Rejected (AGENTS.md §5): SCB is the authority on its own place names, and the old kommun source was already the taxonomy snapshot for the same reason.

### Marker or numbering provenance from the DOCX reader
Not built (point 2); see ADR 0192's alternatives.

## Consequences

### Positive
- A mixed-layout CV and a "Role" / "Employer, City" header are cut into the entries the author wrote, with their employers and periods, and A4 stops asserting a gap that the fusion created.
- A contact block that names a tätort has a place.
- The organisation and place vocabularies are data with a source, a date and a version; a broken asset stops the host and not an import.

### Negative and accepted
- **A CV in the role-less shape moves from Promoted to Blocked.** A title written below the dates is not read; that is β-3's own accepted mirror. Approving the corpus diff approves this and one more behaviour: in a Word file whose bullets come from Word's list button, a last bullet without a full stop directly above a "Company AB, City" line is read as that job's title (today the two jobs are read as one), while in a PDF the bullet is recognised and the job gets no title.
- **The 2026-06-23 bind is narrowed for one case** (D5), by evidence and not by guessing.
- **The residuals above are accepted and known.**
- **Two vocabularies to keep:** a new legal form or a new edition of SCB's data is a new version of an asset.

## Implementation

Part of PR #2353. Infrastructure (`Resumes/Parsing/`): `CvPlaceLexicon` and its loader, `scb-tatorter-2025.v1.json`, `cv-parsing-lexicon.v1.json` (version 8), `CvParsingLexiconData.IsOrganisation`, `EntryBoundaries` with `EntryVocabulary`, `HeadingDrivenResumeSegmenter.SplitTitleOrganization`, `ContactPatterns`, `ContactLocationExtractor`, `PreambleResidue`; `tools/scb-tatorter/`. Architecture: `CvVocabularySingleOwnerGuardTests`. Corpus: three arms in `LayoutCaseCatalog`. The diff is the record of the rest.

## References

- **Local reports (gitignored):** `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md` (D7, D8, D10) · `docs/reviews/2026-11-27-2353-r3-senior-cto-advisor-precode.md` (points 2–5) · `docs/reviews/2026-11-27-2353-r3-dotnet-architect-precode.md` (points 6–8).
- **Issues:** #2343, #2346, #2249.
- **ADRs:** ADR 0043 · ADR 0071 · ADR 0074 · ADR 0107 · ADR 0108 · ADR 0109 · ADR 0136 · ADR 0192.
- **Sources, all read 2026-11-27 (links rot):** SCB, statistical database, table `MI0810A/LandarealTatortN`, and SCB's open-data terms (CC0 1.0): https://www.scb.se/om-scb/om-scb.se-och-anvandningsvillkor/oppna-data-api/ · Bolagsverket's pages on the names of an aktiebolag (https://bolagsverket.se/foretag/aktiebolag/startaaktiebolag/foretagsnamnochverksamhetforaktiebolag.493.html), a handelsbolag (https://bolagsverket.se/foretag/handelsbolag/startahandelsbolag/foretagsnamnochverksamhetforhandelsbolag.863.html), a kommanditbolag (https://bolagsverket.se/foretag/kommanditbolag/startakommanditbolag/foretagsnamnochverksamhetforkommanditbolag.915.html) and an ekonomisk förening (https://bolagsverket.se/forening/ekonomiskforening/startaekonomiskforening/namnochverksamhetforekonomiskforening.1337.html) · GLEIF, ISO 20275 entity legal forms code list v1.6, February 2026: https://www.gleif.org/en/about-lei/iso-20275-entity-legal-forms-code-list (read in part, not imported).
