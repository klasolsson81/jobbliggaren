# ADR 0041 — Dedikerad modal-border-token för WCAG 1.4.11 i dark mode (partiell komplettering av ADR 0037)

**Datum:** 2026-05-16
**Status:** Accepted 2026-05-16 (senior-cto-advisor-beslut 2026-05-16; nextjs-ui-engineer auktoritativ token-math 2026-05-16; Klas-GO på inriktning Alt 2 + tokenvärde + `globals.css`-diff 2026-05-16; live-verifierad design-reviewer 0/0/0 mot deployad fix 2026-05-16; DESIGN.md-enradare applicerad efter Klas `approve-spec-edit.sh` 2026-05-16) — **amended 2026-05-18** (strukturell yt-chrome-kant `--jp-border-structural` inom ADR 0041:s egen deferrade "andra ytor"-verifieringspunkt; Amendment Accepted, IMPLEMENTATION PENDING; se Amendment-sektion).
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0037 (designsystem v2 — slate-skala + dark mode; detta **kompletterar**, supersederar ej), ADR 0038 (läsbarhetsgolv — samma princip: tillgänglighet är identitet, inte polish), ADR 0016 (civic design language), ADR 0039 (F2 SavedSearches — `DeleteSavedSearchDialog` ytade defekten), CLAUDE.md §8.6 (a11y i DoD), §9.2/§12/§13 (token-disciplin/amendment-process), DESIGN.md §4/§6

---

## Kontext

F2 Saved Searches levererade `DeleteSavedSearchDialog` (ADR 0039), som konsumerar den delade primitiven `web/jobbpilot-web/src/components/ui/dialog.tsx`. Auth-gated visuell verifiering mot live-deploy v0.2.7-dev (runbook `frontend-visual-verification.md`) flaggade dialogen som svag/osynlig i dark mode. design-reviewer kunde inte avgöra artefakt vs defekt från bilderna; nextjs-ui-engineer gjorde auktoritativ token-math mot `globals.css`.

**Verifierad rotorsak (dark mode):** `DialogContent` har klasserna `border border-border bg-background`. Token-kedjan:

- `bg-background` → `--background` → `--jp-surface-primary` dark = `#020617` (slate-950, canvas)
- `border-border` → `--border` → `--jp-border` dark = `#1E293B` (slate-800, dekorativ hårlinje)

Dialogytan (`#020617`) är **identisk** med canvas. `DialogOverlay` är `bg-black/50` → dimmad canvas ≈ `#01030C`. Ytseparation yta↔backdrop ≈ **1.0:1**. Enda avgränsande gränsen är `--jp-border` (`#1E293B`) vs backdrop ≈ **1.35:1**.

**WCAG 2.1 SC 1.4.11 (Non-text Contrast, Level AA)** kräver **≥3:1** för en UI-komponents visuella gräns mot omgivningen. En modal vars yta inte separerar från bakgrunden, vars enda kant är 1.35:1, är ett Level AA-fel. Projektets egen `contrast-table.md` (rad 71–72) dömer redan dark `--jp-border` ~1.6:1 (dekorativ, undantagen) och dark `--jp-border-strong` ~2.6:1 ("aldrig endast färg") — d.v.s. den **starkaste befintliga border-token är medvetet inte 3:1-bärande i dark**.

**Verifierad uttömmande:** ingen befintlig låst `--jp-*`-kombination når 3:1 mot dimmad slate-950-canvas (border 1.35:1; border-strong 1.76:1; surface-secondary-yta 1.18:1; surface-tertiary-yta 1.36:1). Shadow kan inte bära separation (DESIGN.md förbjuder djup-via-shadow; dark `--jp-shadow-md` ≈ osynlig mot near-black). Light mode är **ej** defekt — `bg-black/50` ger tydlig dimming i settlat läge; bildernas svaghet där var en 150ms-fade-capture-artefakt (capture-scriptet justerat med settle-wait).

Defekten är **cross-cutting**: `ui/dialog.tsx` är delad primitiv; alla dialoger ärver den. F2 var först att yta ett pre-existerande systemgap i ADR 0037:s dark-palett.

## Beslut

**Inför en dedikerad semantisk modal-border-token `--jp-border-modal` med ett dark-värde som når WCAG 1.4.11 ≥3:1, och låt `ui/dialog.tsx` konsumera den.** (senior-cto-advisor Alternativ 2 — minsta-ingrepp; Klas-GO på inriktning + värde 2026-05-16.)

1. **Token (semantiskt namn, ej värdenamn — Martin, Clean Code):** `--jp-border-modal`.
   - **Light:** `#E2E8F0` — *identiskt* med nuvarande `--jp-border` light. Light har ingen defekt; primitiven ska kunna referera en enda token utan light-regression.
   - **Dark:** `#64748B` (slate-500). ≈ **3.6:1** mot dimmad slate-950-canvas — marginal *över* golvet, inte på det (ADR 0038-principen: designa inte på golvet). **Inget nytt färgvärde uppfinns:** `#64748B` finns redan i dark-paletten som `--jp-info-500` (rad 128) — beslutet inför ett *semantiskt token*, inte en ny färg. Detta håller paletten låst och koherent (ADR 0037).
2. **Bridge:** `--color-border-modal: var(--jp-border-modal)` i `@theme inline` (speglar mönstret för `--color-border-strong` rad 184) → Tailwind genererar `border-border-modal`-utility.
3. **Konsumtion:** `DialogContent` i `ui/dialog.tsx` byter `border-border` → `border-border-modal`. Ingen ytfärg ändras, ingen overlay-mörkhet ändras, ingen annan komponent påverkas. Alla dialoger fixas av ett tokentillägg (DRY/SPOT — Martin 2017 kap. 13).
4. **DESIGN.md / `contrast-table.md`:** dokumentera den nya token + dess WCAG-roll (modal-/popover-gräns är strukturell, ej dekorativ → 3:1-bärande).

**Avvisade alternativ:**

- **Alt 1 (mörkare overlay + omdefinierad dialogyta `surface-tertiary`):** störst regressionsyta — varje dialogs inre kontrast (text/knappar/inputs mot ny `#1E293B`-yta) måste omräknas. Bryter minsta-ingrepp/OCP (Martin 2017 kap. 8) utan att lösa SC 1.4.11 bättre än Alt 2.
- **Alt 3 (brand-kantad modal `--jp-focus`/brand-blå ≈7:1):** löser kontrasten men *ändrar designspråket* och dilutar brand-semantiken (ADR 0037: brand-blå bär interaktion/selektion, inte struktur-chrome; Evans 2003 — en symbol, en betydelse). Ett designspråksbeslut hör inte hemma i en a11y-Blocker-fix.
- **TD-deferral:** avvisat. §9.6: defekten är i nuvarande-fas-kod (primitiven används av F2 nu), ingen saknad dependency, ingen annan fas → default = fixa in-block före fas-stängning. Att TD:a en Level AA-Blocker i en delad primitiv är precis det dumpningsbeteende §9.6/`feedback_td_lifting_discipline` förbjuder. Cross-cutting natur **eskalerar** (Klas-GO för token), den **deferrar inte**.

## Konsekvenser

**Positiva:**
- WCAG 2.1 SC 1.4.11 uppfyllt för alla dialoger i dark mode (delad primitiv, en token).
- Paletten förblir låst och civic (slate, ingen gradient/glow/brand-dilution) — `#64748B` är ett redan sanktionerat dark-värde.
- Minsta möjliga regressionsyta: en token + en utility-klass-byte; inga ytfärger eller overlay-värden rörs.
- Etablerar mönstret "strukturell gräns ≠ dekorativ hårlinje" explicit i token-systemet (kompletterar ADR 0037:s dark-palett).

**Negativa + mitigering:**
- Ny token ökar token-ytan. *Mitigering:* semantiskt namngiven, dokumenterad i DESIGN.md/contrast-table, single-purpose (modal/popover-gräns). Inte en värde-token utan en roll-token.
- Light-värdet duplicerar `--jp-border` light. *Mitigering:* medvetet — gör primitiven token-referentiellt enhetlig utan light-regression; om light-policyn någon gång ändras isoleras det till denna token.
- Endast `Dialog` migreras nu; andra overlay-ytor (Popover/Tooltip om de finns/tillkommer) kan ha samma gap. *Mitigering:* noteras som framtida verifieringspunkt — token finns redo att återanvändas; ej spekulativ migrering nu (YAGNI).

## Implementationsstatus

Accepted 2026-05-16, **fullt levererat och live-verifierat**. Applicerat: `globals.css` (`--jp-border-modal` light `#E2E8F0` / dark `#64748B` + `--color-border-modal`-bridge), `ui/dialog.tsx` (`border-border`→`border-border-modal`), `tokens-full.md` + `contrast-table.md` + DESIGN.md §Färg-enradare (efter Klas `approve-spec-edit.sh`). Deployad via `git push origin main` (Vercel auto-deploy, `64a6bf8`) + backend-tag `v0.2.8-dev`. Live-verifierad: serverad CSS innehåller `--jp-border-modal:#64748b` + `border-border-modal`; design-reviewer re-review mot live-screenshots (`20260516-1424`) = 0 Blockers/0 Major/0 Minor, Blocker RESOLVED, noll regression. Klas slutgodkände bilderna 2026-05-16.

---

## Amendment 2026-05-18 — strukturell yt-chrome-kant (`--jp-border-structural`)

> Originalbeslutet ovan (`--jp-border-modal`, levererat 2026-05-16) **lämnas intakt** som historisk trail. Detta amendment-block kompletterar ADR 0041 inom dess **egen utpekade framtida verifieringspunkt** (se Konsekvenser → "andra overlay-/yt-ytor … noteras som framtida verifieringspunkt"). Det är **inte** en ny ADR och **inte** en supersession — det generaliserar ADR 0041:s redan beslutade mönster ("strukturell gräns ≠ dekorativ hårlinje") från `Dialog` till strukturell yt-chrome (kort/sektion/panel/sidebar). (senior-cto-advisor `a6d33dbbd3beb25d0`; design-reviewer `a730cb207f8249886`, `docs/reviews/2026-05-18-dark-border-structural-contrast-design.md`.)

### Kontext (amendment)

Klas live-verifiering under FAS 3 ytade exakt den deferrade verifieringspunkten ovan. Samma rotorsak som modal-defekten, men på strukturell yt-chrome utanför `Dialog`: kort-, sektions-, panel- och sidebar-kanter mot canvas/kort där kanten är den **enda** avgränsande boundaryn mäter ≈ **1.2–1.4:1** i dark mode (`--jp-border` dark `#1E293B` slate-800 mot slate-950-canvas/närliggande yta). WCAG 2.1 SC 1.4.11 kräver ≥3:1 för en UI-komponents visuella gräns där gränsen är den enda separationen. ADR 0041:s ursprungliga scope löste detta **endast** för `Dialog` via `--jp-border-modal`; den explicita deferralen ("andra overlay-/yt-ytor … framtida verifieringspunkt; token finns redo att återanvändas; ej spekulativ migrering nu — YAGNI") är nu aktiverad av faktisk verifiering — inte spekulation.

### Beslut (amendment)

**senior-cto-advisor Approach B — inför en dedikerad semantisk roll-token `--jp-border-structural` för strukturell yt-chrome-gräns; behåll `--jp-border`/`-soft`/`-hairline` dark oförändrade som genuint dekorativa.**

1. **Token (roll-token, ej värde-token — SRP/OCP, Martin):** `--jp-border-structural`. En token = en change-reason. En strukturell yt-gräns och en dekorativ hårlinje är olika ansvar och får inte dela token.
   - **Dark:** `#64748B` (slate-500). ≈ **3.6:1** mot canvas — marginal över golvet, inte på det (ADR 0038: "designa inte på golvet"). **Inget nytt färgvärde:** `#64748B` är redan dark `--jp-info-500` och är *samma värde som `--jp-border-modal` dark* (ADR 0041 ursprungsbeslut) — palett-låst, koherent (ADR 0037).
   - **Light:** `#E2E8F0` — **behåll**. design-reviewer-dom (`a730cb207f8249886`): ingen light-defekt; konsekvent med ADR 0038 och ADR 0041:s light-precedent. `#CBD5E1` övervägdes och **avvisades** (ingen defekt att åtgärda, skulle vara design-på-golvet-regression i light).
2. **Bridge:** `--color-border-structural: var(--jp-border-structural)` i `@theme inline` (speglar `--color-border-modal`/`--color-border-strong`-mönstret) → Tailwind-utility `border-border-structural`.
3. **Migrerings-princip — strukturell vs dekorativ:**
   - **Migreras** (~13 logiska poster, design-reviewer-set): strukturell yt-chrome som avgränsar en hel yta mot canvas/intilliggande yta — `globals.css` `.jp-*` (card/listCard/sidebar/topbar/attention/filterBar/pipeline) + komponent-`.tsx` (kort-wrappers, status-edit-card, job-info-panel, saved-search-list).
   - **FÖRBLIR `--jp-border`** (genuint dekorativt): inre header-/footer-dividers, hairline-/strong-regler, list-rad-hairlinjes med egen bg-separation. Dessa har inte rollen "enda avgränsande boundary för en yta" och 3:1 är inte tillämpligt — att höja dem skulle lägga civic-tyngd överallt utan a11y-vinst.

### Avvisade alternativ (amendment)

- **Approach A (höj alla generella `--jp-border` dark till 3:1):** bryter SRP — tvingar dekorativa hårlinjer till strukturell vikt, lägger civic-tyngd överallt, maximerar visuell regressionsyta utan a11y-motiverat behov för de dekorativa fallen.
- **Approach C (ljusare yta så kort/canvas separerar utan kant):** ADR 0041 Alt 1-klass — störst regressionsyta (varje yt-interiörs kontrast måste omräknas), löser inte SC 1.4.11 bättre än Approach B.

### Konsekvenser (amendment)

**Positiva:**
- WCAG 2.1 SC 1.4.11 uppfyllt för strukturell yt-chrome (kort/sektion/panel/sidebar) i dark mode, inte bara `Dialog`.
- Palett förblir låst och civic — `#64748B` är ett redan sanktionerat dark-värde (= `--jp-info-500` = `--jp-border-modal` dark); inget nytt färgvärde uppfinns.
- SRP/OCP-koherent: token = en change-reason; dekorativ hårlinje ≠ strukturell gräns blir explicit i token-systemet, generaliserat från ADR 0041:s modal-mönster.

**Negativa + mitigering:**
- Ytterligare roll-token ökar token-ytan. *Mitigering:* semantiskt namngiven, single-purpose, dokumenteras i contrast-table/tokens-full/DESIGN.md/skill (Klas-spec-edit-gate, separat gate 4); roll-token, inte värde-token.
- ~13 logiska migrerings-touch-punkter i `globals.css` + komponent-`.tsx`. *Mitigering:* set fryst av design-reviewer (`a730cb207f8249886`); strikt strukturell-vs-dekorativ-kriterium hindrar scope creep; dekorativa ytor explicit oförändrade = noll civic-regression enligt design-reviewer.

### Implementationsstatus (amendment)

**Amendment Accepted 2026-05-18 — IMPLEMENTATION PENDING.** Beslutet (token, värden, bridge, migrerings-princip) är ratificerat (CTO `a6d33dbbd3beb25d0` + design-reviewer `a730cb207f8249886`). **Inget är levererat** — till skillnad från ADR 0041:s ursprungliga "fullt levererat och live-verifierat". Kvarstår, sekventiellt:

1. `globals.css`-token-amendment (`--jp-border-structural` light `#E2E8F0` / dark `#64748B` + `--color-border-structural`-bridge) — kräver Klas explicit `globals.css`-diff-GO (§9.2/§12/§13).
2. Spec-edits (`contrast-table.md` / `tokens-full.md` / DESIGN.md / `jobbpilot-design-tokens`-skill) — kräver Klas `approve-spec-edit.sh` (separat **gate 4**, ej rörd här).
3. ~13-posters migrering `border-border` → `border-border-structural` på strukturell yt-chrome (efter token finns).
4. Visual-verify + Klas dual-display live-verify (laptop + stationär) + `git push`.

Detta amendment-block (gate 3) ändrar **endast** `docs/decisions/0041-*.md` + `docs/decisions/README.md`. `globals.css`, DESIGN.md, `contrast-table.md`, `tokens-full.md` och skills är **orörda** och kvarstår Klas-gated.

### Referenser (amendment)

senior-cto-advisor `a6d33dbbd3beb25d0` (Approach B-beslut, avvisade A/C); design-reviewer `a730cb207f8249886` → `docs/reviews/2026-05-18-dark-border-structural-contrast-design.md` (light-värde-dom `#E2E8F0`, migrerings-set ~13 poster, strukturell-vs-dekorativ-avgränsning, noll civic-regression). WCAG 2.1 SC 1.4.11 Non-text Contrast (Level AA). ADR 0037 (palett-lås), ADR 0038 (designa inte på golvet), ADR 0041 ursprungsbeslut (modal-mönstret som generaliseras). CLAUDE.md §9.2/§9.6/§12/§13.

---

## Krav på Klas-GO

| Punkt | Kräver Klas-GO? |
|---|---|
| Inriktning Alt 2 + värde `#64748B` | ✅ Givet 2026-05-16 |
| ADR 0041 Proposed-utkast (denna fil) | Nej — CC direct per CTO/§9.6 |
| **`globals.css` token-amendment (`--jp-border-modal` + bridge)** | **JA — §9.2/§12/§13, explicit diff-GO innan applicering** |
| `ui/dialog.tsx` `border-border`→`border-border-modal` | Nej — kod, ej token (efter token finns) |
| DESIGN.md / `contrast-table.md`-uppdatering + ADR→Accepted | **JA — §13 (ingår i token-amendment-GO:t)** |

---

*Referenser: WCAG 2.1 SC 1.4.11 Non-text Contrast (Level AA); Robert C. Martin, Clean Architecture (2017) kap. 8 (OCP), kap. 13 (CCP/DRY/SPOT), Clean Code (intention-revealing names); Eric Evans, DDD (2003) — ubiquitous language; Nygard, Documenting Architecture Decisions (2011). ADR 0037, 0038, 0016, 0039. CLAUDE.md §8.6, §9.2, §9.6, §12, §13. `globals.css` rad 60–64/132–135/183–185; `src/components/ui/dialog.tsx` rad 49–73; `.claude/skills/jobbpilot-design-tokens/references/contrast-table.md` rad 71–72.*
