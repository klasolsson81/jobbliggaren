# Session 2026-06-13 — Klass 2 panel rendered-verify + filter-control contrast fix

**Branch:** `fix/filter-control-border-contrast` · **HEAD at start:** `f37f53d`
**Scope:** Thread 2 — rendered-verify the Klass 2 filter panel on `/jobb` (long
pending "Klas lokalt, stack nere" across E2j / Fas E). Outcome: verify PASS on
all behavioral checks; one confirmed WCAG fail fixed in-block.

## Operational — local stack brought up (CC owns the stack)

Prompt claimed "Api 5049 + Worker + FE 3000 kör redan"; Api was **down**. Root
cause = documented runbook §7 **fälla 1**: `.NET` does not expand
`${POSTGRES_PASSWORD_DEV}` in `appsettings.Development.json` and there is no
`appsettings.Local.json` → a naive `dotnet run` sends the literal placeholder as
the password → `FATAL 28P01 password authentication failed for user
"jobbliggaren"`. Fixed by exporting `ConnectionStrings__Postgres` (built from
`.env`) + `ConnectionStrings__Redis` per runbook §7. Full stack healthy: Api
`/api/ready` 200 (corpus `activeCount=42713`), Worker (Sync + RefreshLandingStats
running), FE real stats. Memory `feedback_restart_stack_after_commit_stop`
hardened with the 28P01 signature (so the reflex is "run §7", not escalate).

## Method — auth-gated `/jobb` IS renderable locally (unblocks the recurring defer)

`pnpm visual-verify` auth-mode throws on `http://localhost` because it *injects*
the `__Host-`/Secure session cookie via Playwright `addCookies`, which the
Playwright API rejects on an http origin. But Chromium itself treats
`http://localhost` as a secure context and accepts `__Host-`/Secure cookies set
by a real `Set-Cookie`. So **driving the real login form** (`/logga-in`,
`#email`/`#password`/"Logga in", dev-test creds) authenticates on the local
stack. A throwaway Playwright script (deleted) logged in, opened the panel, and
captured screenshots + a DOM text-extract. This retires the "rendered-verify
pending Klas, auth-gated, stack down" pattern for local verification.

## Thread 2 — Klass 2 rendered-verify (all behavioral checks PASS)

Real facet data confirmed via API (dev-test login → Bearer):

| Dimension | Result |
|---|---|
| Omfattning order | `Alla` → `Deltid (6 365)` → `Heltid (31 141)` — **Deltid before Heltid** |
| Anställningsform | 8 "honest" types, Swedish-alphabetical; **`Vanlig anställning (25 710)`** present |
| Pill name | `Filter` (button text + panel `aria-label="Filter"`) |
| Counts live-refresh | selecting Deltid → employment counts recomputed to the subset (Vanlig 25 710→3 577), footer "Visa 6 365 annonser" |
| Roving tabindex | radio: only checked has `tabindex=0`; checkbox group: each `tabindex=0` (correct) |
| Focus | green focus ring on radio (rendered-confirmed) |
| Arrow-nav (pilnav) | ArrowDown moves selection **and** focus (WAI-ARIA radiogroup) |
| Dark mode | renders correctly (light+dark captured) |
| Screen-reader (NVDA proxy) | roles + `aria-checked` + `aria-label` + radiogroup/group correct |

Counts drift with the live corpus (snapshot-cron + purge); "Vanlig anställning"
was 24 059 in an earlier session, now 25 710 — the bucket is present (check passes).

## Finding + fix — filter-control indicator border (WCAG 1.4.11)

`.jp-checkitem__box` (globals.css 1477) + `.jp-radioitem__dot` (1556) used
`--jp-border-strong` for the **unselected** indicator border:

- Light: `#97A4B8` on `--jp-surface #FFFFFF` = **2.52:1** → WCAG 2.1 AA 1.4.11
  (non-text contrast, 3:1) **FAIL**.
- Dark: `#6F86A8` on `#1B2B47` = **3.81:1** → PASS (fail is light-only).

Confirmed both statically (tokens) and rendered (faint borders, light+dark).
Flagged as a "tvärgående TD-kandidat" by design-reviewer across E-phase PRs but
never triaged. **senior-cto-advisor** verdict: **in-block-fix, no TD** — both
§9.6 TD-criteria fail (same Fas E, no missing dependency) and it is a Major AA
fail in live code (DoD #6). Fix: swap both borders to `--jp-border-input`
(light `#7C8AA0` = 3.5:1 PASS; dark identical, still 3.81:1). Two shared classes,
exactly two consumers (Klass2 panel + Ort/Yrke popovers).

**Reviews:** senior-cto-advisor `a5262510bd0f60a21` (in-block verdict) +
design-reviewer `a80eb28cbdc1cd0de` (✓ Approved 0/0/0, rendered light+dark ×
Klass2/Ort/Yrke). Gate: vitest (job-ads) green. `pnpm build` intentionally
skipped (CSS-only, no RSC↔Client boundary change per AGENTS.md; would clobber the
live dev `.next`).

## Open / next (Klas)

- **Omfattning order — DECIDED 2026-06-13 (Klas, AskUserQuestion): keep
  Deltid-first.** Render confirmed `Alla / Deltid / Heltid` (Swedish
  Label-Ordinal). Platsbanken shows Heltid first; Klas chose to keep the honest
  alphabetical order (consistent with the "honest data over Platsbanken
  curation" Klass 2 choice). No change, no follow-up PR.
- **NVDA audio pass** — the ARIA is correct (automatable proxy passes); true
  screen-reader audio is Klas's manual check.
- **shadcn `radio-group.tsx` primitive** still uses `--jp-border-strong` —
  separate component, out of scope here; likely the same 1.4.11 issue if it
  renders unselected radios on paper. Separate triage if/when it surfaces in UI.
