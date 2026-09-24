# ADR 0144 — Copy density: the self-evident is not explained

**Date:** 2026-09-24
**Status:** Accepted
**Decider:** Klas Olsson (directive 2026-09-24; his three answers are recorded below)
**Related:** epic #1822 (parts #1823–#1828); #1003; ADR 0003; ADR 0038 (amendment 2026-05-17); ADR 0047; ADR 0140; ADR 0142

## Context

On 2026-09-24 Klas showed four surfaces — `/logga-in`, `/logga-in/kod`, the no-account
mail and the code mail — and said they were examples: many pages carry "brus". In his
words: *"man ska aldrig behöva beskriva självklara saker"*; the format hint under the
email field was *"helt onödigt"*; the code step is *"en halv roman, för att enbart
klistra in en kod"*; the code mail shows a *"superliten kod, men mer BRUS som tar upp
fokus"*; the mail footer should carry the real tagline. He asked whether the design
agent has wrong instructions. It has, and this record measures how.

### The guidance produces the fluff

Measured 2026-09-24 against `origin/main` at `23d38792`:

- `.claude/skills/jobbpilot-design-copy/SKILL.md:302-315` modelled the email format hint
  as the ✅ example of the placeholder rule.
- `.claude/skills/jobbpilot-design-components/SKILL.md:148-151` named the email
  address's syntax as the typical case that needs a hint, and `:169-171` recorded the
  same hint as the placeholder's replacement.
- `.claude/skills/jobbpilot-design-a11y/SKILL.md:156-180` said "Every form input must
  have all of the following" and its example carried a reassurance sentence under the
  email field.
- `.claude/agents/nextjs-ui-engineer.md:73-74`: "label/help text carries the
  instruction".
- `.claude/agents/design-reviewer.md:63-66` (area 4: empty states give a next step,
  errors name cause + action), `:68-75` (area 5: "without guessing", ADR 0047) and the
  severity table `:79-84`: a missing explanation is a Blocker or a Major; **excess text
  had no finding class** — the nearest row was "Minor: micro-copy polish", graded Allow.
  One sentence too few blocked a merge; ten too many passed.
- The only rules pushing the other way were soft ("Direkt: 10 ord där möjligt") or
  aimed at marketing copy, emoji and exclamation marks. The one silence rule (the match
  grade, DESIGN.md §8) was deliberately kept from becoming a principle
  (`docs/reviews/2026-09-02-design-silence-design-reviewer.md:19,42`, local-only).

### The directive that never became a rule

- ADR 0142 line 38 quotes Klas: *"så få klick och info som möjligt"*. No reviewer
  graded against it.
- #1003 (2026-07-20): explanations are not printed inline by default; they go behind
  the ?-help/expander pattern, with the GDPR Art. 5 disclosure in Ansökningshistorik as
  the hard exception. No skill or charter referenced it.
- Klas's live review of `/foretag/sok` on 2026-07-23, point 9 ("för många utskrivna
  hints"), became a per-surface Minor (`docs/reviews/2026-07-25-foretag-sok-followup-design.md:35`,
  local-only).

### Where the four surfaces' copy came from

- The two-sentence lede on `/logga-in` (`pages.auth.passwordless.entry.lede`) and the
  format hint row (`pages.auth.passwordless.entry.emailHint`) were bound by
  design-reviewer in the #1738 form round (`docs/reviews/2026-09-21-1738-form-design.md`
  Q6, local-only; "A body sentence is required"), citing ADR 0047, and written into
  ADR 0142's Page form. Neither was argued on its own merits; the hint was a reuse of
  the skill's example.
- The Art. 13 line (`pages.auth.passwordless.entry.privacyHint`) is security-auditor's
  Major 8 (`docs/reviews/2026-09-17-auth-epic-security.md:117`): the notice moves with the
  collection point, "ingen ny separat notis". Placement is free.
- The persistence disclosure (`pages.auth.passwordless.persistence`) is security-auditor's
  D4 point 2 ("kravet uppfylls om — och bara om — persistensen uppges där handlingen
  görs") and design-reviewer's Blocker B3: directly above the primary button on both
  session-creating steps, never behind a link. ADR 0142 D4 says "30 is the number the
  copy leads with" while its bound string leads with 180 — an inconsistency inside
  ADR 0142 that the sweep's security round resolves.
- The resting copy on `/logga-in/kod` (`pages.auth.passwordless.code.resting` and
  `codeHint`) is enumeration-safe by design: the page never states what the system did,
  only what the user should do, because the backend answers alike whether or not an
  address has an account (ADR 0142 Page form; security-auditor on #1779 and #1786).
- The Art. 14 blocks in the no-account mails
  (`src/Jobbliggaren.Infrastructure/Email/EmailTemplates.LoginChallenge.cs`:
  `NoCredentialBasis*`, `ControllerRightsAndComplaint*`, the retention sentences) are
  security-auditor's conditions from the #1737 form round ("ovillkorat", "exakt").
- The mail footer line and the 16px rendering of the code come from DESIGN.md §11.5's
  type scale (22/16/14), which has no rung for a code; the footer entered with #183
  (2026-08-12).

### Volume

Measured 2026-09-24 at `23d38792` in `web/jobbliggaren-web/messages/sv`, namespaces
outside `content-*`: 2 367 keys / 12 873 words, of which 304 explanatory-type keys
(last key segment `hint|help|lede|intro|description|desc|note|explainer|explanation|info|body|p1-9|caption|subtitle|sub|tip|footnote|legend|helper`)
/ 4 176 words; `pages.auth.*` alone 153 keys / 987 words; the email format hint at
5 sv + 5 en sites. Regenerate from `web/jobbliggaren-web`:

```
python - <<'EOF'
import json, glob, os, re
pat = re.compile(r'(hint|help|lede|intro|description|desc|note|explainer|explanation|info|body|p[1-9]|caption|subtitle|sub|tip|footnote|legend|helper)$', re.I)
def walk(p, v, out):
    if isinstance(v, dict):
        for k, x in v.items(): walk(p + '.' + k if p else k, x, out)
    elif isinstance(v, str): out.append((p, v))
tk = tw = ek = ew = 0
for f in sorted(glob.glob('messages/sv/*.json')):
    if os.path.basename(f).startswith('content-'): continue
    rows = []; walk('', json.load(open(f, encoding='utf-8')), rows)
    ex = [(k, v) for k, v in rows if pat.search(k.split('.')[-1])]
    tk += len(rows); tw += sum(len(v.split()) for _, v in rows)
    ek += len(ex); ew += sum(len(v.split()) for _, v in ex)
print(tk, tw, ek, ew)
EOF
```

## Decision

1. **DESIGN.md §8 carries the rule** "Copy-täthet: det självklara förklaras inte": seven
   one-sentence, yes/no gradeable rules and the ADR 0047 tie-break (deleting a sentence
   may never leave the task a guess; the finding is then a shorter sentence or a better
   label/button). The rule text lives there and is not restated here.
2. **Carrier.** DESIGN.md is canonical (ADR 0003 Alt B: DESIGN.md owns philosophy and
   pedagogy, the skills own the detailed spec; `design-reviewer`'s authority is
   DESIGN.md). The four design skills carry examples and pointers, never restatements
   (#1173). `design-reviewer` grades excess copy as a **Major** (area 4, the severity
   table) and applies the tie-break in area 5; `nextjs-ui-engineer` carries the builder
   form. `docs/spec-rationale.md` is keyed to CLAUDE.md/AGENTS.md only, so this ADR is
   the derivation's home.
3. **What this supersedes, for the density question only:** ADR 0142's Page form
   requirement of a body sentence on `/logga-in` and its `email-hint` row
   (design-reviewer #1738 form round Q6 points 2-3, 2026-09-21). Every other Page-form
   binding stands. The sweep (#1824) amends ADR 0142's Page form with the form as
   delivered and the corrections it makes.
4. **Legally bound strings (DESIGN.md §8 rule 7)** — shortened only with
   `security-auditor`'s signature, never deleted, and never re-argued in a PR body:
   1. the Art. 13 line under the `/logga-in` email field
      (`pages.auth.passwordless.entry.privacyHint`); placement at the collection point
      is free;
   2. the persistence disclosure on `/logga-in/kod` and `/logga-in/villkor`
      (`pages.auth.passwordless.persistence`); stays directly above the primary
      button, never behind a link; the 30-vs-180 inconsistency is hers to resolve;
   3. the enumeration-safe resting copy on `/logga-in/kod`
      (`pages.auth.passwordless.code.resting`, `code.codeHint`): it may be shortened
      but must still never state what the system did;
   4. the Art. 14 blocks in `EmailTemplates.LoginChallenge.cs` (`NoCredentialBasis*`,
      `ControllerRightsAndComplaint*`, the retention sentences) in
      `LoginRegistrationClosed`, `LoginNewAccountCode` and
      `LoginNewAccountCodeLimitReached`;
   5. the consent step's `pages.auth.passwordless.consent.termsLabel` and
      `privacySibling` (ADR 0142 D6 version stamp) — added by this exploration, for
      `security-auditor` to confirm in #1824.
   The `content-*` namespaces (legal texts, FAQ, guides) are outside every sweep.
5. **Three contradictions fixed in the same PR:** the empty state was defined three ways
   (principles: one sentence; copy: statement + next step; components: title +
   explanation + action) and is now one statement + one action, at most two short
   sentences inline, title + action as an Alert; DESIGN.md §6 still allowed the auth
   format placeholder the ADR 0038 amendment abolished; the components skill's
   `references/variants-full.md` still said auth format placeholders "behålls".
6. **Klas's three answers of 2026-09-24 (never re-ask):**
   1. *"Regel först, sedan autonomt svep."* The rule lands first (this ADR, #1823); the
      FE sweep (#1824) follows after #1742 releases `hotspot:i18n`; the BE mail PR
      (#1825) runs in parallel. Klas reviews dev post-merge — no per-row questions, no
      inventory for him to tick.
   2. *"Sex rutor, riv ADR-raden."* The six-digit code field becomes six visual boxes
      over `input-otp`'s single real `<input>` (#1826). That reverses ADR 0142's Page
      form M3 and is recorded as an ADR 0142 amendment in that PR, not here.
   3. *"Egen designomgång inom DESIGN.md"* for `/ansokningar` (#1827). ADR 0140's
      card-grid exception reaches no other page and is untouched.

## Alternatives considered

### DESIGN.md §8 as canonical, an ADR for the derivation — selected

The rule is philosophy-grade (it changes what "Direkt" means) and must be gradeable;
that is the ADR 0003 split. The derivation, the measurement and the override of
reviewer-bound designs need a tracked home, and DESIGN.md has none for derivations.

### A DESIGN.md-only edit with dated attribution — rejected

The 2026-09-02 silence rule took this form. It leaves no home for the measurement
above and no record that ADR 0142's lede and hint bindings are overridden, so the
sweep PR would have to argue it in prose — the review-discipline failure mode.

### An ADR 0142 amendment — rejected

ADR 0142's amendments record per-part auth delivery. This rule reaches 59 routes and
15 mails. The six-box reversal (#1826) is an ADR 0142 amendment because it reverses
ADR 0142's own Page form.

### AGENTS.md §10 — rejected

§10 holds tone rules; AGENTS.md is byte-budgeted (ADR 0135) and is not the
reviewer's authority.

### An inventory Klas ticks per row — rejected by Klas

Answer 1. His taste is the oracle the rules lacked; the rule now carries it, and the
reviewer grades against it.

## Consequences

### Positive

- The pressure runs both ways: a sentence too many blocks a merge exactly as a
  sentence too few does.
- The sweep has a grading form: `key · rule # · words before/after · bound? · signer`.
- Klas's directive of 2026-09-16 and #1003 are finally rules a reviewer reads.

### Negative

- Every shipped surface is non-compliant until #1824 and #1825 land; the sweep touches
  the i18n hotspot and waits on #1742.
- Rule 7's list must be confirmed by `security-auditor` in #1824 (item 5 is the
  session's, not hers).
- Three named skips remain: the password-era examples in the copy skill (:150/:154) and
  `wcag-criteria.md:200` are #1743's to retire; the hint colour tier
  (`text-text-secondary` in the skills vs `text-text-primary` shipped) is carried as a
  checklist line in #1824; the stale route list in `scripts/visual-verify.ts` and the
  stale table in `docs/runbooks/frontend-visual-verification.md` ride a later session's
  own PR.

## Implementation and acceptance

- PR for #1823: this ADR (promoted with `git add -f`; row in the index), DESIGN.md
  (§1.2 row, §6 placeholder sentence, §8 rule), the four design skills and
  `references/variants-full.md`, `design-reviewer.md`, `nextjs-ui-engineer.md`.
- Acceptance: the old example strings grep to zero across `.claude/`, DESIGN.md and
  this ADR (ADRs up to 0143 are immutable records); `design-reviewer.md` names
  DESIGN.md §8 in area 4, area 5 and the Major row; the parity guard
  (`.github/scripts/codex-agent-parity-guard.sh`) passes unchanged, since a text edit
  needs no stub change; the delta is `.md` only, so DoD #4 (rendered states) does not
  fire.
- Panel: `dotnet-architect` + `code-reviewer` (spec edit, CLAUDE.md §9.2),
  `design-reviewer` (her own charter and DESIGN.md), `security-auditor` scoped to rule 7
  and the list in Decision 4.
- Then #1825 in parallel, #1824 when #1742 merges, #1826 after #1824, #1827 and #1828
  as design rounds.

## References

- DESIGN.md §8 (the rule), §11.5 (mail), §1.2, §6.
- `.claude/skills/jobbpilot-design-{copy,components,a11y,principles}/SKILL.md`,
  `.claude/agents/design-reviewer.md`, `.claude/agents/nextjs-ui-engineer.md`.
- ADR 0003 (skills own the detailed spec), ADR 0038 amendment 2026-05-17 (no
  placeholders), ADR 0047 (task completion without guessing), ADR 0140 (the `/oversikt`
  grid reaches no other page), ADR 0142 (Page form; D4; D6).
- #1003, #1822–#1828, #1742, #1743, #1626 (the DESIGN.md-only rule PR precedent).
- Local-only reviews cited above live in `docs/reviews/` of the main checkout and are
  synced into worktrees by `.worktreeinclude`; they are not on any branch.
- `input-otp` 1.5.0 read 2026-09-24: npm, github.com/guilhermerodz/input-otp and
  ui.shadcn.com/docs/components/input-otp — one real `<input>` with
  `autocomplete="one-time-code"`, visual slots drawn on top.
