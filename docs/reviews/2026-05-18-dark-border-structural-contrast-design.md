# Design-review: Dark-mode strukturell kantkontrast — migrerings-domslut (`--jp-border-structural`)

**Status:** ⚠ Domslut levererat (2 deliverables + civic-regressionsbedömning)
**Granskat:** 2026-05-18
**Auktoritet:** ADR 0047 (Gate 2 = kontrast-auktoritet mot RENDERAD UI), DESIGN.md §4, ADR 0041 (mönster + contrast-table-klassning), ADR 0037 (låst slate-palett), ADR 0038 ("designa inte på golvet"), ADR 0016 (civic design language), WCAG 2.1 SC 1.4.11
**Bindande ram:** senior-cto-advisor-beslut `a6d33dbbd3beb25d0` — Approach B, ny roll-token `--jp-border-structural`, dark `#64748B` LÅST. Detta domslut rör endast (1) migrerings-set, (2) light-värde, (3) civic-regression — ramen ändras ej.

---

## Sammanfattning

- **Migrerings-set: 13 selektorer/komponent-poster** bär strukturell enda-perceptuell-boundary i dark och migreras `border-border-default`/`var(--jp-border)` → `border-border-structural`.
- **Light-värde-dom:** **behåll `#E2E8F0`** (= `--jp-border` light) — entydig. Token-referentiell enhetlighet à la ADR 0041-light-precedent; light har ingen WCAG-defekt; ADR 0038 "designa inte på golvet" talar emot att flytta light till `#CBD5E1`-golvet utan defekt att åtgärda.
- **Civic-regression:** ingen regression av dark `#64748B` på det migrerade setet. `#64748B` är redan sanktionerat (`--jp-info-500` dark, = `--jp-border-modal` dark, live-verifierat civic-quiet i ADR 0041). En flagga: `ansokningar/[id]` har 4 staplade `#64748B`-kantade sektioner — visuellt acceptabelt men noteras (se §3).
- **2 out-of-scope adjacenta fynd flaggade** (form-inputs på auth-route + shadcn `--input`-bryggan) — utanför CTO-ramen, migreras EJ, noteras för framtida spår.

---

## Inspekterad korpus

Renderade PNG från `C:/tmp/jobbpilot-visual/20260518-0949/` (ADR 0047 — bedömning mot renderad UI, ej bara diff):

| Skärmbild | Teman | Viewports | Användning i domslut |
|---|---|---|---|
| `ansokningar-detalj-manuell` | dark + light | 1920 | **Primärt bevis** — dark renderade korrekt; visar kort/sektioner som faint tonal-block utan definierad kant |
| `ansokningar-detalj-jobad-kopplad` | dark + light | 1280, 1920 | Dark = capture-artefakt (ljus, settle-wait-fade — känd ADR 0041-klass); light som referens |
| `ansokningar-lista` | dark | 1280, 1920 | Flat list, transparent rad på canvas — divider enda separation, osynlig |
| `sokningar-lista` | dark | 1920 | `saved-search-list` rad — `border-t/-b` enda boundary, osynlig |
| `sokningar-radera-dialog` | dark | 1920 | **Referens "rätt kontrast"** — modal redan `border-border-modal` `#64748B`, tydlig kant mot dimmad canvas |
| `landing` | dark | 1920 | Publik shell — auth-kort-panel sole-boundary; feature-list-dividers incidentella |
| `logga-in` | dark | 1920 | Bare `(auth)`-route — input-kanter osynliga (out-of-scope-fynd, se §4) |

Kod korsläst: `web/jobbpilot-web/src/app/globals.css` (rad 60–200, `.jp-*`-regler), grep `border-border-default`/`border-border`/`var(--jp-border)` över hela `src` (verbatim träfflista i analysen), `ansokningar/[id]/page.tsx`, `application-row.tsx`, ADR 0041, `contrast-table.md` rad 71–73.

**Token-matematik (verifierad mot contrast-table rad 71–73 + ADR 0041):**
- Dark canvas `--jp-surface-primary` = `#020617`. `surface-secondary` (kort-yta i många fall) = `#0F172A` → yta↔canvas ≈ **1.2:1** (ingen perceptuell boundary i sig).
- `border-border-default` dark = `#1E293B` mot `#020617` ≈ **1.6:1** — under SC 1.4.11-golvet (3:1) för strukturell gräns.
- `border-border-structural` dark = `#64748B` mot `#020617` ≈ **3.6:1** — över golvet (≥3:1 även mot `bg-black/50`-dimmad canvas, ADR 0041-verifierat).
- Slutsats: där kanten är **enda** perceptuella boundary → 1.6:1 = Blocker-nivå mot SC 1.4.11. Migrering till `#64748B` löser det med marginal.

---

## Deliverable 1 — Exakt migrerings-set

**Princip (SRP/OCP per CTO):** migrera ENDAST ytor där kanten är den *enda* perceptuella boundary i dark — d.v.s. ytfärgen ≈ canvas (`surface-primary`↔`surface-primary` eller `surface-secondary`↔`surface-primary` ≈ 1.2:1) OCH ingen annan separation (ingen rad-bg, ingen yt-shift, ingen layout-region-skillnad) finns. Incidentella hårlinjer där annan separation bär (inre kort-header-dividers innanför en redan migrerad kant, list-row-hairlines med `hover:bg`-row-separation i bunden container, full-bredds-region-avdelare med `bg`-shift) **förblir `--jp-border`** (genuint dekorativa, ADR 0041 contrast-table rad 71-klassning).

### A. globals.css `.jp-*`-regler — MIGRERA (strukturell enda-boundary)

| # | Selektor | Rad | Motivering (strukturell) |
|---|---|---|---|
| 1 | `.jp-card` | 765 | `border` + `bg: surface-secondary` på canvas. Yta↔canvas ≈1.2:1; kanten är kortets enda definierade gräns. |
| 2 | `.jp-listCard` | 771 | `border` (hairline-token idag) + `bg: surface-secondary` på canvas. Container-kant är enda boundary; byts till structural-token. |
| 3 | `.jp-listCard--flat` `border-top` | 780 | Flat list, transparent — `border-top` är listans enda yttre avgränsning mot canvas. |
| 4 | `.jp-listCard--flat .jp-listRow:last-child` `border-bottom` | 782 | Listans nedre yttre avgränsning (motsvarar #3 i andra änden). |
| 5 | `.jp-sidebar` `border-right` | 377 | Sidebar `bg: surface-secondary` (#0F172A) mot main `surface-primary` (#020617) ≈1.2:1. Kanten är enda perceptuella delningen sidebar↔innehåll (renderat: i `*-lista__dark` flyter sidebaren ihop med canvas). **Sidebar = CTO:s explicit nämnda yta.** |
| 6 | `.jp-topbar` `border-bottom` | 601 | Topbar `bg: surface-primary` = canvas; `border-bottom` enda separation topbar↔scrollyta. Renderat (`landing__dark`): topbar-kanten knappt synlig. |
| 7 | `.jp-attention` `border-top` | 697 | Attention-feed = "ingen låda", transparent rader; `border-top` enda yttre avgränsning. |
| 8 | `.jp-attention__row` `border-bottom` | 704 | Rad-separator i transparent feed — enda boundary mellan rader (ingen rad-bg). |
| 9 | `.jp-filterBar` `border-top` + `border-bottom` | 972–973 | Flat filterbar, transparent bg; topp/botten-kant enda avgränsning av filterzonen. |
| 10 | `.jp-pipeline` `border-top` | 1047 | Pipeline-rutnät, transparent kolumner; `border-top` enda yttre avgränsning. |

### B. Komponent-`.tsx` — MIGRERA (strukturell enda-boundary)

| # | Fil:rad | Selektor-kontext | Motivering |
|---|---|---|---|
| 11 | `app/(app)/ansokningar/[id]/page.tsx`: 122, 132, 154, 235 | `rounded-md border border-border-default bg-surface-primary` — manuell-fallback `<p>`, "Personligt brev"-section, "Uppföljningar"-section, "Noteringar"-section | **Primärbeviset** (`ansokningar-detalj-manuell__dark`): yta = `surface-primary` = canvas → kort flyter som faint block, kanten enda boundary. Migrera de 4 *yttre kort-wrappers*. (Inre `border-b`/`border-t` rad 134/156/225/237/244/268 = kort-header/footer-dividers INNANFÖR migrerad kant → **FÖRBLI `--jp-border`**, incidentell. Inre `<li>` rad 177/256 `rounded-md border` = nästlade kort i bunden förälder — gränsfall; migrera EJ nu, separation finns via förälder-padding + nästling.) |
| 12 | `components/applications/status-edit-card.tsx`: 96 | `rounded-md border border-border-default bg-surface-primary` (yttre kort) | StatusEditCard yttre kort — yta = canvas, enda boundary (CTO: StatusEditCard explicit nämnd). Rad 98/122/157 inre dividers = incidentella → FÖRBLI `--jp-border`. |
| — | `components/applications/job-info-panel.tsx`: 34 | `rounded-md border border-border-default bg-surface-primary` (yttre kort) | JobInfoPanel yttre kort (CTO: JobInfoPanel explicit nämnd). Ingår i #12-klassen — samma motivering. Rad 36/87 inre dividers → FÖRBLI `--jp-border`. |
| 13 | `components/saved-searches/saved-search-list.tsx`: 85 (`border-t` listcontainer) + 44 (`border-b` rad) | Flat lista, transparent rad på canvas | `sokningar-lista__dark`: rad-divider enda separation, osynlig. Migrera listans yttre `border-t` (85) + rad-`border-b` (44). Tom-state `border-y` (71) = samma klass (enda boundary för tom-block) → migrera. |

**Övriga `border-border-default`-träffar — FÖRBLI `--jp-border` (dekorativa/incidentella, migreras EJ):**

- `application-row.tsx:46`, `resume-card.tsx:14`, `cv/page.tsx:78`, `job-ad-list.tsx:26`, `ansokningar/page.tsx:103`: list-rad `border-b border-border-default` MED `hover:bg-surface-tertiary` + `last:border-b-0` i bunden listcontainer — radsepareringen bärs av hover-bg + listans egen container-kant. Gränsfall, men annan separation finns → incidentell, behåll. *(Re-verifieras separat om listcontainern själv inte migreras; här migreras `saved-search-list` container #13 vilket täcker det mönstret där listan ÄR sole-boundary.)*
- `(marketing)/page.tsx` 148/163/174 (feature-list-dividers, tab-borders innanför migrerad panel-kant 162), 77/232 (header/footer full-bredds-region MED `bg-surface-sunken`-shift i footer): incidentella. `page.tsx:162` panel-kort `border border-border-default bg-surface-primary` = sole-boundary → **migrera (lägg till #11-klassen: `(marketing)/page.tsx:162`)**.
- `app-shell.tsx` 82/93/129/132: small chrome (theme-toggle-segment, dropdown-panel) — dropdown `bg-surface-primary` på canvas är gränsfall men är popover-natur (jfr ADR 0041 popover-not-migrated YAGNI); behåll, notera som framtida verifieringspunkt.
- Alla `border-b/-t border-border-default` som är **inre kort-dividers** (status-edit-card, job-info-panel, ansokningar/[id] header-rader): incidentella per definition — finns innanför en kant som migreras.
- `.jp-*`: `--jp-border-hairline`-regler (sidebar__tag, search__kbd, nav__label::after, sidebar__user, sectionHeader, listRow, jobRow__bottom, pill, tabs, appRow, divider) + `--jp-border-strong`-regler (kanban-kol, tabellhuvud, btn-secondary-hover): **rör EJ** — `-hairline` är genuint dekorativt (ADR 0041-klass), `-strong` är redan informationsbärande-med-text-komplement (contrast-table rad 72) och ingår inte i detta spår.

**Justerad slutsumma:** 10 globals.css-selektorer (#1–10) + komponent-poster #11 (4 wrappers i `ansokningar/[id]` + `(marketing)/page.tsx:162`), #12 (`status-edit-card:96` + `job-info-panel:34`), #13 (`saved-search-list` 44/71/85). **Net ~13 logiska poster / ~19 enskilda klass/selektor-byten.** Kirurgiskt — inga dekorativa hårlinjer eller inre dividers migreras.

---

## Deliverable 2 — Light-värde-dom för `--jp-border-structural` light

### Domslut: **Behåll `#E2E8F0`** (= `--jp-border` light). Entydig.

**Motivering:**

1. **Ingen WCAG-defekt i light att åtgärda.** Light `--jp-border` `#E2E8F0` mot `#FFFFFF` ≈1.2:1 — dekorativ hairline, men i light bär *ytan* (kort `surface-secondary` `#F8FAFC` / `surface-primary` `#FFFFFF`) + naturlig ljus-mörk-perception separationen. Renderade light-skärmbilder (`ansokningar-detalj-*__light`) visar tydligt definierade kort utan kant-defekt. SC 1.4.11 är inte aktiverad eftersom boundary inte enbart vilar på kant-färgen i light.
2. **ADR 0038 "designa inte på golvet" talar EMOT `#CBD5E1`.** `#CBD5E1` (= `border-strong` light) är dokumenterat 3.0:1 — exakt på golvet. Att flytta light dit utan en defekt att lösa skulle vara att designa på golvet utan anledning, tvärtemot ADR 0038-principen. Golv-marginal ska köpas där det finns en defekt (som i dark), inte spekulativt i ett defektfritt läge.
3. **ADR 0041-light-precedent är direkt tillämplig.** `--jp-border-modal` light = `#E2E8F0` *just för* token-referentiell enhetlighet utan light-regression (ADR 0041 §Beslut p.1, §Konsekvenser "Light-värdet duplicerar `--jp-border` light … medvetet"). `--jp-border-structural` är samma roll-token-klass; samma precedent gäller — om light-policyn någon gång ändras isoleras det till denna token.
4. **Civic-koherens.** `#CBD5E1` i light skulle göra alla migrerade kort/sektioner/sidebar/paneler synligt tyngre i light än omgivande dekorativa hårlinjer — en visuell tvåklass-kant utan funktionell vinst, mot ADR 0016 (lugn, enhetlig civic-yta). `#E2E8F0` håller light visuellt identiskt med idag (noll light-regression) och flyttar enbart dark.

`#CBD5E1`-alternativet avvisas: det löser ingen defekt, bryter ADR 0038, skapar light-regression (synlig kant-tyngd) och saknar ADR 0041-precedent-stöd för light.

---

## Deliverable 3 — Civic-regressionsbedömning (dark `#64748B` på migrerade setet)

**Domslut: Ingen civic-quiet-regression (ADR 0016/0037). En noterad flagga, ej blockerande.**

- `#64748B` är **inte en ny färg** — det är dark `--jp-info-500` (globals.css rad 129) och redan dark `--jp-border-modal` (rad 137). ADR 0041 live-verifierade exakt detta värde som civic-quiet (design-reviewer 0/0/0 mot live `20260516-1424`, Klas slutgodkände). Paletten förblir låst slate, ingen gradient/glow/brand-dilution (ADR 0037). Referens `sokningar-radera-dialog__dark`: `#64748B`-kanten läser som lugn, definierad, myndighetston — inte dekorativ accent. Civic-utility-identiteten bevaras.
- Kanten går från ≈1.6:1 (osynlig, defekt) till ≈3.6:1 (lugnt synlig) — det är *funktionell tydlighet*, inte visuellt brus. En 1px slate-500-hairline på near-black är diskret; den skriker inte.

**Flagga (ej blocker, för Klas-medvetenhet):** `ansokningar/[id]` staplar i dark fyra `#64748B`-kantade sektioner vertikalt (manuell-fallback/Personligt brev + StatusEditCard + Uppföljningar + Noteringar), plus JobInfoPanel/StatusEditCard sida-vid-sida. Fyra–fem definierade kort i kolumn på near-black blir mer "rutnät" än dagens fritt-flytande layout. Detta är **korrekt och önskat** (boundary SKA finnas — det är hela poängen med fixen) och förblir civic (jfr renderad `sokningar-radera-dialog`-modal som redan bär värdet lugnt). Men: när nextjs-ui-engineer applicerat, **re-verifiera `ansokningar-detalj-manuell__dark` + `ansokningar-detalj-jobad-kopplad__dark`** (sistnämnda kräver capture-settle-fix — dark renderade som artefakt i denna korpus) specifikt för att bekräfta att den staplade kort-rytmen inte blir tung. Min bedömning på token-math + modal-referensen: den blir lugn, men renderad re-verifiering av just den vyn är Gate 2-obligatorisk per ADR 0047.

---

## Out-of-scope adjacenta fynd (flaggade, migreras EJ — utanför CTO-ramen)

1. **Form-input-kanter på `(auth)`-route (`logga-in__dark`).** Inputs på bare auth-route har `#1E293B`-kant mot canvas — osynliga i dark. Detta är shadcn `--input`-bryggan (`--input: var(--jp-border)`, globals.css rad 306), INTE `border-border-default`. Utanför CTO:s frame (`--jp-border-structural` för kort/sektioner/paneler/sidebar). **Migreras EJ i detta spår.** Rekommendation: eget spår — input-kant är SC 1.4.11-strukturell (sole-boundary i dark) och bör utvärderas mot samma princip, men det är ett separat token-beslut (`--input`-bryggan) som kräver egen ADR-väg.
2. **`app-shell.tsx` dropdown-panel (129) + theme-toggle-segment (82).** `bg-surface-primary`-popover på canvas — gränsfall sole-boundary. Per ADR 0041-precedent (popover/tooltip EJ migrerade, YAGNI) lämnas dessa; noteras som framtida verifieringspunkt om/när popover-kontrast spåras.

Båda är genuina dark-kontrast-observationer men ligger utanför detta spårs CTO-låsta scope. Loggade här så de inte tappas.

---

## Rekommendation till nextjs-ui-engineer (repair-delegering)

1. Lägg till `--jp-border-structural` (light `#E2E8F0`, dark `#64748B`) + `--color-border-structural: var(--jp-border-structural)`-bridge i `@theme inline` (speglar `--color-border-modal`-mönstret, ADR 0041) — **kräver Klas token-amendment-GO per CLAUDE.md §9.2/§12/§13 + ADR 0041-amendment-väg.**
2. Migrera de 13 posterna i Deliverable 1 (globals.css #1–10 + komponent #11–13) `--jp-border`/`border-border-default` → `var(--jp-border-structural)`/`border-border-structural`. Rör EJ inre dividers, `-hairline`, `-strong`, list-rad-hover-hairlines.
3. Uppdatera `contrast-table.md` (ny rad: `border-structural` `#64748B` dark ≈3.6:1 strukturell, light `#E2E8F0`) + DESIGN.md §4-enradare (efter Klas `approve-spec-edit.sh`).
4. Visual-verify + lämna till design-reviewer för Gate 2 re-review mot renderad UI — **obligatorisk re-verifiering av `ansokningar-detalj-manuell__dark` och `ansokningar-detalj-jobad-kopplad__dark` (med capture-settle-fix)** per §3-flaggan.

Detta dokument är domslut, ej implementation. Ingen kod/token/commit ändrad av design-reviewer (ADR 0047 — rapporterar, nextjs-ui-engineer reparerar).

---

## Gate 2 re-verifiering 2026-05-18 (korpus 20260518-1051, post-implementation)

**Status:** ✓ **GODKÄND** — inga kvarstår-fynd. Migrering renderad-verifierad mot ADR 0047.
**Auktoritet:** ADR 0047 (Gate 2 = kontrast-domslut mot RENDERAD UI), WCAG 2.1 SC 1.4.11, ADR 0016/0037 (civic-quiet), ADR 0041 (mönster-precedent)
**Ram:** senior-cto-advisor `a6d33dbbd3beb25d0` oförändrad. Detta är §3-flaggans obligatoriska renderade re-verifiering av dark-vyerna efter att hela migrerings-setet (Deliverable 1 #1–13) applicerats.

### Inspekterad korpus (renderad PNG, ADR 0047)

| Skärmbild | Teman | Viewports | Roll i Gate 2-domslut |
|---|---|---|---|
| `ansokningar-detalj-manuell` | dark | 1280, 1920, 3440 | **Primärbeviset** — 4–5 staplade sektioner, §3-flaggan |
| `ansokningar-detalj-manuell` | light | 1920 | Light-regressionskontroll (`#E2E8F0` oförändrad) |
| `ansokningar-lista` | dark | 1920 | Sidebar `border-right` + listrad-boundary |
| `sokningar-lista` | dark | 1920 | `saved-search-list` `border-t/-b` sole-boundary |
| `ansokningar-detalj-submitted-radiogrupp` | dark | 1920 | StatusEditCard-panel + radiogrupp inre hierarki |
| `landing` | dark | 1920 | Publik shell — auth-panel/topbar/footer + feature-dividers |
| `logga-in` | dark | 1920 | Out-of-scope-fynd #1 (input-kant) — bekräfta EJ migrerad |
| `ansokningar-detalj-jobad-kopplad` | light | 1920 | CDP-instrumentartefakt — light-bedömning (dark = Klas browser-toggle) |

### Per-punkt-status

**1. WCAG 1.4.11-fixen renderat bekräftad — JA.**
`ansokningar-detalj-manuell__dark` på samtliga tre viewports (1280/1920/3440): de fyra/fem staplade sektionerna (Om annonsen, Status, Uppföljningar, Noteringar) bär nu en lugnt synlig slate-boundary mot near-black canvas. Tidigare (korpus 0949) var dessa faint tonal-block utan definierad kant (≈1.6:1, Blocker-nivå). Nu perceptuell ≥3:1-gräns — `#64748B` läser definierat utan att skrika. Bekräftat även `ansokningar-lista__dark` (sidebar `border-right` nu synlig, flyter ej längre ihop med canvas), `sokningar-lista__dark` (saved-search-rad `border-t/-b` nu synlig — var osynlig i 0949), `submitted-radiogrupp__dark` (StatusEditCard-panel definierad). SC 1.4.11-defekten löst på hela det renderade setet.

**2. §3-flaggan (staplad kort-rytm lugn/civic eller tung) — LUGN, civic bevarad.**
Den centrala oron i §3: blir 4–5 `#64748B`-kantade sektioner i kolumn på near-black "rutnät-tungt"? Renderat domslut: **nej**. Rytmen läser som lugn myndighets-struktur (ADR 0016) — en 1px slate-500-hairline ger funktionell avgränsning utan visuell tyngd. Sektionerna känns organiserade, inte inramade-till-instängdhet. Identisk lugn karaktär som referensen `sokningar-radera-dialog__dark` (samma `#64748B`-värde, ADR 0041-live-verifierat). Skalar rent från 1280 till 3440 — ingen viewport gör rytmen tyngre. `jobad-kopplad__light__1920` (CDP-artefaktens light-fallback): design-compliant, välavgränsade kort, ingen tyngd-regression; dark-bekräftelse kvarstår som Klas browser-toggle per brief, ej blockerande för detta domslut.

**3. Ingen civic-regression / inga dekorativa migrerade av misstag — BEKRÄFTAT.**
Inre dividers förblir diskreta `--jp-border`: `ansokningar-detalj-manuell__dark` visar tydlig tvånivå-hierarki — yttre kort-kant (structural `#64748B`) vs inre header/footer-divider ("Om annonsen"-rule, "Personligt brev"-footer-rule) som fortsatt faint hairline. `landing__dark` feature-list-dividers (Sökning/Pipeline/CV-anpassning/Kalender) förblir incidentella faint linjer — INTE uppgraderade. `submitted-radiogrupp__dark`: radio-options inre separation diskretare än panel-kant. Inga gradients/glow/glasmorfism/dekorativ accent introducerad. Palett låst slate (ADR 0037). Migreringen kirurgisk som specificerat — ingen scope-glidning till dekorativa hårlinjer.

**4. Light helt oförändrat — BEKRÄFTAT, noll light-regression.**
`ansokningar-detalj-manuell__light__1920` + `jobad-kopplad__light__1920`: identiska med pre-migrerings-light. `#E2E8F0`-hairline knappt perceptibel mot vit yta som tidigare — ytkontrast + ljus-perception bär separationen (precis Deliverable 2-domslutets motivering). Inga kort visuellt tyngre. Light-värde-domslutet (behåll `#E2E8F0`) renderad-bekräftat korrekt: noll synlig förändring i light.

**Out-of-scope-fynd #1 re-bekräftat (ej blocker):** `logga-in__dark__1920` — bare `(auth)`-route-inputs har fortsatt near-osynlig `#1E293B`-kant (shadcn `--input`-bryggan, EJ `border-border-default`). Korrekt EJ migrerad i detta spår (utanför CTO-ramen). Bevisar migreringens scope-disciplin. Kvarstår som separat framtida spår per ursprungsdomslutets §4.

### Gate 2-verdikt

**GODKÄND.** Migrerings-setet (Deliverable 1 #1–13) är renderad-verifierat mot ADR 0047 på dark+light, 1280/1920/3440. WCAG 1.4.11-defekten löst med marginal; §3-flaggans rytm-oro avskriven (lugn/civic); noll light-regression; ingen dekorativ över-migrering; civic-utility-identitet (ADR 0016/0037) bevarad. Inga kvarstår-fynd. Spec-synk (contrast-table/tokens-full/DESIGN.md §4/ADR 0041-amendment) noterad som rapporterad klar — verifieras av docs-keeper, ej design-reviewer. Spåret är mergeklart ur design/a11y-perspektiv.
