# ADR 0170 — An ort or employment preference the user never stated counts as confirmed in the grade: the calculator reads the scorer's cause beside the verdict, the SQL rank follows, and the verdict, the wire and the copy do not move

**Date:** 2026-10-13
**Status:** Accepted
**Decider:** Klas Olsson, for the rule (D1) and for the Distans-only case (D6): his decisions of 2026-10-13, the first taken after a live test of a test account and recorded
on #2154, the second in answer to the code-reviewer's Major on PR #2155. D2–D5 are the driving session's design of that rule; the golden-rung part of D4 is its call alone. No `security-auditor` review was requested (no PII, auth
or secret surface changes).
**Amends:** ADR 0076, by its Amendment 2026-10-13: what a `NotAssessed` region or employment verdict counts for in the grade. The earlier passages it supersedes for the
grade count are named there. The SSYK gate, the Related cap (ADR 0084 §F4), the RB1 contradiction floor and the F1(b) requirement gate are not touched.
**Related:** [#2154](https://github.com/klasolsson81/jobbliggaren/issues/2154) · ADR 0076 and its Amendments 2026-06-19 (b), 2026-06-20, 2026-06-27, 2026-07-18,
2026-07-18 (b) and 2026-08-31 · ADR 0071 (the Goodhart guard) · ADR 0084 §F4 (the Related cap) · ADR 0161 (the five-minute matching run and the direct Toppmatch mail) ·
AGENTS.md §5 (Tests)
**Measured against:** `origin/main` at `ddada1da5`, and the delivered code in the working tree of the branch `fix/grade-open-prefs`, read on 2026-10-13. Nothing here is
merged.

> **Provenance.** Written by `adr-keeper` for the driving session from Klas's decision, the driving session's design (D2–D5) and the delivered code (CLAUDE.md §9.2, §13);
> it adds no decision of its own. Every statement about the code was read from the working tree on 2026-10-13. The mail behaviour in Consequences was measured by the driving
> session with Testcontainers; no test counts or timings are recorded here, because they belong in the PR body. The ADR sits in the ignored 0071–0199 range and needs
> `git add -f` and a README row (precedent 0167–0169), because the code comments point at "ADR 0170". ADR 0076 is tracked and carries its amendment in the same PR.

---

## Context

ADR 0076 grades an ad `Basic`, `Related`, `Good`, `Strong` or `Top` from the verdicts of its dimensions. Two of them are secondaries: ort (`RegionFit`) and employment form
(`EmploymentFit`). The grade counts how many are confirmed. In the Full grade (the card, the modal and the background run) one confirmed secondary is enough for `Strong`
and `Top` needs both plus a skill signal; in the Fast band that the list filters, counts and sorts by, `Strong` needs both. Until this ADR a secondary was confirmed by a
`Match` verdict and by nothing else.

- **The live test, 2026-10-13.** Klas used a test account with an occupation and an ort stated and no employment form. It could never reach Toppmatch, because only one
  secondary could be confirmed. On the list path (Fast band, SQL rank) it topped out at `Good`, so the "Stark match" filter returned no rows and the match sort never ranked
  a `Strong`.
- **The copy promises the opposite.** The setup modal says "Lämna tomt för att matcha alla" (`messages/sv/matchsetup.json`, line 70), and Mina sidor says "Alla
  anställningsformer (inget valt)" and "Hela landet (ingen ort vald)" (`messages/sv/settings.json`, `matchPrefs.emptyEmployment` and `matchPrefs.emptyOrter`). Klas read the
  grade as a broken promise in the copy, and not as the vacuous-gate doctrine at work. The doctrine (an unstated preference is `NotAssessed`, ADR 0076 Amendment 2026-07-18)
  is a rule for the verdict; the live test is about what the grade does with that verdict.
- **A verdict cannot say why a dimension is `NotAssessed`.** `ScoreOrtUnion` returns `NotAssessed` for three reasons: no ort stated (`PreferenceUnstated`), a län-only ad
  in a län that contains a kommun the user preferred (`RegionContainsPreferredMunicipality`, #477 Low 1), and — since this ADR — a user whose only ort choice is Distans
  (no cause, D6). `ScoreEmploymentMembership` returns it for one, `PreferenceUnstated`. The same verdict means different things for region. The reason already rides beside the score (`FullScoredMatch.Causes`, ADR 0076 Amendment
  2026-08-31), and the three production grade callers hold it.
- **Where the old reading lived** (by code reading at `ddada1da5`, 2026-10-13). `MatchGradeCalculator.Grade`, both overloads, counted `confirmedSecondaries` from `Match`
  verdicts only. `PerUserJobAdSearchQuery.GradeRankExpression`, the SQL twin of the Fast band, added nothing for `!ortStated` or `!employmentStated`.

## Decision

**A secondary, region or employment, is confirmed in the grade when its verdict is `Match`, or when its verdict is `NotAssessed` and its cause is
`MatchDimensionCause.PreferenceUnstated`. `MatchGradeCalculator.Grade` takes the causes as a required argument, `GradeRankExpression` gains the same two terms, and no verdict,
wire field, modal row or word of copy changes. An ort counts as unstated only for a user with no ort choice at all: no län, no kommun and not Distans (D6).** D1–D6 are this ADR's numbering.

### D1 — The rule (Klas)

Both overloads, `Grade(MatchScore, …)` and `Grade(FullMatchScore, …)`, count `RegionFit` and `EmploymentFit` by the rule above. Everything else in the ladder is unchanged:
the SSYK gate, the Related cap (ADR 0084 §F4, evaluated right after the gate), the RB1 contradiction floor (a `NoMatch` on either secondary is `Basic`) and the F1(b) requirement gate. The
no-CV ceiling is therefore still `Good`.

For an exact-occupation ad (SSYK `Match`, not Related) with no secondary contradicted, the ladder reads:

| Grade path | Confirmed secondaries | Grade |
|---|---|---|
| Fast | both | `Strong` |
| Fast | one | `Good` |
| Full, requirement gate open | both, with a skill or nice-to-have signal | `Top` |
| Full, requirement gate open | at least one (and not the `Top` case) | `Strong` |
| Full, requirement gate closed | at least one | `Good` |
| Either | none | `Basic` (not reachable, see Consequences) |

### D2 — The signature (driving session)

`Grade(MatchScore score, MatchDimensionCauses causes, bool isRelated)` and `Grade(FullMatchScore score, MatchDimensionCauses causes, bool isRelated)`.
`causes` is required, a `null` throws, and it has no default: a verdict cannot say why a dimension is `NotAssessed`, and a default of `MatchDimensionCauses.None` would let a
caller that forgot the argument silently get the old reading. The cause stays beside the score and is never put inside it, so the Goodhart pin on the score shape
(`FullMatchScorerLayerTests`) is untouched.

The three production callers (`BackgroundMatchingJob`, `GetJobAdMatchBatchQueryHandler`, `GetJobAdMatchDetailQueryHandler`) already hold `FullScoredMatch.Causes` and now pass
it. All three call the Full overload; the Fast overload is the specification its SQL twin mirrors. The Fast scoring methods `ScoreAsync` and `ScoreBatchAsync` return a bare
`MatchScore` with no causes, so a Fast grade in a test is read off `ScoreFull*Async(...)` as `.Score.Fast` together with `.Causes`; the two are equal by contract
(`IMatchScorer`).

### D3 — What stays neutral (driving session)

`RegionContainsPreferredMunicipality` (ADR 0076 Amendment 2026-07-18, #477 Low 1: the user stated a kommun and the ad names only its län) is a `NotAssessed` that neither
confirms nor contradicts, and it is not counted. A `NotAssessed` that carries no cause confirms nothing, and for the ort it is produced by exactly one actor: the Distans-only user (D6). For employment it is
not reachable, because `ScoreEmploymentMembership` always attaches `PreferenceUnstated`; if one ever arrived it would degrade to the old reading, and the calculator would not invent a confirmation. Because an unstated ort
confirms and the containment arm does not, a län-only ad grades one step lower for a user who stated a kommun than for a user who stated nothing (read from the code, not measured).

### D4 — The SQL twin follows (driving session)

`PerUserJobAdSearchQuery.GradeRankExpression` gains `ortOpen` (no place and not Distans, D6) as a disjunct in the ort term and `!employmentStated` in the employment term, in both the
`Strong` (4) arm and the `Good` (3) arm, so the Fast band that the list filters, counts and sorts by still equals the Full grade at `Good` and above. The floor, the Related cap and
the remote disjunct stay keyed on whether the ort has PLACES (a non-empty region or municipality list), so they are untouched. What "stated" means in SQL is derived in one place
(`HasOrtPlaces`, `IsOrtOpen`, `IsEmploymentStated`).

The golden top-skill sort lift (F4-15) follows. A `Strong` ad that also shares a CV skill sorts above a plain `Strong`. Since a user who left a dimension empty can now have a
`Strong`, the lift's condition takes the same two terms, so the lift follows `Strong`'s definition for them. It reads the exact occupation set only and has no remote
disjunct, both as before. This part is the driving session's call alone (Alternative E); `dotnet-architect` reviews it in the PR.

### D5 — Verdicts, wire and copy do not move; what is superseded (Klas, driving session)

The `NotAssessed` verdict for an unstated preference, the cause on the wire, the modal rows ("Ej bedömt") and all copy are unchanged. There is no migration and no new text,
and `MatchScorer.cs` changes in comments only. The vacuous-gate doctrine stands for the verdict. What is superseded is its reading as "neutral in the grade count": the
passages of ADR 0076 that say a `NotAssessed` region or employment "neither confirms nor caps", or that the `NotAssessed`/`NotAssessed` cell is `Basic`, no longer hold for
the grade when the cause is `PreferenceUnstated`. ADR 0076 Amendment 2026-10-13 names them.

### Not decided here

Background matching without consent (a separate change), and any change to the modal rows, the badge copy or the SSYK dimension. An unstated occupation still earns no tag.

### D6 — Distans-only is a stated ort (Klas, 2026-10-13, answering the code-reviewer's Major on PR #2155)

A user whose only ort choice is Distans (no län, no kommun) has stated an ort. The rule above applies only to a user with no ort choice at all; for a Distans-only user the ort is
neutral and the grade is what it was before this ADR (at most `Strong`, never `Toppmatch` on an on-site ad). The mechanism is the smallest one: `CandidateMatchProfile` carries
`PreferredRemote`, `ScoreOrtUnion` returns `NotAssessed` with no cause when the lists are empty and `PreferredRemote` is set, and the SQL twin's `IsOrtOpen` is false for her.
`PreferredRemote` decides whether the ort is stated and never whether an ad matches it: the scorer still matches on the two lists and on the ad's own remote flag only, and
`MatchProfileRemoteIndependenceTests` was amended narrowly to allow exactly that one member (ADR 0079 F1 is otherwise unchanged). Making a remote ad count as a `Match` for her is
a separate change (F1: scorer, SQL rank and pin), tracked as a follow-up issue.

## Alternatives considered

- **A — The calculator counts a secondary from the verdict and the cause, with the cause a required argument.** Chosen (D1, D2). *For:* the cause is already the carrier and
  already names `PreferenceUnstated`; the verdict, the wire and the copy stay as they are; a caller cannot forget it. *Against:* a pure function that took only the score now
  takes the causes too, and every caller must pass them.
- **B — The scorer emits `Match` for an unstated dimension** (the shape ADR 0076 Amendment 2026-07-18 used for #552, closing a hole in the emitted verdict and leaving the
  calculator alone). *For:* the calculator keeps its one-argument signature. *Against:* there the verdict was the lie; here `NotAssessed` is the truth. A `Match` would show
  "matchar" in the modal for something the user never stated, and it would change the wire.
- **C — Pass two booleans, `ortStated` and `employmentStated`.** *For:* explicit. *Against:* the cause already carries the fact, and Klas named `PreferenceUnstated`.
- **D — An optional `causes` that defaults to `MatchDimensionCauses.None`.** *For:* existing call sites compile. *Against:* it fails open. A caller that forgot the argument
  would silently get the old reading, because with `None` nothing confirms.
- **E — Leave the golden rung (the top-skill sort lift) alone.** *For:* a smaller change. *Against:* it is incoherent with D1. A `Strong` that shares a CV skill is meant to
  sort above a plain `Strong`, and without the two terms a user who left a dimension empty would have `Strong` ads the lift never reaches.

## Consequences

### Positive

- The promise in the copy holds. A user who leaves ort or employment empty can reach "Stark match" in the list and Toppmatch on the card (the latter when the ad gives a skill or
  nice-to-have signal and the requirement gate is open).
- Fast equals Full at `Good` and above still holds, because the SQL twin carries the same terms.
- The wire, the modal and the copy do not move, and there is no migration.

### Negative and risks

- **`Basic` now means one thing for an exact-occupation ad: a stated preference is contradicted** (the RB1 floor). Every exact ad that is not contradicted is at least `Good`.
  The Fast overload's `_ => Basic` arm, the `Basic` fallbacks in the Full overload and the SQL's last `: 1` arm are structurally unreachable after the floor, because
  `ScoreEmploymentMembership` has no `NotAssessed` arm but `PreferenceUnstated`. They stay for totality and for parity with the calculator.
- **For a user who stated neither ort (no place, not Distans) nor employment, every exact-occupation ad is `Strong` in the Fast band,** so the "Stark match" filter does not discriminate for that
  user. That follows directly from "Lämna tomt för att matcha alla" and is Klas's decision; it is written here so nobody rediscovers it as a bug.
- **The background run writes more `Top` rows and sends more direct mails to users who have consented** (ADR 0161). Measured by the driving session with Testcontainers: a
  consenting user who states an occupation and the skill the ad requires, and at most one of ort and employment form (three shapes: ort only, employment only, neither), now
  gets a direct Toppmatch mail for an ad that carries any value on the empty dimension. With the old counting none of the three got one, because the ad graded `Strong`, which
  goes to the digest. Users who state everything are unchanged. The contradiction floor holds: a stated ort that the ad contradicts writes no row and sends no mail. How many consenting
  accounts on the box this reaches is not measured here. A consenting user who stated neither now also gets a persisted `Good` row (or better with a CV) for every new exact-occupation
  ad, where `Basic` was never persisted (`ToNotifiable` in `BackgroundMatchingJob`; read from the code, not measured).
- **Rows already persisted keep the grade they were written with.** The run reads only ads created after a user's `LastMatchScanAt` watermark (`BackgroundMatchingJob`), so an ad
  scanned before this change is not graded again by it. Read from the code, not measured.
- **Test premises.** Hand-built `NotAssessed` fixtures with `MatchDimensionCauses.None` asserted a grade for a state production cannot produce (AGENTS.md §5, Tests). They were
  replaced by the causes the scorer emits: `ProducedCauses.For` in the unit tests, and `ScorerGrading` in the integration oracles, which grade from the real Full scorer.
- **Revisit trigger.** Any new `NotAssessed` reason for region or employment says, in the same change, whether it confirms. A reason that says nothing does not confirm,
  because only `PreferenceUnstated` does.

## Implementation

One PR on `fix/grade-open-prefs`. No migration, no new copy, no frontend change.

- **Application:** `Matching/Grading/MatchGradeCalculator.cs` (both overloads, `ConfirmedSecondaries`, `IsConfirmed`); the three callers named in D2; doc comments in
  `Matching/Abstractions/MatchDimensionCause.cs`, `FullScoredMatch.cs`, `IMatchScorer.cs` and `Matching/Grading/MatchGrade.cs`.
- **Infrastructure:** `JobAds/PerUserJobAdSearchQuery.cs` (`GradeRankExpression` and the golden-rung `OrderByDescending`); `Matching/MatchScorer.cs` (`ScoreOrtUnion` takes
  `preferredRemote`; otherwise comments only); `CandidateMatchProfile.PreferredRemote` filled by `MatchProfileBuilder` (D6).
- **Tests:** the unit ladder through `ProducedCauses.For`; the integration oracles through `ScorerGrading`; the background-run and mail tests for the three shapes in
  Consequences. Run results belong in the PR body.
- **Docs:** ADR 0076 Amendment 2026-10-13 (tracked, same PR); the README row for this ADR; this ADR is promoted with `git add -f`.

## References

- ADR 0076 (Amendments 2026-06-19 (b), 2026-06-20, 2026-06-27, 2026-07-18, 2026-07-18 (b), 2026-08-31) · ADR 0084 §F4 · ADR 0161 · ADR 0071
- Code: `src/Jobbliggaren.Application/Matching/Grading/MatchGradeCalculator.cs` · `src/Jobbliggaren.Application/Matching/Abstractions/MatchDimensionCause.cs` ·
  `src/Jobbliggaren.Infrastructure/Matching/MatchScorer.cs` (`ScoreOrtUnion`, `ScoreEmploymentMembership`) · `src/Jobbliggaren.Infrastructure/JobAds/PerUserJobAdSearchQuery.cs`
  (`GradeRankExpression`)
- Copy: `web/jobbliggaren-web/messages/sv/matchsetup.json` (line 70) · `web/jobbliggaren-web/messages/sv/settings.json` (`matchPrefs.emptyOrter`, `matchPrefs.emptyEmployment`)
- [#2154](https://github.com/klasolsson81/jobbliggaren/issues/2154) · AGENTS.md §5 (Tests) · CLAUDE.md §9.2, §9.6
