# Design-review: Fas E2b — Ort-pickern Län→Kommun-kaskad (branch `feat/sok-paritet-fe-kommun-kaskad-e2b`)

**Status:** ✓ Approved — inga Blockers, inga Major. Automerge OK.
**Granskat:** 2026-06-11
**Auktoritet:** DESIGN.md §3 (tokens), §6 (komponenter), §8 (copy), §9 (a11y); skills jobbpilot-design-a11y/copy/components/principles; ADR 0055, ADR 0067, ADR 0068
**Granskningsform:** kod/diff + struktur per FAS-DEFERRAL-MANIFEST punkt 1 (rendered-verifiering pending live-deploy, Klas granskar Vercel post-merge — E2a-prejudikatet). Manifest-punkterna 2–4 respekterade; inga fynd nedan rör dem.

---

### Adjudikering: "Hela länet" vs "Välj alla kommuner" (begärd dom)

**Dom: "Hela länet" GODKÄNNS.** Detta är den ärliga etiketten, och ärlighet i affordances är kärnan i civic-utility (GOV.UK-principen: en kontrolls label ska beskriva vad som faktiskt händer).

Motivering i tre led:

1. **Semantisk ärlighet.** Raden togglar ETT region-conceptId och materialiserar aldrig kommun-ids — kommun-raderna under förblir visuellt obockade när hela länet är valt (verifierat i `jobb-filter-popover.tsx`: `selectAllChecked = groupAxis ? activeGroupChecked : ...`). "Välj alla kommuner" skulle lova två saker som inte sker: att kommun-checkboxarna bockas i, och att 290 kommun-val skapas. Med den labeln hade obockade kommun-rader sett ut som en bugg. "Hela länet" beskriver exakt utfallet: en chip ("Stockholms län"), ett val.
2. **Konsekvens med Yrke-pickern utan falsk likhet.** Yrkes "Välj alla yrkesgrupper" materialiserar faktiskt item-ids — där är "Välj alla" sant. Att Ort-raden får en ANNAN label signalerar korrekt att semantiken är en annan. Samma label på två olika beteenden hade varit den verkliga inkonsekvensen.
3. **Platsbanken-mentala modellen.** I Platsbankens ort-filter är länet självt hel-läns-affordancen (länet väljs som enhet, kommunerna är förfining) — "Hela länet" speglar den modellen. **Reservation:** WebSearch är exkluderad ur design-reviewerns verktyg per agentdefinition (konsistens över trend), så Platsbankens exakta copy är inte live-verifierad 2026-06-11. Domen står på semantik-argumentet (led 1–2), som bär själv. Om Klas vill ha exakt Platsbanken-ordalydelse-paritet kan det live-verifieras post-merge utan att domen ändras — "Hela länet" är rätt oavsett vad Platsbanken råkar skriva.

Se Minor 1 nedan för en förbättring av samma label.

### Interaktions-semantik: kommun-klick ersätter helläns-val

**GODKÄNNS.** Per-län-normaliseringen (`lib/job-ads/ort-selection.ts`) är begriplig: synlig feedback i klick-ögonblicket (Norman) — "Hela länet"-raden släcks samtidigt, i samma kolumn; ingen tyst dataförlust (backend unionerar — normaliseringen är kosmetik, inte korrekthetsbärare, korrekt dokumenterat); riktningsasymmetrin är rätt (PÅ rensar länets kommun-val, AV tar bara bort sitt eget id; andra läns val rörs aldrig). Edge-fallen täckta i `ort-selection.test.ts`.

### Områdesgenomgång

- **Civic-utility:** Ren. Samma `.jp-popover`-primitiv som Yrke, noll ny CSS-yta, inga gradients/glow/radius-brott. Vänsterradens dot `var(--jp-leaf-600)` definierad i light + dark.
- **Tokens:** Inga hårdkodade färger; inline-styles är layout, inte färg — samma mönster som befintlig komponent.
- **A11y:** Mönstret intakt efter dual-axis-ändringen — dialog/listbox/option/checkbox-roller, `aria-checked` korrekt bunden till region-axeln för "Hela länet"-raden, Enter/Space/ESC + fokus-retur via `useDismissable`, key-remount för Ort (paritet med Yrke, ingen setState-i-effect), chips `aria-label="Ta bort filter X"`, `role="status" aria-live="polite"`, hasSel-dot inkluderar `groupSelectedSet`.
- **Svensk copy:** "Län kunde inte laddas just nu. Du kan söka på sökord ändå." / "Välj ett län till vänster." / "Hela länet" / "Kommuner". Inga utropstecken/emoji/placeholder (Klas hård regel — efterlevd). `emptyText`/`rightEmptyText` som props = copy-korrekthetsvinst.
- **Task-completion:** Stark — tre fällor täppta: hidden municipality-inputs i GET-formuläret, `buildPageHref` appendar municipality (F3-felklassen), `DeriveLabel` inkluderar kommun-labels. Rensa-scopen konsekventa (header = båda ort-axlarna; höger-kolumn = ett läns val).
- **Chips:** ordning region → kommun → yrkesgrupp samlar geografin; delad MapPin rätt (dimensionen Ort; läns-labels slutar på "län" → entydigt).

### Minor (nice-to-fix, blockar ej)

1. **"Hela länet" → "Hela {länsnamn}"** (`jobb-hero-filters.tsx`, `selectAllLabel`): "Hela Stockholms län" — konkretion + bättre SR-kontext (accessible name bär länsnamnet). Kräver per-grupp-härledbar `selectAllLabel` — liten API-ändring, lämplig för E2d-touchen.
2. **Dialogens accessible name "Län" ≠ triggerns "Ort"** — följer befintligt Yrke-mönster (konsekvent), men en separat `dialogLabel`-prop vore snäppet bättre SR-orientering. Med Minor 1.

### FYI (pre-existing, ej E2b)

- Vänsterradens har-val-dot är färg-enda-indikator (WCAG 1.4.1-borderline) — pre-existing från Yrke-pickern, supplementär; samkörs ev. med TD-108.
- Popovern icke-modal utan fokus-trap (medvetet per ADR 0055 Beslut 2).

### Bra gjort

- `groupAxis`-parameterisering i stället för mode-flagga — en primitiv, två beteenden, ärligt API; enkolumns-läget utgick med noll konsumenter
- `ort-selection.ts` exemplarisk: ren funktion, dokumenterad semantik, omfattande test
- Paginering + hero-sök + recent-searches bär alla municipality-axeln — ingen yta tappar filtret tyst
- `municipalities` REQUIRED i zod — kontraktsdrift maskeras inte
- Copy parametriserad i stället för hårdkodade Yrke-strängar i delad komponent

### Sammanfattning

**0 Blockers, 0 Major, 2 Minor, 2 FYI.** "Hela länet"-labeln godkänd med uttrycklig dom (ärlighets-argumentet bär; "Välj alla kommuner" hade varit en falsk affordance). Per-län-normaliseringen och chips-strukturen godkända. A11y-mönstret oskadat av dual-axis-ändringen. **Mergeklar** — Minor 1+2 tas i E2d-touchen tillsammans med chip-arbetet.
