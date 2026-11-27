# ADR 0191 — The CV review reads the verb and cliché lists of the CV's own language, from one embedded asset each; a saved CV carries its detected language; a bullet's opener starts at its first letter

**Date:** 2026-11-26
**Status:** Accepted 2026-11-26 (the ruling of `senior-cto-advisor` named under *Deciders*, under Klas's directive of the same day).
**Deciders:** Klas Olsson (controller and product owner), by his directive of 2026-11-26 that a defect with the same root cause or in the same area is fixed in the same PR and not filed ·
`senior-cto-advisor` (ruling D2, D3 (its denominator half), D6 and D9 of 2026-11-26, `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md`; rulings A, B and C of 2026-11-27, `docs/reviews/2026-11-27-prx-senior-cto-advisor.md`; and the routing of PR #2354's first review round, 2026-11-27, `docs/reviews/2026-11-27-2354-senior-cto-advisor.md`; all report-only, local and gitignored; the rulings' numbers are their own and are not this ADR's D1–D10)
**Amends:** nothing formally.
**Related:** [#2341](https://github.com/klasolsson81/jobbliggaren/issues/2341) (English lists) · [#2352](https://github.com/klasolsson81/jobbliggaren/issues/2352) (a lead glyph defeats A6 and A2) · ADR 0071 (deterministic CV engines, no AI) · ADR 0093 and ADR 0112 (the improvement surface was removed from the API) · ADR 0043 (embedded versioned data)
**Measured against:** branch `fix/cv-review-opener-and-english` in the worktree `C:/tmp/jbl-cv-x`, read on 2026-11-26. The findings under *Context* were measured the same day on synthetic English CV fixtures rendered to PDF with headless Chrome, through the production chain `CvChainProbe` → `CvReviewEngine`.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. Facts about the code and the measurements are the driving session's, taken on 2026-11-26; none was re-run for this ADR. The ruling file and the knowledge-bank research record are gitignored local files, so the decisions and the sources are restated here and the files are cited as local provenance. What the change does to the review corpus and to the probes is measured after the change and goes in the PR body, not here.
>
> **Numbering.** 0191 was verified free on 2026-11-26 in the main copy, in every worktree and in every open PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0190).

---

## Context

The review is deterministic and uses no AI (ADR 0071). Six defects in the CV review share one area, and Klas's directive puts them in one PR.

1. **An English CV failed A2 (action verbs) on every bullet.** `verb-mapping.v1.json` (version "1.1") held 60 strong verbs in 7 groups and 8 weak phrases, all Swedish preterites. A2 lexemises with the CV's own language (Snowball sv/en), so an English opener ("Designed", "Led", "Automated") never shares a stem with a Swedish strong verb. A7 (clichés) and A9 (soft skills backed by an example) passed English text, because `cliche-list.v2.json` (version "2": 7 clichés, 8 soft skills) holds Swedish phrases only. Measured: A2 Fail citing "Designed …", A7 Pass on "results-driven team player", A9 Pass on "Reliable and detail-oriented". A8 also reads the soft-skill entries, for its "bare adjective list" check.
2. **A saved CV was always Swedish.** `Resume.Language` defaulted to Sv and `Resume.CreateFromParsed` took no language, so the canonical review of a saved English CV ran the Swedish lists and the Swedish Hunspell dictionary. The promoted English fixture had Language Sv and C7 Warn "19 möjliga stavfel".
3. **The bank's own weak phrase counted as strong.** With the production analyzer (`LocalTextAnalyzer` over `SnowballStemmer`), "Var ansvarig för budgeten" lexemises to [ansvar, budget], the same stem the strong "ansvarade för" lexemises to, and A2 tested strong before weak over the whole bullet.
4. **A lead glyph defeated A2 and A6** (#2352). "• ", "- ", U+F0B7 and "➤" defeated A2's weak check (`TrimStart` and `StartsWith`) and A6's capital scan.
5. **The two arms read different prose.** The staging arm's `ReviewText.AllProse` included entry header and period lines, which counted as sentences for C3 (passive ratio) and reached A7, A9, C2, C4, C6 and C7. The canonical arm read Profile and descriptions only.
6. **Two verdict notes assumed a Swedish CV.** A8 told an English CV that opens with "Objective" to write "en kort svensk sammanfattning", and C4 cited a "svensk standard" for omitting pronouns.

## Decision

**The review engine judges a CV with the verb mapping and the cliché list of the CV's own language, each list one embedded, versioned asset per `ResumeLanguage`, and a host that lacks a list for a language does not start. A saved CV carries the language its parse detected. A bullet's opener, for A2, A6 and `WeakVerbTransform`, begins at its first letter or digit, and the bank's weak phrase is tested before the strong stem. Both arms read one prose corpus, and no verdict note assumes Swedish.**

### D1 — One embedded, versioned asset per list kind per CV language

`verb-mapping.sv.v1.json` (version "1.1") and `cliche-list.sv.v2.json` (version "2") are pure renames of the old files. New: `verb-mapping.en.v1.json` (version "1.0") and `cliche-list.en.v1.json` (version "1"). Each list carries its own version because each changes with its own research (Martin, REP and CCP).

### D2 — The ports take the CV's language

`IVerbMapper.GetVerbMapping(ResumeLanguage)` and `IClicheLexicon.GetClicheList(ResumeLanguage)`. The review engine reads the list for `CvReviewContext.Language`, so A2, A7, A8 and A9 judge a CV with the lists of its own language.

### D3 — A host without a list for a language does not start

The Infrastructure DI root constructs both lexicons eagerly, and `KnowledgeBankAsset.LoadForEveryLanguage` throws an `InvalidOperationException` naming the missing language(s). An asset that is declared but not embedded also throws. The text analysis and the rendered labels for a CV language come from total maps (`TextLanguageMap.For`, `CvRenderStrings.For`) that throw for a language they do not know, and a test over every `ResumeLanguage` pins both, so a language added without them fails in CI instead of being read as Swedish.

### D4 — Provenance

The `VerbMapping` and `ClicheList` records carry a `Source`. The Swedish sources stay "verb-mapping" and "cliche-list"; the English sources are "verb-mapping.en" and "cliche-list.en". Improvement transforms cite `Source` and `Version`.

### D5 — Frames stay bound to the Swedish mapping

`frames.v1.json`'s `verbMappingVersion` still names the Swedish mapping, and the frame catalogue records the Source of the list it was validated against; `FrameApplyComposer.BuildStrongVerbSet` refuses a list whose Source or version differs, since a version alone no longer names a list. No Swedish frame can reach an English CV today, because no API endpoint maps the improvement engine, the frames or the preview and apply handlers since ADR 0112 (measured 2026-11-26 in `src/Jobbliggaren.Api/Endpoints/ResumesEndpoints.cs`, whose comments at lines 253–258 and 489–495 record the removed routes; no route maps them). `FrameProvider` and the Preview and Apply handlers pass `ResumeLanguage.Sv` explicitly; `CvImprovementEngine` reads the parse's `DetectedLanguage`. If the improvement surface returns, frames are gated by language in that same change.

### D6 — The English lists

- **Verb mapping.** The same seven groups under their Swedish group names. Twelve weak openers. Only "was responsible for" → "managed" is `dropInSafe` (same valency). The closure rule, that every weak phrase's suggestion is one of the list's own strong verbs, holds per language.
- **Cliché list.** Twelve clichés and eleven soft skills. Their `why` and `guidance` stay civic Swedish. Deliberately left out of the soft skills: "responsible" (it is also the weak opener "responsible for"), "driven" (it sits inside "data-driven" and "results-driven" at a word boundary), "dynamic" ("dynamic pricing"), "committed" ("committed code"), "motivated" alone (also a verb), "organised" and "organized" (also a common past-tense verb), and "dedicated" (the attribute in "dedicated team" and the verb in "Dedicated a weekly session to …"). Each would draw an A9 note that says something false about the text it cites.
- **What the content rests on.** The lists are our own curation, not copies of a published list. The sources give the principle: strong past-tense action verbs for finished roles, and no self-descriptions nobody can check. They are listed under *References*, Swedish ones first, the international ones flagged as such, all read 2026-11-26. The local research record is `docs/research/fas4-cv-kunskapsbank-sverige.md` §6.4 (gitignored).

### D7 — The opener (#2352; A2, A6 and `WeakVerbTransform`)

A bullet's opener is the bullet from its first letter or digit (`ReviewText.FromFirstWord`), so every non-letter lead glyph is skipped. A2 tests the bank's weak phrase at the opener first, then the strong verbs as the list declares them (`StrongVerbMatch`). The Swedish list has no key and matches by stem: the stem of the opener's first word alone against the stems of the listed verbs; a stopword opener has no lexeme and is never strong. The English list declares `"strongVerbMatch": "listedForm"`: it lists every inflected form it endorses (base, -s, past and -ing, 332 forms of 83 verbs) and no derived word, and A2 tests the opener against those forms with the weak phrases' matcher (`StartsWithWord`), because the English stemmer reads a derived noun as its verb ("development" as "develop", "manager" as "manage"). The Swedish list file is unchanged. A6's concreteness scan reads from the opener. `WeakVerbTransform`'s `Before` is the verbatim span at the opener, so a glyph stays outside the replacement. A number is a first word, so a number-led bullet does not open with a verb even when its next word is spelled like a bank verb ("35 automatiserade regressionstester …"). A year-led bullet ("2021 Ledde teamet") therefore opens with its year; no rule tells a year from a quantity by its digits, so this stays accepted and known. Also accepted and known: an inflected form that is also a noun reads as its verb ("Test Analyst at …", "Testing of …", "Design of …", "Lead developer …"), and so does a Swedish agent noun whose stem is a strong verb's ("Utvecklare i teamet …"); a bullet that opens with a pronoun ("Jag ledde …", "I led …") does not open with a strong verb, which is A2's own criterion. A letter used as a list marker (for example Word's second-level "o") and a numbered-list marker ("1. ") are not covered yet; they need the shared marker set from a measured exporter, in #2353.

### D8 — Language at promotion

`Resume.CreateFromParsed` takes a required `ResumeLanguage`, and a null fails with `Resume.LanguageRequired`, so the aggregate is valid in one step. Both callers, `PromoteParsedResumeCommandHandler` and `AutoPromoteGate`, pass the parse's `DetectedLanguage`. There is no backfill: it would rewrite saved CVs without the owner acting.

### D9 — One prose corpus on both arms

The staging arm's `AllProse` reads Profile plus the description units, which is what the canonical arm already reads. Staging verdicts may move (C3's denominator loses header and period lines); the staging arm has no finding ledger.

### D10 — Verdict notes hold for either language

A8's Objective note drops "svensk" ("skriv en kort sammanfattning"). C4's pronoun note names the CV convention ("CV-standard: utan pronomen"), which is the same in Swedish and English.

## Deferred (recorded, not done)

The rubric's internal `atsPassSignal` for A2 says "starkt svenskt handlingsverb" (`rubric.v2.3.0.json`, line 72), and C4's names a "svensk standard" (line 380). Correcting them needs a rubric version bump, which creates a new finding-ledger key (`ResumeFindingStatus.cs:28`) and so resets every user's finding statuses on every criterion; no client renders the signal (grep of the web app, 2026-11-26). The correction rides the next rubric bump that has its own reason. The user-facing `passCriterion` is outside the rubric version and already language-neutral.

## What is not measured

- **The effect of this change on the review corpus and on the probes.** It is measured after the change and recorded in the PR body.
- **Canonical A2 and A6 findings can re-key at an unchanged rubric version.** Derived, not measured.
- **A letter or a number as a list marker.** Not covered yet; it needs the shared marker set from a measured exporter, in #2353 (D7).

## Alternatives considered and items left out

This section lists only what the rulings record as weighed or left out.

### Alt A — The chosen design (D1–D10)
**For:** a CV is judged with the lists of its own language; each list changes with its own research and carries its own version; a language without its lists fails at start-up.
**Against:** see *Consequences*.

### Backfilling the language of saved CVs
Not done (D8): it would rewrite saved CVs without the owner acting.

### Correcting the rubric's `atsPassSignal` now
Deferred: it needs a rubric version bump, which resets every user's finding statuses on every criterion, for a signal no client renders (*Deferred*).

### Gating frames by language now
Not done (D5): no route reaches the frames since ADR 0112. They are gated in the same change that brings the improvement surface back.

## Consequences

### Positive
- An English CV is judged on A2, A7, A8 and A9 with English lists, and a saved English CV is no longer stored as Swedish at promotion, which was the cause in Context 2.
- The two arms read one prose corpus, so C3 and the other criteria that read prose no longer count header and period lines on one arm only.
- A language added to `ResumeLanguage` without its lists fails at start-up and not at the first review.
- Each list is versioned on its own, so the Swedish and the English change independently.

### Negative and accepted residuals
- **A mixed-language CV is read with the lists of its dominant language.** C5, language consistency, stays NotAssessed v1.
- **Adding a `ResumeLanguage` now requires every list kind for it** before the host starts.
- **The English lists are a curated v1, not exhaustive.** An English strong verb that is not listed reads as "other", not strong.
- **Canonical A2 and A6 findings can re-key at an unchanged rubric version** (derived, not measured).
- **Staging verdicts may move** (D9).
- **A letter or a number used as a list marker is not covered** (D7, #2353).
- **A year-led bullet opens with its year** (D7), accepted and known.
- **An inflected English form that is also a noun reads as its verb, so does a Swedish agent noun, and a pronoun opener is not strong** (D7), accepted and known.
- **The Swedish lists are unchanged,** pure renames (D1). What can change a Swedish verdict is D7's opener rule and D9's corpus, not the list.
- **The rubric's `atsPassSignal` for A2 and for C4 still name Swedish** until the next rubric bump (*Deferred*).

## Implementation

One PR on `fix/cv-review-opener-and-english` for #2341 and #2352. The members it changes are named in D1–D10; the diff is the record of the rest.

## References

- **Local reports and research (gitignored):** `docs/reviews/2026-11-26-cv-user-senior-cto-advisor.md`; `docs/reviews/2026-11-27-prx-senior-cto-advisor.md`; `docs/reviews/2026-11-27-2354-senior-cto-advisor.md`; `docs/research/fas4-cv-kunskapsbank-sverige.md` §6.4.
- **Issues:** #2341, #2352.
- **ADRs:** ADR 0043 · ADR 0071 · ADR 0093 · ADR 0112.
- **Sources for the English lists (D6), all read 2026-11-26.** Swedish sources first:
  - Arbetsförmedlingen: https://arbetsformedlingen.se/other-languages/english-engelska/cv-application-and-interview/writing-a-cv
  - Karolinska Institutet career service: https://education.ki.se/current-student/support-and-resources/career-service/career-support-for-students/10-tips-on-how-to-write-a-cv
  - Lund University alumni and career office: https://alumni.blogg.lu.se/6-steps-to-the-perfect-cv/
  - Örebro University: https://oru.se/english/career/finding-a-job-in-sweden/writing-a-cv
  - University of Gothenburg, Job Seeker Guide 2023: https://www.gu.se/sites/default/files/2023-11/Job%20Seeker%20Guide%2023%27_ENG.pdf
  - International, verbs: University of Pennsylvania Career Services: https://careerservices.upenn.edu/resources/career-services-resume-action-verbs/
  - International, verbs: Careers New Zealand: https://careers.govt.nz/articles/improve-your-cv-by-using-action-words
  - International, clichés: Monster: https://www.monster.com/career-advice/resume/resume-buzzwords
  - International, clichés: Hays: https://www.hays.com.au/career-advice/resumes-cover-letters/cv-cliches-to-avoid
