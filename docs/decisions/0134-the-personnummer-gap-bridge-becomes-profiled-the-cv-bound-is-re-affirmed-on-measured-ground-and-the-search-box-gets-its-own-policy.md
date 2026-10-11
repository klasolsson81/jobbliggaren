# ADR 0134 — The personnummer gap bridge becomes profiled: the CV bound is re-affirmed on measured ground, and the hand-typed search box gets its own policy

**Status:** Accepted · **Date:** 2026-08-20 · **Decider:** `senior-cto-advisor` (bind)
**Type:** A **re-adjudication** of an accepted residual, not an acceptance of a new one. #427 V3 is
re-affirmed for extracted document text and **superseded for one surface**, with the residual that
remains named and evidence-gated.
**Related:** [#1415](https://github.com/klasolsson81/jobbliggaren/issues/1415) (owns the bound) ·
[#427](https://github.com/klasolsson81/jobbliggaren/issues/427) V3 (the residual re-adjudicated
here) · [#666](https://github.com/klasolsson81/jobbliggaren/issues/666) (the newline sibling —
**scoped, not reversed**) · [#665](https://github.com/klasolsson81/jobbliggaren/issues/665)
(the precedent that moved this quantifier once before) ·
[#498](https://github.com/klasolsson81/jobbliggaren/issues/498) (a #427 residual overturned when it
reached an unencrypted column) · [#844](https://github.com/klasolsson81/jobbliggaren/issues/844)
(one recogniser owns the question) · [#1414](https://github.com/klasolsson81/jobbliggaren/pull/1414)
(put this chain in front of `?q=`) · ADR 0074 Invariant 1 (**explicitly NOT amended** — see the
Invariant 1 section) · ADR 0049 (the envelope `recent_job_searches` sits outside)

## Context

`PersonnummerTextNormalizer` builds a transient scan-copy in which a personnummer-shaped
digit-gap-digit pattern has its bridging whitespace removed, so the contiguous-only scanner can
flag gapped forms. From F4-8 until now that bridge admitted **0-2 visible columns** of `\p{Zs}`/tab
and never a line break. A wider gap was a documented, adjudicated residual: **#427 V3**.

The ground for V3 was one clause — *"a wider window would needlessly raise the chance of bridging
two unrelated numbers"* — resting on an architect's estimate that such a collision is
*"a ~1-in-hundreds coincidence"*. That ground was never measured, and the word carrying it was
**"needlessly"**: nothing required a wider window in June 2026.

PR #1414 changed what the bound governs. It put the identical `Scan(Normalize(x))` chain in front
of `?q=` — a **hand-typed search box** whose value persists in plaintext to
`recent_job_searches.q` (outside ADR 0049's envelope, no time-based retention, no hit gate) and
**re-renders verbatim** as the row label on `/sokningar` (`DeriveLabel`). A residual adjudicated
against machine-extracted CV text was now silently governing a different kind of input, on a
surface where the bearer may be a third party and the leak is user-visible.

## What was measured

Two measurements decided this, both taken 2026-08-20 against `e4d11e73`. Neither existed before.

**1. The residual is wider than the issue claimed.** Running the shipped chain over a vector set,
**twelve** gap classes are flagged by no guard in the product, not eight: 3/4/5 spaces, 3 tabs,
LF, CRLF, CR, `U+0001`, space-`Cc`-space, **`U+2028`**, **`U+000B`**, **`U+000C`**. The last three
were found by re-measuring rather than by inheriting the issue's list. *(An earlier draft of this
ADR said thirteen. The extra entry was a three-space gap in free text, counted as its own class
because the source list described it separately; it is the same gap and `security-auditor`
measured it behaving identically. The list was right and the number was not.)* `U+2028` LINE SEPARATOR is
load-bearing for the class choice below: it lives in `\p{Zl}`, which is in **neither** `\p{Zs}` nor
`\p{Cc}`, so the otherwise-natural `[\p{Zs}\p{Cc}]` would have left it an undocumented residual.

**2. The collision estimate was right about the case it named and wrong about the case a
line-break widening opens.** Measured against the real `Personnummer.TryParse` (seed `20260820`,
200 000 trials, committed as `PersonnummerBridgeCollisionRateTests`):

| bridged shape | valid personnummer |
|---|---|
| two arbitrary numbers, 8 digits + 4 digits | 1 in 133 |
| **a real `YYYYMMDD` date column + 4 arbitrary digits** | **1 in 9.9** |

A **13.4x difference**, and the reason is structural rather than incidental: date sanity is a
probabilistic gate for arbitrary digits and a **certainty** for a date column, leaving only Luhn.
Extracted document text does not stack arbitrary digits above a 4-digit run — it stacks date
columns, and `ResumeContentPersonnummerGuard.CollectFreeText` manufactures the same shape wholesale
by joining separate DTO fields with `AppendLine`.

So the F4-8 argument is ~13x optimistic for precisely the form a newline widening would open. **The
measurement supports #666 rather than overturning it**, which is the opposite of what the issue's
framing anticipated.

## Decision

**The bridge takes a required profile. There is no default.**

| `PersonnummerGapProfile` | gap class | bound | line breaks | surfaces |
|---|---|---|---|---|
| `ExtractedDocumentText` | `[\p{Zs}\t]` (unchanged) | `{0,2}` (unchanged) | excluded | every call site but `?q=`'s; the set is pinned by `PersonnummerGapProfileCallSiteTests` |
| `SingleLineUserInput` | `[\s\p{Cc}]` | `{0,8}` | included | `?q=` |

**D1 — `ExtractedDocumentText` is byte-identically the old policy**, and is now re-affirmed on
measured rather than asserted ground. A 3+ visible-column gap and every line break remain an
accepted residual in a CV.

**D2 — `SingleLineUserInput` is a different policy, not a stronger one.** The line-break hazard
cannot arise on a value with no line structure: a break inside a search box is stuffing, not
layout. There a missed personnummer is a plaintext PII leak that renders back to the user, while an
over-flag costs one silently uncaptured search.

**D3 — the two profiles are deliberately not orderable by strength**, which is why the parameter is
required and has no default. Each is unsafe on the other's input. A default would silently pick one
of those wrongs for whichever call site is written next.

**D4 — the gap class is written as ONE character class `[\s\p{Cc}]`, never as an alternation.** The
ReDoS linearity argument in `PersonnummerScanner` rests on the alternatives being pairwise disjoint,
and `\s` overlaps `\p{Cc}` on tab/LF/CR/VT/FF; an alternation would make that argument quietly false.

**D5 — the bound is `{0,8}`, not `{0,4}`.** `{0,4}` covers every vector measured on this issue and
still misses a five-space gap. That is fitting the bound to the test set instead of to a risk level.

**D6 — `\p{Cf}` is NOT folded into the gap class** on either profile. It stays stripped globally.
Treating it as a bridgeable gap character would re-open the `"811218 9876<U+0001>5"` counterexample
that the #1414 measurement turned on.

**D7 — `GapAwareCandidateRegex` (the redaction path) is untouched, and the flag-subset-of-redaction
invariant survives vacuously.** Every `PersonnummerRedactor.Redact` call site in `src/` lives under
a `Resumes/` path — CV bodies, evidence quotes, file names, ATS text — and **none redacts a search
query**. So the wider flag profile has nothing on the redaction path to be a superset of,
`PersonnummerGuardPathEquivalenceTests` keeps its product and floor unchanged, and **no existing pin
inverts**.

That is a property, not a count, and it is pinned rather than asserted:
`PersonnummerGapProfileCallSiteTests.Every_redaction_call_site_is_a_resume_surface` fails the build
if a `Redact` call ever appears outside `Resumes/`, which is the event that would make this
vacuity claim false. Regenerate the raw list with:

```
grep -rn "PersonnummerRedactor.Redact(" src/ --include=*.cs | grep -v "///"
```

**D8 — the CV file-name surface stays on `ExtractedDocumentText`, and that is a decision.** A file
name is a single line a human typed and reads like a `SingleLineUserInput` case — but it is
*redacted* downstream (`BackfillParsedResumeSourceFileNameMaskJob`, `ParsedResume`, `ResumeFile`),
and the redaction path keeps the narrow bridge per D7. Flagging it on a wider profile than the
redactor can mask would produce **flagged-but-unmasked** — exactly the superset break #465 and #498
each had to close.

## The residual that remains, and how it reopens

**R1 — extracted document text keeps the 3+-column and line-break residual** (D1). Its ground is
now the collision measurement above rather than an assertion.

**R2 — the mid-gap separator form (`"811218 - 9876"`) is unbridged on BOTH profiles**, and the
reason is the **grammar, not the bound**: both regexes are `sep? gap-run sep?`, so a separator is
admitted only digit-adjacent. It stays unbridged at any bound. The pin that covered this was named
`..._V3BoundHolds` and attributed it to the width — wrong in a way that decays silently, since a
later widening leaves that test green and a reader takes its name as evidence the bound held. Fixed
in the same change.

**R3 — `SingleLineUserInput` does not bridge a gap wider than eight characters.** `{0,8}` is a
risk-level choice (D5), not a measurement of any corpus, so what sits above it is a residual and
is named here rather than left to be inferred from the quantifier. Measured 2026-08-20: a gap of
eight bridges, nine does not — pinned on both sides by
`Normalize_SingleLineUserInput_BoundIsEightNotUnbounded`, because a bound nothing crosses is
indistinguishable from an unbounded quantifier.

**Reopening is evidence-gated, in #666's own form**, because that is the standard the house already
set for this class: *if a 3+-column, line-break or `Cc`-gapped personnummer is observed in real
corpus text, or in the pending read-only re-measurement of `recent_job_searches` on the box, reopen
#1415 with the evidence.* That wording is in the CV path's classes, so R3 gets its own arm: *if a
stored `q` is observed carrying a personnummer across a gap wider than eight characters, R3 is the
thing to reopen, not R1.* The QA corpus is the standing instrument and runs observe-only; it is
**not** a merge gate here, because nothing on the CV path changes and it therefore has no verdict to
flip.

## ADR 0074 Invariant 1 is explicitly NOT amended

Its guarantee — *"a CV containing a personnummer MUST be flagged and the user prompted to remove it
before any downstream processing; flag and ask to remove, never ask to add"* — is written about **a
CV** and **raw parsed text**, and says nothing about a bound, a gap class, or a residual. The
`ExtractedDocumentText` profile leaves every CV-path behaviour byte-identical, so Invariant 1 is
untouched in both letter and effect. This ADR adds a policy for a surface Invariant 1 never
described; it does not narrow or widen the one Invariant 1 does.

**What this ADR does supply is a home V3 never had.** The #427 session log recorded V3 as documented
in *"kod + ADR 0074"*, but ADR 0074 contains no mention of the residual, the bound, or #427. Until
now the accepted residual lived **only in code comments and test names** — which is how a residual
adjudicated for one surface came to govern another without anyone deciding that it should.

## Consequences

**Positive:** twelve gap classes close on the surface that persists and re-renders them · the CV
bound gains quantitative ground after fourteen months as an assertion · the profile choice per call
site becomes reviewable and is pinned by a fitness function · V3 gets an ADR home · the false claim
in `PersonnummerRedactorTests` ("the import-time guard still flags the CV") is corrected, and with it
the one sentence that made this residual look covered elsewhere.

**Negative:** two policies where there was one, so every call site now carries a decision it did not
have to make before — mitigated by the required parameter and `PersonnummerGapProfileCallSiteTests`,
which pins the file-to-profile mapping exactly, so both a changed profile and a NEW call site fail
loudly · the residual in R1 is unchanged and remains real.

**A sink this ADR does not reach, named so its absence is a decision.** The SavedSearch write
paths (`CreateSavedSearch`, `UpdateSavedSearch`, `ConfirmDerivedSearch`) persist `criteria->>'q'`
and a user-supplied `Name` with **no personnummer guard at all** — measured on this PR by
`dotnet-architect` and `security-auditor` independently. That is a **missing guard**, not a wrong
profile choice, and the two are different defects: nothing there calls `Normalize`, so this ADR's
profile axis has nothing to say about it and `PersonnummerGapProfileCallSiteTests` cannot see it.
`security-auditor` grades it **Minor on reach** — the surface has no frontend consumer today — and
records that the grade re-escalates to Major the day a "save this search" UI lands. Follow-up, with
its own bearer analysis: `SavedSearch.Name` is displayed and not redacted downstream, while `q` is
replayed by `RunSavedSearch`, so a guard would fail differently on each.

**Deliberately not done here:** moving `DisplayName`, `Resume.ValidateName` and `AutoPromoteGate` to
the single-line profile. They are single-line, non-redacted values and are plausible candidates, but
each is a separate change-reason with its own bearer analysis, and bundling them would put four
surfaces' worth of risk behind one verdict. Follow-up PR, not in-block. The file-name surface is
**not** a candidate — see D8.

## Amendment 2026-09-23 — a call site added (#1741, PR V1 #1798)

`PreambleResidue` became an `ExtractedDocumentText` call site in V1; the reason stands in
`PersonnummerGapProfileCallSiteTests`' own message. The table's surface count, "6 of 7 invocations" when this ADR was
written, is replaced by a pointer to that test, which pins the set. This ADR is local, so it could not change in the
commit that added the call site; it changes here, after the merge.

## Amendment 2026-09-23 (2) — a call site removed (#1741, PR B #1818)

`JobSeeker.ValidateDisplayName` left with the account name's last writer (ADR 0142 D7, Amendment 2026-09-23 (7)),
so the set lost its `JobSeeker.cs` row; `PersonnummerGapProfileCallSiteTests`' own message gives the reason. The
`DisplayName` candidate in "Deliberately not done here" above has no surface left to move. This ADR is local, so it
changes here, after the merge.

## Amendment 2026-10-11 — D7: one `Redact` site outside `Resumes/` (#2098)

D7's wording ("Every `PersonnummerRedactor.Redact` call site in `src/` lives under a `Resumes/` path") stopped being literally
true when `JobAd.ApplyContactRedaction` began redacting an imported ad's Title and Description (ADR 0106 D5 amendment
2026-10-11). The property D7 protects holds: no `Redact` call redacts single-line user input, and nothing flags ad text, so
there is no flag profile for the redactor to be a superset of. The pin was renamed `No_redaction_call_site_redacts_single_line_user_input_so_D7s_vacuity_holds`
(it was `Every_redaction_call_site_is_a_resume_surface_so_D7s_vacuity_holds`) and names `Jobbliggaren.Domain/JobAds/JobAd.cs`
as the one allowed site outside `Resumes/`. A second site outside `Resumes/` needs its own reading of D7.


## Amendment 2026-10-11 (2) — R2 superseded on the redaction path; the separator forms of an ad (#2123)

**Trigger.** `security-auditor` measured on 2026-10-11 (a probe built from the Domain of `ca7e65687`, no stored data read) that
an imported ad whose title or description carries `811218 - 9876` (a space on each side of the hyphen), `811218.9876`,
`811218/9876` or `811218_9876` reached the stored text unmasked. #2098 put `PersonnummerRedactor` in front of the ad text, but the
redactor's grammar is the CV one, whose R2 was accepted for a CV. On a public, indexable page (SEO S2, #2090) the same residual is
a published personnummer, and the S2-P3 count measures the stock on one day, not an ad imported after go-live
(`senior-cto-advisor`, #2123, 2026-10-11).

**What changes: `GapAwareCandidateRegex`, in two ways, and nothing else.**

1. A separator may sit **between two single visible columns**: `[\p{Zs}\t]` + separator + `[\p{Zs}\t]` is a second alternative of
   the gap group. The first alternative (`sep? gap{0,2} sep?`) admits a separator only digit-adjacent and cannot reach it.
2. `.`, `/` and `_` join the separator class (`-`, `+`, `\p{Pd}`, U+2212) in every slot of the redaction regex.

**What does not change.**

- **The flag path is byte-identical.** `PersonnummerTextNormalizer`, `CandidateRegex` and both profiles are untouched, so D1 holds
  as written (`ExtractedDocumentText` is byte-identically the old policy), ADR 0074 Invariant 1 is untouched, and no CV import
  behaves differently. The redactor can only mask more than the guard flags, never less.
- **The visible-column budget is two in total** (D1). The separator may sit between the two columns; a third column on either side
  stays unbridged. Zero-width `\p{Cf}` characters stay free.
- **A line break is still not bridged**, and neither is a split digit group (`81 12 18-9876`, `811218-98 76`,
  `19 81 12 18 9876`). #2123 is separator forms only.
- **`Personnummer.TryParse` stays the only authority** (date + Luhn), so a wider candidate shape cannot manufacture a valid match.

**R2, restated.** Unbridged on the flag path in both profiles (pinned by
`Scan_FlagPath_StillDoesNotBridgeASeparatorFlankedBySpaces_ADR0134R2StandsThere`); bridged on the redaction path for one column
each side. The reason R2 gave, "the grammar, not the bound", still describes the flag path exactly. On the redaction path the
grammar now has an arm for it.

**D7, restated.** "`GapAwareCandidateRegex` is untouched" is no longer literally true. The property D7 protects holds: the redactor
masks every spelling it masked before, with the same span (a frozen copy of the old pattern is the oracle:
`ScanWithGaps_EverySpellingTheOldGrammarMatched_IsStillMatchedWithTheSameSpan`), so the flag-subset-of-redaction invariant
(`PersonnummerGuardPathEquivalenceTests`) holds unchanged, and no `Redact` call redacts single-line user input.
`PersonnummerGapProfileCallSiteTests` re-measured: green, the set unchanged (6 files, 7 `Normalize` invocations, one allowed
`Redact` site outside `Resumes/`: `JobAd.cs`, which now also redacts `Company.Name`, ADR 0087). The widening is the safe direction
of the superset invariant. The opposite, a flag path wider than the redactor, is the break the normalizer's lockstep comment warns
about, and it is not made.

**Collision measurement, before and after** (`PersonnummerBridgeCollisionRateTests`, seed `20260820`, 200 000 trials per
population). **Before:** each new form 0 hits, because none was a candidate. **After:** exactly the rates of the same digits
written contiguously, asserted equal rather than near: 0.7515 % (1 in 133) for two arbitrary numbers, 1 in 9.9 for a real date
column plus four digits, for the five forms ` - `, ` – `, `.`, `/`, `_` (`TheIssue2123Forms_CollideExactlyAsOftenAsTheContiguousDigits_…`).
The amendment adds candidate shapes, not a collision rate; the exposure is bounded by the two numbers this ADR already priced. The
cost is the one D2 already names: a reference written like a personnummer (`20261011/1234`) is masked when it also passes date and
Luhn, which is over-redaction of an ad number, priced as the cheaper error.

**What the S2-P3 count can still read above zero on.** `docs/runbooks/public-job-page-publication-count.sql` counts a superset of
what the scanner masks, on purpose. Measured over its gap grammar (`gap{0,3} sep? gap{0,3}`; 2 leads × 400 gap strings per side ×
9 separators × 400 = 2 880 000 spellings, gap characters drawn from five visible spaces and two zero-width characters), the scanner
masks a spelling **if and only if** it holds at most two visible columns in total: 274 500 masked, 2 605 500 not, every one of the
latter with three to six visible columns, none within the budget. The committed pin is a smaller sample of the same grammar
(`ScanWithGaps_OverTheS2P3GapGrammar_MasksExactlyTheSpellingsWithinTheTwoColumnBudget`). So a reading above zero comes from one of
three things: three or more visible columns (R1/D1), a split digit group, or the link (`url` is not masked, by decision).

**Reopening** is evidence-gated, as D1 and R3 are: if an ad is observed carrying a personnummer in a form outside the list above,
reopen #2123 with the evidence. The QA corpus is the standing instrument and runs observe-only (63 of 63 green on this change).
