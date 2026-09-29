# ADR 0052 — Designsystem v3: modern civic (tokens + typografi + radius + spacing)

**Datum:** 2026-05-19
**Status:** Accepted
**Kontext:** JobbPilot v3 UI-refactor (HANDOVER-v3.md §0–§1, §3). Användartest av v2 (slate-civic, ADR 0037) visade läsbarhets-/avgränsningsproblem för §1.1-målanvändare (55-åriga jobbsökare).
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-flip-GO 2026-05-19)
**Amends:** [ADR 0016](./0016-civic-design-language.md), [ADR 0037](./0037-design-system-v2-slate-dark-mode.md) (radius-golv), [ADR 0038](./0038-typography-recalibration-govuk-readability-floor.md) (typografi-skala/färg)
**Supersedes:** ingen ADR i sin helhet — ADR 0037:s dark-mode-mekanism (`data-theme="dark"`) består oförändrad
**Relaterad:** ADR 0041 (dark-modal-border-token), ADR 0047 (design-reviewer-mandat); design-skills `jobbpilot-design-tokens`, `jobbpilot-design-principles`, `jobbpilot-design-components`; underlag: HANDOVER-v3.md §0–§7 + `jobbpilot-v3.css`

> **Livscykel-/proveniens-not:** Skriven 2026-05-19 av Claude Code (adr-keeper)
> på explicit Klas-begäran — medveten override av CLAUDE.md §9.4
> webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`).
> Besluts-substansen är transkriberad från HANDOVER-v3.md (auktoritativ
> designspec med §0-veto över tidigare ADRs) + senior-cto-advisor-dom Fas 0
> (Beslut 1). Inga nya beslut konstruerade. Status **Accepted** per Klas
> explicit Accepted-flip-GO 2026-05-19.

---

## Kontext

DESIGN.md och design-skills v2 kodifierar en slate-baserad civic-utility-palett
(ADR 0037: `--jp-*`-namnrymd, slate-skala, dark mode via `data-theme="dark"`;
ADR 0038: GOV.UK-läsbarhetsgolv). Användartest med §1.1-målanvändare
(55-åriga jobbsökare) visade att testpersoner inte tillförlitligt kunde avgöra
var ett kort började eller slutade — kontraster och kantmarkeringar var för
svaga för målgruppen (HANDOVER-v3.md §1).

HANDOVER-v3.md är auktoritativ designspec för v3-refactorn och bär §0-veto
över tidigare ADRs. v3 är "modern civic" — referensmål DigID och
australia.gov.au med Platsbankens listrytm. Den civic-utility-tonen från
ADR 0016 bevaras (seriös, pålitlig, ingen AI-estetik), men kontraster,
borders och fält bumpas för avgränsning och läsbarhet.

senior-cto-advisor (Fas 0, Beslut 1) avgjorde token-migrationsstrategin mellan
tre varianter (se Alternativ övervägda).

## Beslut

### Beslut 1 — v3 navy-palett ersätter v2 slate-palett och namnrymd

v2:s `--jp-*` slate-palett **och** namnrymd ersätts av v3 navy-palett som
kanon i `globals.css` `:root` + `[data-theme="dark"]`:

- `--jp-navy-50` … `--jp-navy-900` (primär skala)
- `--jp-surface`, `--jp-surface-2`, `--jp-surface-3` (ytnivåer)
- `--jp-ink-1`, `--jp-ink-2`, `--jp-ink-3` (textnivåer)
- `--jp-border`, `--jp-border-soft`, `--jp-border-strong`, `--jp-border-input`
- `--jp-hero-*` (hero-specifika tokens)

### Beslut 2 — Tailwind 4 `@theme inline`-bryggan behålls som OCP-indirektion

shadcn-konsumentklasser (`bg-surface-primary`, `text-text-primary` m.fl.)
behåller sina semantiska namn men får bridge-alias mot v3-tokens via
`@theme inline`. Detta är avsedd indirektion per Tailwind theme-variables-docs
(Open/Closed-isolering mellan konsument och token-källa), **inte** ett
DRY-brott. shadcn-primitiver överlever paradigmskiftet via bryggan utan
className-omskrivning.

### Beslut 3 — Token-strategi = Hybrid (CTO Variant C)

- Strukturella `.jp-*`-klasser portas **verbatim** från `jobbpilot-v3.css`
  (ingen omtolkning).
- shadcn-primitiver överlever via `@theme inline`-bryggan (Beslut 2).

### Beslut 4 — Radius-golv

| Token | Värde | Användning |
|-------|-------|------------|
| `--jp-r-sm` | 4px | inputs, badges |
| `--jp-r-md` | 6px | rader, kort, knappar |
| `--jp-r-lg` | 8px | modaler |
| `--jp-r-xl` | 12px | **endast** hero |
| pill | 9999 | pills/badges (oförändrat) |

Detta höjer radius-golvet från ADR 0016/0037:s 4px till 6px för
rader/kort/knappar; 12px tillåts uteslutande för hero.

### Beslut 5 — Typografi

- h1 (sidrubrik): 32 / 700
- hero landing: `clamp(40px, …, 56px)` / 700
- hero `/jobb`: 40 / 700
- jobb-/ansökningstitel: 18 / 600 (light) · 700 (dark)
- body: 16 / 400
- mono **endast** för IDs, datum, antal

### Beslut 6 — Färg

- Primärknapp: navy-800 `#0A2647` (kontrast 14:1 på vit — AA-golv passerat
  med marginal)
- Header och auth-kort: vit bg i **båda** teman (scoped token-override,
  medvetet avsteg från global dark-yta)
- Hero-input: alltid vit bg / mörk text oavsett tema

WCAG AA behandlas som **golv, ej mål** (jfr CLAUDE.md §2.5-disciplinen för
mätbara konventioner; ADR 0038-läsbarhetsgolv).

## Konsekvenser

### Positiva

- Pixeltrohet mot v3-prototypen (`jobbpilot-v3.css` portad verbatim).
- Kontrast-/avgränsningsläsbarhet för §1.1-målanvändare åtgärdad — det
  konkreta användartest-fyndet (kortgränser) löses av bumpade
  borders/kontraster.
- shadcn-konsumentkod orörd: bryggan absorberar token-skiftet (OCP).
- Civic-ton från ADR 0016 bevarad — ingen drift mot AI-/trend-estetik.

### Negativa + mitigering

- **Två tokenparadigm samexisterar transient:** v3-kanon + bridge-alias under
  refactorn, samt kvarvarande v2-alias. Mitigering: v2-alias städas i egen
  fas efter grep-verifierad nollkonsumtion (ingen tyst kvarlämning).
- **Bred yta:** globals.css `:root`/`[data-theme]` + design-skills + DESIGN.md
  påverkas. Mitigering: amends mot ADR 0016/0037/0038 + design-skills
  explicitgjorda; dark-mode-mekanismen från ADR 0037 lämnas orörd för att
  begränsa blast radius.

**Amendment 2026-07-26 (#1054) — two Beslut clauses narrowed by measurement.**
No status change, no supersede; the clauses above are left as written.

- **Radie-skalan's `--jp-r-xl` (12px, "endast hero") is removed.** ADR 0068
  made the hero plate 6px (`--jp-r-md`), which left the 12px rung with no
  consumer; the shadcn bridge already capped `--radius-xl` to `--jp-r-lg`.
  ADR 0067's impl-note (line 138) declined a 0052 amendment at the time
  *expressly because the token was preserved in the token layer* — that premise
  no longer holds, which is why this note exists. The canon is now 4 / 6 / 8 /
  pill, and radii above 8px are forbidden outright rather than hero-excepted.
- **The navy scale is `--jp-navy-700` and `-800` only.** The other six rungs of
  "`--jp-navy-50` … `--jp-navy-900` (primär skala)" had zero consumers. The two
  that remain are load-bearing and untouched: they back `--jp-heading-1` /
  `--jp-heading-2` (ADR 0068's E2f amendment, which explicitly revoked the
  earlier plan to clean up the ramp) and the `.jp-brand` logo substrate.

Reintroducing either requires a new decision, not a re-add — the same bar
ADR 0037 §12 documents for dark mode.


## Alternativ övervägda

### Alternativ A — Behåll v2-namnrymd, värdeskifta tokens

Behåll `--jp-*` slate-namn, ändra bara värdena till v3-paletten.

**Avvisat:** lossy mappning (v3-strukturen har fler ytnivåer än v2-namnrymden
rymmer) och bryter Ubiquitous Language — namn skulle ljuga om innehåll.
(Källa: senior-cto-advisor Beslut 1; Martin, *Clean Architecture* kap. 8/14;
Evans, Ubiquitous Language.)

### Alternativ B — Riv Tailwind-bryggan, skriv om alla className

Ta bort `@theme inline`-indirektionen och migrera varje shadcn-konsument
direkt till v3-tokens.

**Avvisat:** maximal risk (varje komponent rörs) för noll pixelvinst —
bryggan är avsedd indirektion, inte teknisk skuld. (Källa: senior-cto-advisor
Beslut 1; Tailwind theme-variables-docs.)

### Alternativ C — Hybrid (valt, se Beslut 3)

Strukturella `.jp-*` portas verbatim; shadcn överlever via bryggan.

**Valt:** lägst risk × högst pixeltrohet. (Källa: senior-cto-advisor Beslut 1.)

## Implementationsstatus

- **Beslut accepterat 2026-05-19** (Klas Accepted-flip-GO).
- Implementation: JobbPilot v3 UI-refactor (F-faser per HANDOVER-v3.md +
  AGENTS.md `pnpm build`-gate). Verbatim-port av `jobbpilot-v3.css` `.jp-*`,
  `@theme inline`-brygg-alias, v2-alias-städning i egen grep-verifierad fas.
- Cross-ref-uppdatering i ADR-index + design-skills sker i refactor-faserna
  (docs-keeper underhåller index efter denna ADR).
- **Transitionellt shim — `.jp-shell-transitional-container` (F1b+F2, CTO
  2026-05-19 B1-reparation):** v3-shellen (`.jp-content`) constrainar
  medvetet ej bredd; v3-sidor wrappar i `.jp-container`/`.jp-page` själva.
  Tills F3/F5/F6 gett alla `(app)`-sidor egna wrappers wrappar `app-shell`
  un-refaktorerade sidor i `.jp-shell-transitional-container` (max-width
  1200 + padding) så de inte renderar edge-to-edge (samma
  branch-by-abstraction-doktrin som v2-token-alias / v2 `.jp-*`-shim).
  **Borttagnings-trigger:** när F3/F5/F6 gett alla `(app)`-sidor egna
  `.jp-container`/`.jp-page` (+ `/jobb`-hero edge-to-edge-opt-out) blir
  containern dubbel-padding och ska bort — verifieras analogt
  v2-alias-städningen (grep `jp-shell-transitional-container`).
- **Rubrik-token-realignment — `--text-h1` 28→32 (#549 WS1, 2026-07-03, CTO
  D1):** on-disk `--text-h1` had drifted to 28px against this ADR's own
  Beslut 5 (32/700) — a pure drift, not a re-decision. Epic #549 WS1
  re-aligns the token **to** the ADR's own documented value, not away from
  it, and collapses the three divergent page-title tiers this drift had
  allowed to co-exist — `.jp-page__title` (32/700), the legacy `.jp-h1` tier
  (28/600, live on ~11 routes), and the auth-page h1 (20/500) — onto the
  **one** `--text-h1` token at 32/700. Beslut 5's h1 spec itself is
  unchanged; only the on-disk drift is closed. Paired with the heading
  *colour* change (ink → navy ramp) in ADR 0068's 2026-07-03 (#549 WS1)
  implementation note, merged in the same PR. Rode CLAUDE.md §12's STOPP
  class (bundled with the E2f-override colour change) — **Klas GO
  2026-07-03, PR #562 merged manually** (never automerge); design-reviewer
  rendered-verify passed the auth-h1 20→32 jump (gate c, round 1).

---

## Amendment 2026-07-27 (#1095) — `.jp-*` control heights minuted; input radius NOT changed (see #1103)

**Datum:** 2026-07-27
**Källa:** senior-cto-advisor binding decision (`docs/reviews/2026-07-27-control-heights-cto.md`, Option 3 bound). Klas delegated the decision to the CTO (CLAUDE.md §9.2: unambiguous CTO verdicts execute without extra Klas GO).
**Trigger:** Issue #1095. A corpus audit flagged `.jp-btn` (44px) and `.jp-input` (48px) as apparent drift against ADR 0038's 44px/40px. Verified false: both values are minuted in `docs/handoff-oversikt/HANDOVER-v3.md` §5.1 (Buttons) and §5.2 (Inputs) — the authoritative v3 design spec this ADR's own Livscykel-not (top of file) says its Beslut substance was transcribed from, whose header (line 3) reads verbatim: *"Beslutat av produktägaren (Klas). Designspec har veto över befintliga ADRs och CC-default-preferenser."* Beslut 4 (radius) and Beslut 5 (typography) transcribed HANDOVER's corresponding rows; **the height rows in §5.1/§5.2 were dropped from that same transcription.** That is an incomplete transcription, not an unminuted decision. (The input RADIUS is a different case — see the section below; it was transcribed correctly and the source contradicts itself.)
**Beslutsfattare:** senior-cto-advisor (decision-maker, CLAUDE.md §9.2); Klas Olsson (delegated the choice to the CTO)
**Status:** Accepted. Additive — Beslut 4 and Beslut 5's tables are not rewritten (ADR immutability, Nygard 2011); this amendment supplies the rows `HANDOVER-v3.md` §5.1/§5.2 minuted that the original transcription omitted. It corrects **nothing** in Beslut 4 — an earlier draft claimed the input-radius row was mis-transcribed; that claim is retracted below and the question is filed as #1103.

### Control heights (completes Beslut 5's scope)

`HANDOVER-v3.md` §5.1 (line 208–220) and §5.2 (line 222–229) minute:

| Class | Height | Source |
|---|---|---|
| `.jp-btn` — `--primary`/`--secondary`/`--ghost`/`--danger` | **44px** | §5.1 table, all four variant rows |
| `.jp-btn--sm` | 36px | §5.1, "Varianter: `--lg` 52 px, `--sm` 36 px" |
| `.jp-btn--lg` | 52px — ratified, unimplemented (zero consumers) | §5.1, same row |
| `.jp-input` / `.jp-select` / `.jp-textarea` | **48px** | §5.2, "Höjd 48 px (sm 40 px)" |
| `.jp-input` sm | 40px — ratified, unimplemented (zero consumers) | §5.2, same line |

These rows extend Beslut 5's scope; the existing Beslut 4/5 tables are left as written.

**Reason** (`HANDOVER-v3.md` §1, line 60): *"v3-justering: behåll civic-tonen men **bumpa kontraster, borders och input-fält**."* This ADR's own Kontext (above) records the same v2 user-test finding — the §1.1 target user (55-year-old jobseekers) could not reliably tell where a card began or ended. The height bump is part of the same contrast/border/field correction that produced Beslut 4 (radius) and Beslut 6 (color), not an isolated, undocumented change.

**ADR 0038's 44px/40px remains live and correct — for the system it governs.** This is a scoping resolution between two ratified decisions, neither deviant:

- ADR 0038 (Accepted 2026-05-16) governs the **shadcn primitives**: `Input` (44px), `Button` (40px; sm 36, lg 44), `SelectTrigger` (44px, sm 36).
- This ADR / `HANDOVER-v3.md` §5.1–§5.2 govern the **`.jp-*` system**: `.jp-btn` (44px), `.jp-input` (48px).
- The two regimes do not share a visual plane: `.jp-input` is used only in `foretag-sok-searchbar.tsx`, its child `bransch-typeahead.tsx`, `cv-upload-form.tsx`, and `activity-report-view.tsx`. The shadcn `Input`s that appear on the same `/foretag/sok` route (`criterion-picker.tsx:88`, `criterion-dialog.tsx:211`) render only inside `CriterionDialog`, which wraps in a Radix `<Dialog><DialogContent>` — a modal overlay, never on the same rendered plane as the page's own `.jp-input` search bar.

**Operative rule, binding on every future corpus line about control height:** name the system a height statement governs. A bare "input height = 44px" (or 48px) is false-by-omission regardless of which number it carries — write "shadcn `Input` = 44px (ADR 0038)" or "`.jp-input` = 48px (ADR 0052, Amendment 2026-07-27)".

`.jp-btn--lg` and `.jp-input` sm are minuted here, unbuilt, so a future implementer does not invent a third number for either rung. Building them now would be premature — zero consumers (YAGNI).

### Input radius — NOT corrected here; the source contradicts itself (open, #1103)

An earlier draft of this amendment called Beslut 4's inputs row a transcription defect of the same
kind as the heights. **That is wrong, and it is recorded here rather than quietly dropped.** Beslut
4's `| --jp-r-sm | 4px | inputs, badges |` is a *verbatim* transcription of `HANDOVER-v3.md` §4
(line 195): *"sm 4 px — inputs, badges"*. It was transcribed correctly.

`HANDOVER-v3.md` disagrees with itself about the input radius:

| source | says |
|---|---|
| §4 line 195 (radius scale) | `sm 4 px — inputs, badges` |
| §1 line 65 (v2→v3 table) | `6 px på rader/kort, **4 px på inputs**` |
| §5.2 line 226 (input spec) | `Radius 6 px` |

Two lines say 4px, one says 6px, and the shipped code (`.jp-input` → `var(--jp-r-md)` = 6px) follows
the minority line. So this is **not** the heights' defect class: there the source was unambiguous and
the transcription lost a row; here the transcription is faithful and the source is inconsistent.

Resolving it requires deciding which HANDOVER line governs — a decision, not a correction — so
Beslut 4 stands unamended and the question is filed. `--jp-r-sm` remains in active use (42
occurrences across `globals.css` and `(app)/app.css`) and is untouched either way.

No CSS change either way: the code ships 6px today and stays there until #1103 rules.

### Cross-reference

- ADR 0038 — dated forward-pointer added 2026-07-27 (#1095) in its "Relation till andra ADR:er" section, pointing here.
- `docs/reviews/2026-07-27-control-heights-cto.md` — full CTO reasoning, rejected alternatives, trade-offs accepted.
- `docs/handoff-oversikt/HANDOVER-v3.md` §5.1 (line 208–220), §5.2 (line 222–229), §1 (line 60).
- `docs/decisions/README.md` — index summary lines for ADR 0038 and ADR 0052 updated in the same PR (an instruction left in an ADR is an instruction nobody runs).

---

## Amendment 2026-09-29 (#1914) — a chip-only radius token, `--jp-r-chip` (19px), inside the pill exception

**Datum:** 2026-09-29
**Källa:** Klas Olsson's answer of 2026-09-29 (AskUserQuestion, with both variants open live in the browser pane, `/foretag/sok` at 320px); design-reviewer's Major 3 on PR #1923 (`docs/reviews/2026-09-29-1923-design-reviewer.md`, gitignored); senior-cto-advisor's routing of that finding (`docs/reviews/2026-09-29-1923-cto.md`, gitignored). The measurements are carried below; the two reports are local, so this record does not depend on them.
**Trigger:** Issue #1914, PR #1923. The PR makes a long chip label wrap inside the chip instead of overflowing its container. A chip that wraps keeps `--jp-r-pill`, and the pill's curve reaches the text. The fix is a radius above `--jp-r-lg`, and the 2026-07-26 amendment above states the canon as "4 / 6 / 8 / pill" and radii above 8px as "forbidden outright", so without this amendment the ADR contradicts the token.
**Beslutsfattare:** Klas Olsson (answer (a)); design-reviewer (the finding and the value); senior-cto-advisor (placement).
**Status:** Accepted. Additive — Beslut 4's table and the 2026-07-26 amendment are left as written (ADR immutability); this amendment adds one chip-scoped token and places it under the pill exception. No status change, no supersede.

### The decision

The question put to Klas, verbatim: *"Får en chip som bryts över flera rader få 19 px-hörn, som en ny radie under pill-undantaget i DESIGN.md §5, i stället för att bli en allt rundare kapsel?"* Option (a) read *"Ja, med token i DESIGN.md §5 och tokens-skillen"*; option (b) read *"Nej, behåll kapseln: kapseln behålls, och texten når ramen från tre rader"*. Klas answered *"(a) Ja, 19 px (Rekommenderat)"*.

The radius canon gains one token, `--jp-r-chip: 19px`, defined in `:root` beside `--jp-r-pill` in `web/jobbliggaren-web/src/app/globals.css` and used only by `.jp-chip`. 19px is half the height of the tallest single-line chip: 38px at ≤768, where the remove/edit button is 32px. CSS scales a radius down to half the box height, so a single-line chip still draws an exact pill (effective radius measured 14.1 / 15 / 19px at chip heights 28 / 30 / 38px), while a chip that wraps keeps 19px corners instead of becoming an ever-rounder capsule.

The name is chip-scoped on purpose and is never a scale step. `--jp-r-xl` was removed by the 2026-07-26 amendment and stays removed; a general step above `--jp-r-lg` would open radii over 8px to every surface.

### Why the pill radius fails on a wrapped chip

With `--jp-r-pill` a chip's radius is half its height, so it grows with every wrapped line: 32px at three lines, 41px at four. design-reviewer estimated, from the glyph box, the clearance between the first glyph on the first and last line and the inside of the border:

| Lines | Clearance, first / last line |
|---|---|
| 2 | +4.8 / +3.2 px |
| 3 | −0.4 / −2.6 px |
| 4 | −6.3 / −8.9 px (`/foretag/sok` at 320px, SNI 46141 / 95400) |

From three lines the curve reaches the text; at four it cuts glyphs. With 19px injected, every two-to-four-line case kept at least 5.6px.

### Alternatives considered

- **(b) Keep the capsule.** Offered to Klas beside (a). Not chosen: the capsule stays and the text reaches the border from three lines (the table above).
- **A general scale step above `--jp-r-lg`** (an `--jp-r-xl`-style rung). Ruled out in senior-cto-advisor's routing when the token was named: it would open radii over 8px to every surface.

### Placement

In-block in PR #1923, by the Common Closure Principle (Martin, *Clean Architecture*, 2017, ch. 13, as senior-cto-advisor cites it): the wrapping and the token change for the same reason, so they ship and revert together. A token in a PR of its own would either be dead or leave the capsule in place.

### What stays true

- The canon "4 / 6 / 8 / pill" stands for every other surface, and radii above `--jp-r-lg` stay forbidden.
- `--jp-r-chip` sits inside the pill exception, the way `.jp-chip`'s pill radius already did (Beslut 4's table: pill, 9999, "pills/badges"). AGENTS.md §5, "radius > 8px except pills/badges", already covers it and is not changed.
- Read with this amendment, the 2026-07-26 sentence "radii above 8px are forbidden outright" leaves the pill exception standing, and `--jp-r-chip` is inside it.

### Consequences

**Positive**

- A wrapped chip keeps its corners: with 19px injected, every two-to-four-line case kept at least 5.6px of clearance, against −0.4 to −8.9px at three and four lines with the pill radius.
- A single-line chip is unchanged: it still draws an exact pill (measured above).
- The ADR, DESIGN.md §5, the tokens skill and `globals.css` now state the same token.

**Negative**

- A component-bound token now sits beside a size scale. senior-cto-advisor accepted that as honest, because the value is derived from the chip's geometry rather than chosen.
- The value rests on that geometry: half of 38px, the tallest single-line chip at ≤768. senior-cto-advisor's condition for the separate change that extends the remove/edit buttons' hit area (Blocker 2 of the same review): the button box stays 24/32px and only its `::after` grows, which is also what keeps 19px at half the height of the tallest single-line chip.

### Cross-reference

- `DESIGN.md` §5 (the border-radius line) and `.claude/skills/jobbpilot-design-tokens/` (`SKILL.md`, `references/tokens-full.md`, `references/theme-block.md`) — updated in the same PR to carry the token.
- `web/jobbliggaren-web/src/app/globals.css` — `--jp-r-chip` in `:root`; `.jp-chip` is its only consumer.
- `AGENTS.md` §5 — unchanged; its "radius > 8px except pills/badges" already covers the token.
- `docs/reviews/2026-09-29-1923-design-reviewer.md` (Major 3) and `docs/reviews/2026-09-29-1923-cto.md` — gitignored; the measurements and the routing are carried above.
- `docs/decisions/README.md` — the ADR 0052 index line updated in the same PR.
