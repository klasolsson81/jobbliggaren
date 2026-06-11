# Design-review: Fas E2e — Rensa-röda-textlänkar + sorterings-labels

**Status:** ✓ Approved
**Granskat:** 2026-06-11
**Branch:** `feat/sok-paritet-fe-rensa-sortering-e2e` (1 commit, `ac3e108`)
**Auktoritet:** DESIGN.md, jobbpilot-design-tokens (contrast-table.md), jobbpilot-design-principles §statusfärger, jobbpilot-design-copy (microcopy-library), ADR 0067 Beslut 7 rad 109, ADR 0068
**FAS-DEFERRAL-MANIFEST:** respekterat — review är kod/diff (rendered-verifiering post-merge på Vercel per E2a/E2b-prejudikat); facet-counts (E2c), chip-komponist (E2d) och TD-108-borderlines är out-of-scope och har inte genererat fynd.

### Bedömning per granskningsfråga

**1a. Danger-röd för rensa-affordance — korrekt eller färgmissbruk?**
Korrekt. Tre oberoende stöd: (i) ADR 0067 rad 109 specar uttryckligen "**Rensa** som röd text-länk per sektion (ej knapp)" — Klas-låst riktning; (ii) principles-skillen definierar `danger` → "avslag/fel/**destruktivt**" — att nollställa användarens filterval är en destruktiv handling på interaktionsnivå; (iii) Platsbanken-paritets-baselinen använder samma mönster. Att länken alltid bär `text-decoration: underline` gör att affordansen inte vilar på färg ensam (WCAG 1.4.1). Semantiken är också rätt byggd: `<button type="button">` stylat som text-länk — "ej knapp" i ADR:n avser visuell behandling, inte element-semantik. En `<a>` hade varit fel (handling, inte navigation).

**1b. WCAG-kontrast 13px/600-text (small text, 4.5:1-golv):**

| Par | Ratio | WCAG |
|---|---|---|
| `#BE1B1B` på `surface` #FFFFFF (popover light) | ~6.2:1 | AA pass |
| `#BE1B1B` på `canvas` #F4F6FA (toolbar light) | ~5.7:1 | AA pass |
| `#FB8989` på `surface` #1B2B47 (popover dark) | ~6.1:1 | AA pass |
| `#FB8989` på `canvas` #0B1525 (toolbar dark) | ~7.9:1 | AA pass |

Båda teman validerade separat. Focus-ring täcks av globala `*:focus-visible` (2px `--jp-focus`) — inga `outline: none`-övertramp.

**2. "Rensa alla filter" i results-toolbaren:** Godkänd. Synlighet gated på `chips.length > 0` (test verifierar både närvaro och frånvaro). Placering sist i `.jp-filterchips`; transparent bakgrund + underline skiljer den från pill-chipsen. Copy konsistent med microcopy-bibliotekets "Rensa alla"-mönster. Att `q` bevaras är begripligt: knappen säger "filter", söktermen är inte ett chip och ägs synligt av hero-formuläret. Synlig text är accessible name — ingen aria-label behövs.

**3. Sort-labels:** Godkända. "Relevans / Datum (nyast) / Ansökningsdatum (sista ansökan)" matchar Klas-promptens ordalydelse verbatim. Borttagningen av "(CV-match)" är en **faktakorrigering** — gamla labeln lovade CV-matchning som inte finns (ts_rank-FTS per ADR 0062; CV-match är Fas 4+, ADR 0042 Beslut F förbjuder placeholder-löften). Civic-utility i kärnan: säg aldrig mer än systemet gör. Längd OK (native select `width: auto`, toolbar flex-wrap).

**4. Svenska/copy:** Inga utropstecken/emoji/placeholder, rak svenska. Pass.

### Blockers
Inga.

### Major
Inga.

### Minor (nice-to-fix, inte blocker)

1. **Kontrasttabellen saknar de nya dark-paren** (`.claude/skills/jobbpilot-design-tokens/references/contrast-table.md`) — `danger` på `surface`/`canvas` i dark (~6.1:1 / ~7.9:1) finns inte i tabellen. **OBS (huvud-CC):** design-skill-edit = spec-edit som kräver Klas `approve-spec-edit.sh` — kan inte tas autonomt i natt-körningen; lyft i morgonrapporten.
2. **Fokus-förlust efter "Rensa alla filter":** knappen unmountas med chips-containern → fokus till body. Mildras av `aria-live="polite"` på räknaren; identiskt med pre-existerande sista-chip-×-beteendet (ärvt, inte introducerat). Framtida polish: programmatisk fokus till räknaren/selecten. Inte AA-brott.
3. **Ingen hover-state på `.jp-clearlink`:** medvetet korrekt icke-val — mörkare hover-röd kräver distinkt `--jp-danger-700`-token (Klas-GO), underline + cursor bär affordansen.

### Bra gjort

- `<button>` med text-länk-styling — rätt semantik för handling
- Klassen omdöpt `.jp-popover__clear` → `.jp-clearlink` när scopet växte — noll död CSS (grep-verifierat), en kanonisk klass
- CSS-kommentaren dokumenterar ADR-källa + kontrast-rationale — granskningstrail i koden
- Faktakorrigeringen av Relevans-labeln med on-disk-verifierad ExpiresAtAsc-mappning dokumenterad i kodkommentar
- Tre nya tester inkl. negativ-test + q-bevarande — flödesinvarianten test-låst
- `Relevans`-disabled-gaten (ADR 0042 Beslut D) intakt genom label-bytet

### Sammanfattning

**0 blockers, 0 major, 3 minor** (varav nr 3 medvetet korrekt icke-val; nr 1 = spec-edit → Klas). Klas-låst riktning implementerad exakt, AA-kontrast verifierad i båda teman, copy-faktafel rättat. **Mergeklar.** Rendered-verifiering post-merge på Vercel per manifestet.
