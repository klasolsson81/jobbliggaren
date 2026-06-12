---
name: jobbpilot-td-lifecycle
description: >
  Tech-debt lifecycle mechanics for JobbPilot — when a TD may be raised
  (phase rule), how TD blocks are written, moved, and closed across
  docs/tech-debt.md and docs/tech-debt-archive.md. Use whenever a TD is
  proposed, raised, closed, split, or merged, or when triaging review findings
  into in-block-fix vs TD. Triggers on: TD, tech debt, teknisk skuld,
  tech-debt.md, TD-nummer, severity, fas-regel, in-scope-fix.
---

# JobbPilot TD lifecycle

Extracted from CLAUDE.md §9.6–9.7 (2026-06-12) — this skill is the canonical
mechanics reference; CLAUDE.md keeps only the core rule.

## The phase rule (§9.6) — default is fix in-block

When a review or analysis identifies a finding, do **not** raise a TD by
default. Default = **fix in-block** (same commit batch as the original task).
A TD may be raised ONLY if one of two criteria holds:

1. **Different phase:** the finding belongs to a phase whose feature/dependency
   does not exist yet (e.g. "BYOK onboarding Fas 3" before the BYOK domain
   exists). TDs belonging to the current phase are fixed before phase close.
2. **Missing functional dependency:** the scope requires code/projects that do
   not exist (e.g. "JobbPilot.Api.UnitTests project missing" — TD-49).

No time threshold per touch (the 4h rule was removed 2026-05-11 — threshold
lifting creates TD bloat that returns next session). "Saving scope" is not a
legitimate reason — quality > tempo. When in doubt: in-block fix wins, and
the decision goes to senior-cto-advisor (decision-maker, not CC).

## Decision flow

1. Finding identified (CC or agent).
2. Default = fix in-block.
3. Multi-approach or triage decision → invoke `senior-cto-advisor` — CC gives
   no own recommendation.
4. CTO cites industry sources (Martin, Evans, GoF, Fowler, Beck, MS Learn) on
   quality trade-offs.
5. CC follows an unambiguous CTO decision without extra Klas GO; Klas-STOPP
   only for strategic questions (phase shift, ADR amendment, deploy). Klas
   always has the last word.

## Files

- `docs/tech-debt.md` — active TDs in a **Severity × Fas matrix**
- `docs/tech-debt-archive.md` — closed TDs in chronological close order,
  **full body preserved** (audit trail — Fowler 2018, Ford/Parsons/Kua 2017)

TD IDs are monotonically increasing and **never reused** (gaps are fine).

## Raising a TD

1. Verify the phase rule actually permits it (above).
2. Allocate next ID = max(existing `TD-[0-9]+` across both files) + 1.
3. Write the block in `tech-debt.md` under the right Severity × Fas section.
   Format: `## TD-N: <title>` (h2 with colon) with fields `**Kategori:**`,
   `**Severity:**`, `**Fas:**`, `**Källa:**`, description,
   `**Föreslagen åtgärd:**`, optionally `**Beroenden:**` / `**Trigger:**`.
4. Add a row to the overview table at the top (sorted Severity → Fas → ID).
5. Ensure cross-references from ADRs/session logs resolve to one of the files.

## Closing a TD

1. Move the **entire block** to `tech-debt-archive.md` with a closing note
   (date, commit/STEG reference, delivery notes, reviews). Never strip to
   "name + title".
2. Place chronologically (oldest first — new closures append last).
3. Remove from `tech-debt.md`'s matrix section + overview table.
4. Add a row to the "Stängda TDs" table at the end of `tech-debt.md`:
   `| TD-N | Title | Stängd YYYY-MM-DD | commit/STEG |`.

## Splitting/merging a TD

Mark the original `~~Title~~ — ERSATT YYYY-MM-DD av TD-Na + TD-Nb`, move it to
the archive with a short split rationale. New entries get full blocks with
`**Källa:** TD-N split per senior-cto-advisor-triage YYYY-MM-DD`.

## Classification

- **Severity Major:** security blocker, time-bound, or critical for phase
  close. **Minor:** everything else. When unsure → senior-cto-advisor.
- **Fas Nu** = time-bound/acute (should be empty); **Fas 1** = current phase,
  fix before phase close; **Fas 2/3+** = future phase lacking the dependency
  today; **Efter MVP/Trigger** = addressed on real user signal or scale
  threshold.
- **Never classify Minor — Fas Nu.** If a TD fits there: fix in-block instead.
