# Design-review: Fynd 2 FE — svenska namn-väljare (ADR 0043)

**Status:** APPROVED (modulo post-deploy visual-verify + Klas skärmbilds-approve)
**Granskat:** 2026-05-17
**Commit:** c79aace (lokalt, ej pushad)
**Auktoritet:** DESIGN.md + jobbpilot-design-principles (regel 1/3/5/6/7),
-copy, -a11y, -tokens
**Räkning:** 0 Blocker / 0 Major / 3 Minor

Filer granskade: region-picker.tsx, occupation-picker.tsx,
taxonomy-chip-list.tsx (NYA), job-ad-filters.tsx, jobb/page.tsx (ändrade),
lib/dto/taxonomy.ts, lib/api/taxonomy.ts.

---

## 1. Civic-utility (kärnan) — GODKÄND

concept-id-jargongen är HELT borta ur användarytan. Verifierat:

- `MVqp_eS8_kDZ`/`CifL_Rzy_Mku` förekommer bara i kod-kommentarer och
  test-fixturer — aldrig i renderad JSX.
- Den gamla hint-texten "JobTech-yrkeskod (concept-id), t.ex. MVqp_eS8_kDZ.
  Lägg till flera för OR-bevakning." är borttagen (job-ad-filters.tsx-diffen).
  "OR-bevakning" och "JobTech-yrkeskod" finns inte längre i någon renderad
  sträng.
- `font-mono` används inte i de nya komponenterna. Den gamla
  job-ad-multi-select.tsx hade `font-mono` på chippen — den komponenten är
  död kod (CC git-rm:ar i batchen, utanför scope per uppdrag).
- Chips visar `item.label` (namn) via TaxonomyChipList; redan-valda/sparade
  id reverse-lookas till namn server-side (page.tsx Promise.all →
  resolvedLabels). Stale snapshot faller civilt till "Okänd kod (<id>)" —
  saklig svensk text, ingen rå concept-id-exponering, ingen krasch.
- Regel 1 (papper ej glas): inga cards, inga shadows, ingen gradient. Chip =
  `bg-surface-secondary` + hairline-border, fieldset `border-0 p-0` (ren
  rubrik utan box-chrome). Korrekt.
- Regel 3 (inga fyllnadselement): enda ikonen är dismiss-X på chippen — den
  signalerar handling, inte dekoration. `aria-hidden` korrekt satt.
- Regel 5 (en accentfärg): ingen statusfärg missbrukad; neutral surface på
  chippar. Korrekt.
- Platsbanken-paritet: hierarkisk Yrkesområde→Yrke + Län-multi med rena
  fält speglar Platsbankens modell. Kärnsyftet med Fynd 2 uppnått.

Veto-ytan ren. Ingen AI-design-creep.

## 2. jobbpilot-design-copy — GODKÄND

- "du"-tilltal, gemen d, genomgående ("Du har valt…", "Ta bort ett för att
  lägga till fler.").
- Inga utrop, inga emoji, ingen peppning, ingen AI-klyscha.
- Etiketter rena och konkreta: "Län", "Yrkesområde", "Yrke".
- Civil degraderings-raden (job-ad-filters.tsx) följer empty-state-mönstret
  konstatering + konkret nästa steg: "Län- och yrkesval kunde inte laddas
  just nu. Du kan söka på sökord ändå och försöka igen om en stund." — saklig,
  ej skyllande, ger handling. Korrekt per -copy §1/§3.
- Cap-hint är konkret och kvantifierad: "Du har valt 10 län (max). Ta bort
  ett för att lägga till fler." Bra civic-utility-ton.
- "Rena fält"-regeln (Platsbanken, Klas 2026-05-17): inga exempel-placeholders
  på inputs. Native `<select>` unselected-display ("Välj län", "Välj
  yrkesområde", "Välj yrke", "Välj yrkesområde först") är funktionell
  unselected-state, inte exempeltext — faller under det dokumenterade
  select-undantaget i -components. Korrekt hanterat och kommenterat i koden.

## 3. a11y (VETO-yta) — GODKÄND

- RegionPicker: `<label htmlFor>` ↔ `<select id>` via useId; hint via
  `aria-describedby`. Korrekt per -a11y §5.
- OccupationPicker: `<fieldset>` + `<legend>` ger hierarki-gruppen ett
  tillgängligt namn utan att kollidera med input-label-queryn (legend ≠
  label) — väl resonerat i koden. Bägge inre `<select>` har egna
  htmlFor/id-par; yrkes-select kopplad till hint via aria-describedby.
- Progressiv disclosure inom väljaren tillgänglig: yrkes-select `disabled`
  tills yrkesområde valt, och option-texten byter till "Välj yrkesområde
  först" (status via TEXT, inte enbart disabled-state) — bra för skärmläsare.
- Native `<select>` valt i stället för custom combobox: robust tangentbord/
  skärmläsar-stöd utan ARIA-combobox-risk. Korrekt designval för
  civic-utility-målanvändaren.
- Dismiss-knapp: `aria-label={`Ta bort ${item.label}`}` (namn, ej id),
  X-ikon `aria-hidden`. Verifierat i test (getByRole button name "Ta bort
  Stockholms län").
- Chip-lista är `<ul>`/`<li>` med `aria-label` ("Valda län"/"Valda yrken") —
  semantiskt korrekt.
- Hit-area: dismiss-knapp `p-2` (≥32px in-app) + `max-md:p-2.5` (bump till
  ≥44px på touch ≤768px). Korrekt per -a11y §9. `<select>` `h-11` (44px) —
  över input-golvet.
- Fokusring: `<select>` använder `focus:outline-2 outline-offset-2
  outline-ring` — identiskt med befintlig Sortering-select i samma fil
  (konsekvent). Native select visar ring vid all fokus oavsett
  focus/focus-visible, så acceptabelt. Chip-knapp använder korrekt
  `focus-visible:`. Inget `outline: none` utan ersättning.
- Status-rad vid träd-fail har `role="status"` (artig live-region för
  degradering) — korrekt, ej assertive för rutin.

Ingen WCAG-blocker. Tangentbord/skärmläsar-flödet hållbart i kod.
Slutverifiering av renderad UI sker post-deploy via visual-verify (granskas
av mig, godkänns av Klas) — kod-ytan är ren.

## 4. Tokens — GODKÄND

- Endast design-tokens: `text-label`, `text-body`, `text-body-sm`,
  `text-text-primary`, `text-text-secondary`, `border-border-default`,
  `bg-surface-primary`, `bg-surface-secondary`, `outline-ring`. Alla
  resolveras i globals.css `@theme inline` (verifierat).
- Inga hårdkodade hex, inga Tailwind-defaults (slate-/gray-/zinc-).
- Radius: `rounded-md` (4px) + `rounded-sm` (2px) — ≤6px. OK.
- Ingen gradient, ingen glow, ingen shadow, ingen glasmorfism.
- Inga nya tokens introducerade (ingen Klas-approval krävs).
- `text-body`=16px på select uppfyller brödtextgolvet för §1.1-
  målanvändaren (ADR 0038). `text-body-sm`=14px på hint/secondary är
  a11y-golvet — OK för hjälptext.

## 5. FE-flagga (security-auditor) — VERIFIERAD GODKÄND

`dangerouslySetInnerHTML` förekommer noll gånger i taxonomy-chip-list.tsx,
region-picker.tsx, occupation-picker.tsx (grep verifierad). Reverse-lookup-
label renderas uteslutande som JSX children: `{item.label}` i chippen,
`{r.label}`/`{o.label}`/`{f.label}` i `<option>`. Stale-id-fallback
("Okänd kod (<id>)") är en ren textsträng. DTO-kommentaren i taxonomy.ts
dokumenterar att conceptId medvetet inte pattern-valideras vid stale snapshot
eftersom strängen ändå renderas som ren text. XSS-ytan stängd.

## 6. Server/Client-gräns — GODKÄND

- lib/api/taxonomy.ts: `import "server-only"`, ApiResult<T>, Zod vid
  ACL-gränsen. Ingen useEffect-fetch (CLAUDE.md §4.3/§5.2). Korrekt.
- jobb/page.tsx (Server Component): träd + reverse-lookup hämtas server-side,
  parallellt med listan via Promise.all (oberoende requests). Civil
  degradering: `taxonomyResult.kind === "ok" ? … : null` → väljarna får tomma
  listor + informativ rad; sök på sökord fungerar ändå. Korrekt resilient
  design.
- Pickers `"use client"` — endast interaktion på klienten. URL-driven
  server-state ägs av föräldern (JobAdFilters). Gränsdragningen ren.

---

## Minor (nice-to-fix, ej blocker — CC kan fixa in-block eller låta stå)

1. **`<select>` fokus-pattern `focus:` vs `focus-visible:`**
   region-picker.tsx:97, occupation-picker.tsx:115/141
   `<select>` använder `focus:outline-*`. Detta är medvetet konsekvent med
   befintlig Sortering-select (job-ad-filters.tsx:183) och native select
   visar ring vid all fokus oavsett — alltså ingen faktisk a11y-defekt.
   Noteras endast för framtida konsekvens-städning om hela select-ytan
   migreras till `focus-visible:` samtidigt. Inte blockerande.

2. **Cap-hint döljer var capen träffades vid blandade listor**
   Län- och yrkes-capen är separata (MAX_CONCEPT_IDS per lista). Hint-texten
   "Du har valt 10 län (max)" är korrekt per lista, men en användare med 10
   län + 3 yrken ser cap-texten bara på läns-väljaren. Beteendet är korrekt;
   möjlig mikro-copy-förbättring vore framtida. Ej blocker, ingen ändring
   krävs nu.

3. **`available.length === 0` disablar select utan synlig förklaring**
   region-picker.tsx:94 — om alla län redan valda blir select disabled.
   Hint-texten täcker cap-fallet men inte "alla valda, ej cap"-fallet (ovanligt:
   färre län än MAX). Edge case, låg sannolikhet (~21 län > cap 10 normalt).
   Ingen åtgärd krävs.

## Bra gjort

- concept-id-eliminering ur UI:t komplett och test-bevisad
  (region-picker.test.tsx: queryByText concept-id .not.toBeInTheDocument()).
- fieldset/legend-resonemanget (legend ≠ label, undviker label-query-kollision)
  är genomtänkt a11y, inte cargo-cult.
- Native select-valet motiverat ur civic-utility-målanvändarperspektiv —
  rätt avvägning trust > trend.
- Civil degradering hela vägen (träd-fail → tom lista + saklig rad;
  stale id → namn-fallback) utan att blockera sök-ytan.
- Touch-bump (`max-md:p-2.5`) korrekt och explicit kommenterad mot a11y §9.
- Server/Client-gräns ren, server-only-guard på fetchern.
- Inga emoji, inga utrop, du-tilltal, rena fält genomgående.

## Sammanfattning

0 Blocker / 0 Major / 3 Minor. Ingen Minor kräver in-block-fix —
samtliga är konsekvens-noteringar/edge cases utan a11y- eller
civic-utility-defekt. Kärnsyftet med Fynd 2 (concept-id-jargong ut,
svenska hierarkiska namn-väljare in, Platsbanken-paritet) är uppnått ur
design-perspektiv.

**Mergeklar ur design-perspektiv**, modulo:
- post-deploy visual-verify-skärmbilder (granskas av mig)
- Klas skärmbilds-approve (per Batch 6-mönster)

Utanför denna granskning per uppdrag (ej flaggat som blocker mot denna diff):
job-ad-multi-select.tsx/.test.tsx död kod (CC git-rm i batchen);
saved-search-list.tsx criteriaSummary rå concept-id (ADR 0043 Beslut D
cap-fråga — Klas/CTO-triage, separat batch).
