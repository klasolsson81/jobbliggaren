# ADR 0136 — The year-first slash date is a ROW: recognised by a row grammar, dated by nobody

**Status:** Accepted · **Date:** 2026-08-24 · **Decider:** `senior-cto-advisor` bind 2026-08-24,
on the authority of Klas-direktiv 2026-08-03
**Context:** [#1195](https://github.com/klasolsson81/jobbliggaren/issues/1195) (the notation) ·
#1060 road 3 (the widening this closes out)
**Related:** ADR 0071 (determinism; honest-absent over confidently-wrong). This ADR **applies**
that principle to a population it had not yet reached. It does not amend it.
**Full decision record:** `docs/reviews/2026-08-24-1195-cto-bind.md` (this bind) ·
[#1195](https://github.com/klasolsson81/jobbliggaren/issues/1195) ·
[PR #1498](https://github.com/klasolsson81/jobbliggaren/pull/1498)

---

## Context

Swedish CVs write an employment or study period as `2020/01 – 2024/12`, and they also write a
`läsår` or a `räkenskapsår` as `2008/09` or `2023/24`. The two are the same notation and mean
different things, and the difference is not recoverable from the string.

**Klas ruled on the reading, 2026-08-03 (`d360bcc7`), verbatim:**

> *"det är väl ingen människa som skriver 2008/09 som månad. 2008-09 är månad september. 08/09 är
> år, som du sa, ett läsår t ex."*

That ruling settles one half and leaves the other half open. It settles that the engine has no
authority to **read** the year-first slash notation as a year and a month. It does not say what the
engine should then do with a period the CV plainly states.

The ruling is already carried in `PeriodParser`, together with the measurement that produced it:
the widening briefly read `2008/09 – 2011/12` as September 2008 to December 2011 where the writer
meant autumn 2008 to spring 2012, and it read the notation **inconsistently** — `2008/09` parsed
because `09` is a valid month number while `2019/20` did not because `20` is not. Same notation,
opposite outcomes, decided by an accident of arithmetic rather than by what the form means.

### What round 5 did, and why the question came back

#1060 road 3 added the slash point to `DatePatterns.DateRange`. Round 5 (decision D′) took it back
out of **both** endpoints, and that removal was correct on its own axis: `DateRange`'s match value
is what `HeadingDrivenResumeSegmenter.ExtractPeriod` **stores**, so a slash point beside an
unrelated, perfectly readable endpoint (`2020 – 2024/12`) stored a value `PeriodParser` then
refused whole — where `origin/main` had stored a working bare-year degradation (`2020 – 2024`).
`PeriodParser` states the rule the regression broke: a form the segmenter can extract but the
parser refuses *"is not an honest 'not stated', it is a period the CV states and the product then
drops."*

D′ restored `origin/main`'s answer on the VALUE axis. It left the LINE axis unaddressed, and
#1195 was opened to own that remainder.

### What D′ left open — measured 2026-08-24

Three things, all measured on the working tree before this change:

1. **The cited-evidence inversion, on six rows and not one.** On the three-line layout, review
   criteria A1/A2/A6 returned an affirmative `Pass` quoting the **date row itself** as the
   achievement evidence — the product asserting the user had quantified a result out of her
   employment dates, CLAUDE.md §5's cited-evidence rule inverted. The six rows:
   `2020/01 – 2024/12`, `2008/09 – 2011/12`, `2019/20 – 2021`, `2018 – 2019/20`, `2020 – 2024/12`,
   `2020-06 – 2024/12`. The last three are the **mixed-notation** rows D′ protected on the VALUE
   axis: `IsDateOnlyLine` was `False` for them because the trailing `/NN` residue keeps the reduced
   line non-empty, even though `DateRange` matched a prefix and stored a parseable period. So D′
   bought them a readable period and left the inversion open on them. #1195 framed this defect as
   `2020/01 – 2024/12` alone.
2. **A confidently wrong span, pinned as correct.** On the Lines[0] layout, `2019/20 – 2021` stored
   the bare leading year `2019`, which parses to a span of **zero years** for a CV stating autumn
   2019 to 2021. `2008/09 – 2011/12` stored `2008`, likewise zero years against a stated ~4. A
   characterisation pin asserted that outcome as *"a value both types agree on"* — the lane's own
   position pinned in reverse polarity.
3. **A span lifted out of a prose bullet.** `ExtractPeriod`'s leftmost scan runs over the whole
   entry text, so an entry whose date row is `2020/01 – 2024/12` and whose description says
   *"Ansvarig för perioden 2021 – 2023 av budgeten"* stored `2021 – 2023`: roughly two years
   claimed for a stated period of roughly five.

### The mechanism the file had already named

`DatePatterns.DateRange` serves two consumers whose requirements point in opposite directions, and
the file says so in its own words:

> *"The two consumers want OPPOSITE postures and that is why one list cannot serve both: the LINE
> question (IsDateOnlyLine → suppression) wants maximal structural coverage, because an ambiguous
> date is still a date; the VALUE question (ExtractPeriod → stored Period → PeriodParser) wants
> exactness, because an ambiguous date stored as a confident claim is worse than none."*

The file already acts on that split twice — once **by position** (`StartPoint` keeps a loose
`\d{4}-\d{2}`, `EndPoint` validates the month structurally) and once **by mechanism**
(`IsIgnorableTail` carries the trailing qualifier and the keyword-less open end *outside*
`DateRange`, explicitly because *"anything added there rides into the promoted CV and must survive
PeriodParser"*). What it had not done was give the split a name.

That is why widening `DateRange` cannot close the LINE half without moving the VALUE half, and it
is the whole reason this is an architecture decision rather than a one-token regex patch.

---

## Decision

**The year-first slash notation is RECOGNISED as a date ROW by a separate row grammar, and DATED by
nobody. Where an entry states such a row, the segmenter stores no period at all.**

Concretely:

1. **`DatePatterns.DateRowRange()` is the row grammar** — private, identical to `DateRange()` plus
   one point form, `\d{4}/\d{2}`, placed before the bare `\d{4}` it is a prefix of, per the file's
   existing prefix-order contract.
2. **The LINE/MASK consumers read the row grammar.** `StripTrailingDate` (and therefore
   `IsDateOnlyLine`, and therefore the segmenter's `SplitTitleOrganization` and
   `StripTrailingPeriod`, and `ReviewText.DescriptionLines`), plus `StripDates`, which masks dates
   so a downstream digit test cannot count an employment period as a quantified result (#487).
   `StripDates` moved with them: leaving it on the value grammar would have closed *"A1 cites the
   date row"* and left *"A1 cites a date inside a bullet"* open, same defect class, one altitude
   down, with the instrument already built.
3. **The VALUE consumer is untouched.** `HeadingDrivenResumeSegmenter.ExtractPeriod` still reads
   `DateRange()`. **The row grammar never produces a stored value** — it is a veto, not an
   extractor. Nothing the row grammar recognises can ride into a stored `Period`, which is exactly
   what keeps D′'s Blocker closed.
4. **`DatePatterns.IsUnreadableDateRow(line)` is the single question the segmenter asks.** It is
   true when the line carries a date range, is date-only under the row grammar, and the value
   grammar cannot read it. `ExtractPeriod` returns `null` when any line of the entry answers true,
   before either of its fallbacks runs. The grammar itself stays private (ISP): the segmenter needs
   the question, not the spelling.
5. **`DateNormalizationTransform` is not changed.** Under this decision the question it would have
   raised does not arise, because nothing unreadable is ever stored. That is avoidance by candidate
   choice, not suppression.

### Why this is a recognition rule and not a reading

#1195 required an ADR because *"any of 1/3/4 puts a specific reading of a Swedish date convention
into the CV engine as a product rule."* This decision puts in a **recognition** and a **refusal**,
so the clause's letter does not reach it. Its ground does: the change alters what `/cv/granska`
does on six measured rows and leaves `Period` empty for an entire notation, so a real user meets
the difference. The decision has also moved three times, and its two previous homes were review
reports under `docs/reviews/`, which are gitignored and therefore invisible to anyone who has not
run the worktree docs sync. Splitting one grammar into two, with a contract test as the sync
mechanism, is an architecture decision (AGENTS.md §8 point 9).

---

## Rejected alternatives

### Read the slash `NN` as a month (`2020/01` = January 2020) — #1195 option 1

Refused by **Klas-direktiv 2026-08-03**, quoted in full above. Independently of the ruling it was
also incoherent: it parsed `2008/09` and refused `2019/20` — the same notation, decided by whether
the second number happens to fall in 1–12.

### Date the slash point at YEAR precision — #1195 option 4, architect's option (C)

Refused in the round-5 bind. The sharpest of the three grounds: it gives a **confident wrong
answer on exactly the population Klas named**. A läsår `2008/09 – 2011/12` would be dated
2008..2011 — one year short, with full knowledge of what the notation means. A refusal is honest;
a wrong number that parses is not.

### Recognise it nowhere — decision D′ continued as the end state (#1195 option 2, status quo)

Refused. D′ was right about the VALUE axis and is preserved there. As an end state it leaves the
A1 cited-evidence inversion open on all six measured rows and leaves both round-6 consequences —
the prose-lifted span and the zero-length Lines[0] span — standing. "Zero risk" is not zero cost.

### Widen `DateRange` itself — #1195's own recommended option 3

Refused, and the issue's recommendation does not survive its own measurement. Widening the one
grammar both consumers read reopens D′'s Blocker on **three** measured rows, not one:

| date row | stored period before | parses | stored period under option 3 | parses |
|---|---|---|---|---|
| `2018 – 2019/20` | `2018 – 2019` | yes | `2018 – 2019/20` | no |
| `2020 – 2024/12` | `2020 – 2024` | yes | `2020 – 2024/12` | no |
| `2020-06 – 2024/12` | `2020-06 – 2024` | yes | `2020-06 – 2024/12` | no |

The issue names only the first and therefore under-states its own cost. It also breaks the file's
written rule that `DateRange`'s match value must survive `PeriodParser`. The recommendation was
written before the measurement existed, and the measurement removes its premise.

The issue additionally set a precondition — measure the LINE-grammar risk on `Acme AB 2000/12`, a
bracket-less trailing slash-year on an organisation line. That risk was measured and **cannot
materialise on any candidate**: every candidate adds `\d{4}/\d{2}` as a *point* inside a
`point – point` range alternation, and a bare trailing `2000/12` is not a range, so no branch
reaches it. `IsDateOnlyLine` is `False` and `StripTrailingDate` returns the line untouched. The
precondition is discharged, and it was aimed at a risk the proposed mechanism cannot produce.

### Row grammar only, leaving `ExtractPeriod` untouched (candidate "L")

Refused, and this was the only genuinely hard refusal. L is the minimal change and closes four of
the six measured problems without touching `ExtractPeriod` at all. It leaves two: the prose-lifted
span, and the confidently wrong zero-length span on the Lines[0] layout. Closing five of six while
leaving standing the one pin that records the lane's own position in **reverse polarity** would be
choosing the smallest diff over the quality bar — with the instrument that closes it already built
in the same file.

### Store the row's own text (candidate "L+")

Refused. It makes the slash form a stored-but-unreadable `Period`, which fires
`DateNormalizationTransform` (whose condition is literally `!PeriodParser.TryParse(period, …)`),
and all three available answers are bad: accepting the proposal tells a user her correct läsår is
non-standard and steers her toward `MM/ÅÅÅÅ` — the month reading Klas refused, now one altitude up
on the advice layer; suppressing it with a slash carve-out gives the notation a **third** home, in
the Improvement layer, which would have to re-encode "what a year-first slash pair looks like";
avoiding it means choosing this decision instead. L+ also loses the only thing it would buy: the
text lands in `Period`, but no criterion can grade it, `OccupationExperienceDeriver` still gets
zero years, and the text is already preserved in `RawText`.

---

## Consequences

### Positive

- **The cited-evidence inversion closes on all six measured rows.** A1/A2/A6 now quote the
  achievement bullet instead of the user's employment dates.
- **The prose-lifted span is gone.** An entry whose date row the engine may not read no longer
  borrows a period from a description bullet.
- **The two-line and Lines[0] layouts stop fabricating fields** from the date row — no invented
  Organization, no date row split into a fake Title plus a fake Organization.
- **D′'s Blocker stays closed.** The three mixed-notation rows keep their bare-year degradation
  (`2020 – 2024/12` → `2020 – 2024`), because the value grammar is untouched and the veto's third
  conjunct excludes every form the value grammar can read.
- **The two questions now have two homes**, each with one reason to change: `DateRange` changes
  when the app learns to **read** a notation, the row grammar when it learns to **recognise** one.

### Negative — accepted

- **`Period` is empty for this notation, on every layout.** A4/B6/B7 report `NotAssessed` and
  `OccupationExperienceDeriver` attributes no years to the entry. This is deliberate, and it is
  what ADR 0071 already prescribes; `ExtractPeriod`'s own comment states the rule this extends —
  *"A bare year on a later line is deliberately NOT treated as a period (honest-absent over
  confidently-wrong)"*.
  **What the empty field replaces is not one right answer but three measured wrong ones:** nothing
  stored at all (the three-line layout), a bare leading year parsing to a zero-length span
  (`2019/20 – 2021` → `2019`), and a span lifted out of a prose bullet (`2021 – 2023` for a CV
  stating `2020/01 – 2024/12`). Nothing true is lost. Three false things are.
- **The user's stated period leaves the structured field entirely.** It survives in the CV's raw
  text, which is what she sees; it does not reach anything that grades or aggregates periods.

  **Klas confirmed this half of the decision directly, 2026-08-24.** It was put to him as the one
  product question the `senior-cto-advisor` bind reserved for him — empty field, or her own text
  stored in it — with the cost of the alternative stated: storing an unreadable value re-arms
  `DateNormalizationTransform`, which would then advise her to rewrite a correct läsår as
  `MM/ÅÅÅÅ`, i.e. the month reading his own 2026-08-03 direktiv refused, arriving as advice instead
  of as a verdict. **He chose the empty field.** Recorded here rather than in a PR body because a
  confirmation given in chat and never written down gets asked again.
- **A pathological entry loses both of its periods.** An entry carrying *both* an unreadable date
  row and a readable one is vetoed whole, because the veto is entry-scoped. Refusing is the right
  direction under ADR 0071, and a rule that had to decide which of two stated periods is the
  entry's would be guessing. The shape is pinned, not guarded against.
- **Four point lists instead of two.** Built from shared fragments with no duplicated literal, and
  pinned along both axes (below). The count of lists is not the risk; unsynced divergence is, and
  that is closed.
- **The row grammar's year class is unbounded** (`\d{4}`, not `(?:19|20)\d{2}`), so `StripDates`
  masks `NNNN/NN – NNNN` whatever the digits mean — a priced widening of the residual the bare
  `\d{4} – \d{4}` already carried, pinned with its own control rather than guarded.
- **Out of scope and untouched:** the hyphen-written läsår form, the `13/2020` residual, the
  `2005 - 2010,` residual, and the `Mars`/`Maj`-as-employer residual. All four are already named
  and priced in `DatePatterns`.

### Open to Klas, and it does not block

Klas ruled that the notation is not a month. He has not ruled on what the app should then do with
the user's stated period. This ADR records the decision taken in the absence of that ruling — an
empty field — and the alternative he may choose instead (store her own text) is set out in the
bind report verbatim, together with its price: the improvement box returns and must be answered
before release, and the only answer that does not give wrong advice puts the notation in a third
place in the code.

---

## Implementation status

Shipped in the same PR as this ADR, single commit for the production change and the pins it moves.

- `DatePatterns.DateRowRange()` — private row grammar; `LineStartPoint` / `LineEndPoint` built from
  the same shared fragments as `StartPoint` / `EndPoint`, with `SlashPoint` the only literal that
  appears once.
- `DatePatterns.StripTrailingDate` and `DatePatterns.StripDates` read the row grammar.
- `DatePatterns.IsUnreadableDateRow` — the segmenter's one question, three conjuncts, each
  excluding a population the other two do not.
- `HeadingDrivenResumeSegmenter.ExtractPeriod` — the veto, before both fallbacks.
- `PeriodParser` unchanged in behaviour; its comment corrected, since the form is now dated by
  neither VALUE home while being recognised by the LINE home.
- `ReviewText.DescriptionLines` unchanged in behaviour; the slash date row is now suppressed
  through the `DatePatterns` disjunct alone, pinned by
  `ReviewTextPeriodLineUnionTests.DescriptionLines_SuppressesTheSlashDateRow_ThroughTheDatePatternsDisjunctAlone`,
  because a union that started passing through *both* halves would be a different change wearing
  this one's result.
- Every characterisation pin that named the slash form as an accepted regression is rewritten in
  the same commit to pin the new behaviour. Rewriting them **is** the change, not a cost of it.

**Drift control.** The four point lists are a 2×2 over two orthogonal one-token deltas — hyphen
(loose → exact month class) and slash (absent → present) — pinned by
`DateRangeYearFirstCharacterisationTests.TheFourPointLists_AreATwoByTwoOverTwoOneTokenDeltas`. It
asserts both deltas in both directions, pins each substitution's occurrence count at one, and pins
that the VALUE lists never carry the slash point. Four byte-equalities over two substitutions
cannot be satisfied by any other divergence, which is what makes it a synchronisation mechanism
rather than a comment.

**Regenerate the verdict** with `dotnet test --project tests/Jobbliggaren.Application.UnitTests`,
reading the `total:` line rather than the exit code (AGENTS.md §7). For provenance: the candidate
comparison that produced this decision ran on 2026-08-24 at `total: 18512` on every candidate,
where each remaining failure was a named accepted-regression pin that this change rewrites.

---

## References

- `docs/reviews/2026-08-24-1195-cto-bind.md` — the `senior-cto-advisor` bind this ADR records:
  candidate choice, the stored-value question, the transform question, scope, and the product
  question reserved for Klas.
- Decision D′ (#1060 road 3, round 5) — removed the slash point from `DateRange` on both endpoints,
  because a slash point beside an unrelated readable endpoint stored a value `PeriodParser` refused
  whole. Preserved here on the VALUE axis. Its own record is local and not tracked (ADR 0072
  docs-privacy), so the sentence this ADR needs from it is carried above rather than pointed at.
- [#1195](https://github.com/klasolsson81/jobbliggaren/issues/1195) — the option list this ADR
  gives a durable home, including the recommendation it declines.
- ADR 0071 — determinism, honest-absent over confidently-wrong. Applied here, not amended.
- Klas-direktiv 2026-08-03 — quoted verbatim under Context; paraphrased in `PeriodParser`.
