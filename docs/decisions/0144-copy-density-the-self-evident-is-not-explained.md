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
  Q6, local-only), citing ADR 0047, and written into
  ADR 0142's Page form. Neither was argued on its own merits; the hint was a reuse of
  the skill's example.
- The Art. 13 line (`pages.auth.passwordless.entry.privacyHint`) is security-auditor's
  Major 8 (`docs/reviews/2026-09-17-auth-epic-security.md:117`): the notice moves with the
  collection point, "ingen ny separat notis", under the email field.
- The persistence disclosure (`pages.auth.passwordless.persistence`) is security-auditor's
  D4 point 2 ("kravet uppfylls om — och bara om — persistensen uppges där handlingen
  görs") and design-reviewer's Blocker B3: directly above the primary button, never behind a link. ADR 0142 D4 says "30 is the number the
  copy leads with" while its bound string leads with 180 — an inconsistency inside
  ADR 0142. security-auditor resolved it on PR #1829: the string stands ("upp till 180
  dagar" is the only single number true of every device), the subclause is true of the
  cookie policy and false of the string, and #1824's ADR 0142 amendment strikes it.
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
(last key segment `hint|help|lede|intro|description|desc|note|explainer|explanation|info|body|p[1-9]|caption|subtitle|sub|tip|footnote|legend|helper`)
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

1. **DESIGN.md §8 carries the rule** "Copy-täthet: det självklara förklaras inte":
   one-sentence, yes/no gradeable rules and the ADR 0047 tie-break (deleting a sentence
   may never leave the task a guess or hide an irreversible action's consequence; the
   finding is then a shorter sentence or a better label/button). The rule text lives
   there and is not restated here.
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
4. **Legally bound strings (DESIGN.md §8 rule 7)** — shortened or moved only with
   `security-auditor`'s signature, never deleted, and never re-argued in a PR body. The
   class is rule 7's (consent and withdrawal under Art. 7, information under Art. 13 and
   14, #1003's exception). The set below is her confirmation of 2026-09-24 on PR #1829
   (`docs/reviews/2026-09-24-copy-density-security-auditor.md`, local-only), sv and en
   alike; #1824 (catalogue strings) and #1825 (mail templates) are graded against it, and
   later additions land as amendments here:
   1. `pages.auth.passwordless.entry.privacyHint` — the whole line; visible without a
      click; stays in the email field's `aria-describedby` (auth-epic Major 8 → ADR 0142
      D6; Art. 13(1), 12(1)). It may stand anywhere in the collection form, under the
      button included, on those two conditions; she signs such a move with one line in #1824.
   2. `pages.auth.passwordless.persistence` — that one stays logged in on the device;
      "upp till 180 dagar", never 30 alone; Logga ut on every page; directly above the
      primary button (D4 point 2 + design B3; ePrivacy 5(3)/WP194). A
      shortened string may add 30 as the inactivity limit but never lead with it.
   3. `pages.auth.passwordless.code.resting` (the conditional clause), `code.resend.receipt`,
      `code.expired`, `code.burned` — never state or presume that a code or link was sent;
      name no cause the uniform answer hides; show no address except `code.youEntered`,
      and never inside a sentence about mail (ADR 0142 Page form + Amendment 2026-09-21 (3);
      #1779 Minor 1; #1738 B; Art. 5(1)(a), 12(1)). `code.codeHint` is not bound.
   4. `pages.auth.passwordless.consent.termsLabel`, `consent.privacySibling` — the
      acceptance covers the terms and only them; the privacy policy is a sibling sentence,
      never inside the acceptance; from part 6a this is the collection notice on the OAuth
      path (auth-epic Major 8 → D6 + Page form; Art. 6(1)(b), 13, 5(2)).
   5. `pages.auth.passwordless.link.alreadyLoggedIn.body`, second sentence — says what
      "fortsätt" does; no address (#1738 M-2 + Klas "(a) Två knappar"; Art. 32(1)).
   6. `resumes.consent.*` — versioned consent; a material change bumps
      `PnrConsentDialog.Version` in the same PR (CV-pivot 5b security bind B6; Art. 7(1)–(2),
      87 + DSL 3:10). No test pins the copy to the version, so the bump is the sweep's duty.
   7. `settings.backgroundMatch.intro` + `.toggleDescription` — what the consent covers,
      and that it can be withdrawn, which only `intro` says (ADR 0080 flip gate 3;
      Art. 7(2)–(3), 13(2)(c)).
   8. `settings.followedCompanyNotifications.toggleDescription` — what the consent covers,
      plus "Du kan dra tillbaka samtycket …" (ADR 0087; Art. 7(2)–(3)).
   9. `jobads.applicationHistory.incompleteNote`, and the word "minst" in
      `applicationHistory.applicationCount`, `ui.card.previousApplications` and
      `ui.detail.previousApplications` — the notice is visible above the content in both
      branches, never behind help; the floor "minst" stays (#824 PR 4, #858, #1003;
      Art. 5(1)(a)/(d)).
   10. `jobads.ui.detail.recruiterNoticeLink`, `applications.ui.preservedAd.recruiterNoticeLink`
      — the link, shown with the contact block (#842 CTO rebind R5, #842 PR 4; Art. 14(5)(b)).
   11. `pages.sokningar.lede` — the retention sentence and the policy reference; may move
      into the ?-help (ADR 0060 says "hjälptext"), never be struck (ADR 0060 mechanics
      note 6; Art. 13(1)(c)/(2)(a)).
   12. `settings.account.delete.{description,mailOff,contactRoute}`,
      `pages.auth.passwordless.notice.accountDeleted.body` — the 30-day window; the
      kontakt@ address where self-service ends (#1740 form round; Art. 12(2)–(3), 17).
   13. `LoginRegistrationClosed`, `LoginNewAccountCode`, `LoginNewAccountCodeLimitReached`
      and `LoginAddressChangeCode` in `EmailTemplates.LoginChallenge.cs` — the whole
      class-(3) notice: the ground paragraph (`NoCredentialBasis*`, NewAccountCode's own
      paragraph per condition 20, AddressChangeCode's own paragraph with the 14(2)(f)
      category sentence), the retention paragraph, `ProcessorPlain/Html` and
      `ControllerRightsAndComplaint*` (#1735 Q20.2, #1737 B3 + conditions 19–21, #1739 Q8,
      #1795; Art. 14(1)–(2)).
   14. `MatchNotification`, `FollowedCompanyNotification` in `EmailTemplates.cs` — the
      "Du får detta för att …" paragraph and the link to the settings (docblock
      "OBLIGATORISK (GDPR Art. 7(3))"; #1740; Art. 7(3)).
   15. `LoginReauthenticationCode` (what the code unlocks + "Om det inte var du är någon
      annan inloggad … Skriv till oss"), `EmailChangedNotification` ("Om du inte känner
      igen ändringen …"), `PasswordChangedNotice` while the template exists — the
      detection channel (ADR 0142 D5, #679 CTO bind 4, #1740 Minor 5; Art. 32(1)).
   16. the subject and preheader of every code-bearing template — the code never appears
      there (#1737 condition 22; Art. 32(1)).
   Not bound, strikable without her signature: `code.codeHint`; in `resting` the sentence
   "Kontrollera inkorgen och skräpposten", and "Koden gäller i 15 minuter" (if kept it must
   match `ChallengeTtl`); `entry.lede`; `entry.emailHint`; the opening sentence of the
   class-(3) mails; and the free-standing "Om det inte var du behöver du inte göra något."
   in the class-(1) mails and in `LoginRegistrationClosed`/`…LimitReached` — a reassurance
   that §8 rule 6's carve-out and the Klas requirement in `EmailTemplates.cs` decide, not
   she. The `content-*` namespaces need no row; the cookie policy's persistence sentence
   lives there.
5. **Three contradictions fixed in the same PR:** the empty state was defined three ways
   (principles: one sentence; copy: statement + next step; components: title +
   explanation + action) and is now one statement + one action, at most two short
   sentences, in the delivered inline form (never a `role="alert"` box); DESIGN.md §6 still allowed the auth
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
- Rule 7 names the class; Decision 4 is `security-auditor`'s confirmed set of 2026-09-24,
  and #1824 and #1825 are graded against it.

## Implementation and acceptance

- PR for #1823: this ADR (promoted with `git add -f`; row in the index), DESIGN.md
  (§1.2 row, §6 placeholder sentence, §8 rule), the four design skills and
  `references/variants-full.md`, `design-reviewer.md`, `nextjs-ui-engineer.md`.
- Acceptance: the old hint-example strings grep to zero across `.claude/`, DESIGN.md and
  this ADR (ADRs up to 0143 are immutable records; `namn@exempel.se` is allowed in an
  error-message example, never in a hint); `design-reviewer.md` names
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

## Amendment 2026-09-24 — #1825: the mail rows as signed

`security-auditor` signed the shortened mail texts in #1825's pre-code form round on
2026-09-24 (her report, `docs/reviews/2026-09-24-1825-form-security.md`, local-only, like
the #1829 report Decision 4 cites); the shortenings remove words and no element, and the
texts themselves live in the templates, each changed block pinned word for word in both
parts of its mail. Four elements join Decision 4's set:

- Row 12: and in `LoginPendingDeletion`, the earliest permanent-deletion date and the
  restore route via kontakt@
- Row 13: and AddressChangeCode's line to a recipient who did not ask ('Bortser du från
  meddelandet ändras ingenting: adressen kopplas aldrig till kontot.'), the counterpart
  of condition 20's last sentence
- Row 15: `EmailChangedNotification` (its subject/h1, since #1825 the only statement of
  the event, and 'Om du inte känner igen ändringen …')
- Row 16: the subject and preheader of every code-bearing template, and the first
  paragraph of its plain part — the code never appears there (#1737 condition 22;
  #1825; Art. 32(1))

## Amendment 2026-09-25 — #1824: the catalogue rows as signed

`security-auditor` ruled on the catalogue sweep in #1824's pre-code form round on 2026-09-25 (her report,
`docs/reviews/2026-09-25-1824-form-security.md`, local-only). She signed the shortened texts of rows 2, 3, 5 and
11, Swedish and English; each keeps every element its row lists. Three elements join Decision 4's set:

- Row 8: and the first sentence of `settings.followedCompanyNotifications.intro` ('Nya annonser från företag du
  följer visas alltid i appen, oavsett vad du väljer här.'), which separates the contract channel (the app) from
  the consent channel (email); without it the toggle reads as governing every notice (Art. 4(11), 7(2))
- Row 12: and `pages.auth.passwordless.outcome.pendingDeletion.body` with the `{date}` in its `title`, the
  kontakt@ route and the permanent-deletion date, the in-app counterpart of `LoginPendingDeletion`'s elements
  (Art. 12(2)–(3), 17)
- Row 17: `pages.foretag.criteria.browse.source`, `pages.foretag.sok.source` and the SCB half of
  `pages.foretag.criteria.ads.source`, the whole line; the ground is the DPIA measure C-D2/M-D2 (attribution on
  every surface that shows SCB company data) and SCB's terms (ADR 0091), not Art. 14, since the register holds no
  natural person; the Platsbanken half is not hers

Every catalogue row of Decision 4 is pinned whole, sv and en, in
`web/jobbliggaren-web/src/lib/i18n/legally-bound-copy.test.ts`, so a change to a bound string fails that test.

## Amendment 2026-09-26 — #1828: the card and detail rows as signed, row 10's reading, row 18

`security-auditor` signed #1828's texts in its form round (her report, `docs/reviews/2026-09-25-1828-form-security.md`,
local-only, with its follow-ups of 2026-09-25 and 2026-09-26):

- Row 9: `jobads.ui.card.previousApplications`, sv "Minst {count, plural, one {# tidigare ansökan} other {# tidigare
  ansökningar}} till företaget", en "At least {count, plural, one {# previous application} other {# previous
  applications}} to this employer"; `jobads.ui.detail.previousApplications`, the same with a closing full stop. The
  detail's second sentence ("Sammanställningen kan vara ofullständig.") is struck: it is not bound, since "minst"
  reserves the number on its own. Her element set for any later shortening: the floor word governs the number in every
  ICU branch and in every channel that carries the count (visible text, a description, sr-only text, a `title`); what is
  counted is named as applications; the scope is the employer, in words, in the same text run, never readable as the ad.
  Where the count reaches an accessible description, the description names the whole counter line, never a fragment.
- Row 10: `jobads.ui.detail.recruiterNoticeLink` renders on the detail surface with or without contacts, visible
  without a click, pointing at `/kontaktperson-i-annons`, beside where the contact block stands or would stand. Row 10
  binds that, not position, size or alignment. A move keeps the link outside `RecruiterContactBlock`, which renders
  nothing for `[]`; never behind help, an expander or a tooltip; never in or after the modal foot. For both of row 10's
  keys the line stays at least 13 px, at least 4.5:1 against its surface, and underlined, and a test pins that the link
  renders with and without contacts. #1828 moved it directly after the block, left-aligned, 14 px, in the house link
  colour.
- Row 18 (new): `jobads.ui.contact.derived`, the whole tag ("Från annonstexten" / "From the ad text"), visible beside
  the lead value of every derived contact (#842 CTO rebind R1(b), #842 PR 4; Art. 5(1)(a)/(d)): our extraction is never
  presented as the advertiser's own statement.

Every catalogue row above is pinned whole, sv and en, in `web/jobbliggaren-web/src/lib/i18n/legally-bound-copy.test.ts`.
