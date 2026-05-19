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
