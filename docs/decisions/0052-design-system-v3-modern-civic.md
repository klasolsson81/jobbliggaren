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

## Amendment 2026-07-27 (#1095) — `.jp-*` control heights minuted; input radius row corrected

**Datum:** 2026-07-27
**Källa:** senior-cto-advisor binding decision (`docs/reviews/2026-07-27-control-heights-cto.md`, Option 3 bound). Klas delegated the decision to the CTO (CLAUDE.md §9.2: unambiguous CTO verdicts execute without extra Klas GO).
**Trigger:** Issue #1095. A corpus audit flagged `.jp-btn` (44px) and `.jp-input` (48px) as apparent drift against ADR 0038's 44px/40px. Verified false: both values are minuted in `docs/handoff-oversikt/HANDOVER-v3.md` §5.1 (Buttons) and §5.2 (Inputs) — the authoritative v3 design spec this ADR's own Livscykel-not (top of file) says its Beslut substance was transcribed from, whose header (line 3) reads verbatim: *"Beslutat av produktägaren (Klas). Designspec har veto över befintliga ADRs och CC-default-preferenser."* Beslut 4 (radius) and Beslut 5 (typography) transcribed HANDOVER's corresponding rows; the height rows in §5.1/§5.2 and the input-radius value in §5.2 were dropped from that same transcription. This is an incomplete transcription, not an unminuted decision.
**Beslutsfattare:** senior-cto-advisor (decision-maker, CLAUDE.md §9.2); Klas Olsson (delegated the choice to the CTO)
**Status:** Accepted. Additive — Beslut 4 and Beslut 5's tables are not rewritten (ADR immutability, Nygard 2011); this amendment supplies the rows `HANDOVER-v3.md` §5.1/§5.2 minuted that the original transcription omitted, and corrects one mis-transcribed row in Beslut 4.

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

### Input radius correction (Beslut 4)

Beslut 4's table assigns `--jp-r-sm` (4px) to "inputs, badges". `HANDOVER-v3.md` §5.2 states *"Radius 6 px"* for inputs, and the shipped code (`globals.css` `.jp-input`) runs `border-radius: var(--jp-r-md)` — 6px, matching HANDOVER. Beslut 4's inputs row is corrected: **inputs use `--jp-r-md` (6px)**; `--jp-r-sm` (4px) applies to **badges only**.

No CSS change — the code already matched `HANDOVER-v3.md`; only the Beslut 4 table row was mis-transcribed. `--jp-r-sm` is untouched and remains in active use (41 consumers measured across `globals.css` and `app.css`) for badges and other 4px surfaces — it is simply not the input radius.

### Cross-reference

- ADR 0038 — dated forward-pointer added 2026-07-27 (#1095) in its "Relation till andra ADR:er" section, pointing here.
- `docs/reviews/2026-07-27-control-heights-cto.md` — full CTO reasoning, rejected alternatives, trade-offs accepted.
- `docs/handoff-oversikt/HANDOVER-v3.md` §5.1 (line 208–220), §5.2 (line 222–229), §1 (line 60).
- `docs/decisions/README.md` — index summary lines for ADR 0038 and ADR 0052: docs-keeper to add a reference to this amendment.
