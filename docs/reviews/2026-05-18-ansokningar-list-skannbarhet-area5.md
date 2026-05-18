# Design-reviewer — FAS-efter-3 /ansokningar list-skannbarhet Area 5 render-domslut

**Datum:** 2026-05-18
**Agent:** design-reviewer (bindande Area 5 render-VETO)
**Batch:** `/ansokningar` list-skannbarhet — statusöversikt-rad + minimera/maximera grupper
**Grind:** Area 5 render-VETO mot rendrad korpus (ADR 0047 flow-comprehension / ADR 0046)
**Mandat:** ADR 0047 (flow-comprehension / Area 5-roll), kontext ADR 0046
**Bindande ram:** CTO a8e269eb8dce9d541 (statusöversikt + opt-in disclosure + RSC/client-ö-arkitektur), CTO-superseder a316ed539d85b2f79 (slot-map-transport)
**Auktoritet:** jobbpilot-design-principles / -a11y, DESIGN.md §3/§4/§5/§6/§9, CLAUDE.md §1/§10
**Skärmbilder:** `C:/tmp/jobbpilot-visual/20260518-1145/` (äkta live-render)
**Källfiler granskade:** `src/app/(app)/ansokningar/page.tsx` (commit 40a413a), `src/components/applications/applications-pipeline.tsx`, `src/components/applications/application-row.tsx`, `src/lib/applications/status.ts`

## Domslut: GODKÄND — 0 Block / 0 Major / 2 Minor

Feature passerar Area 5-grinden. Statusöversikt-raden och opt-in-disclosure
är begripliga för en förstagångsanvändare utan gissning, CTO-ramen punkt 1–3
är efterlevd i renderad korpus, civic-utility-tonen hålls (ingen
accordion-/glow-/gradient-AI-kliché), a11y-strukturen i koden är korrekt, och
dark mode renderar listan utan fel (den tidigare RSC-serialiserings-incidenten
är bekräftat åtgärdad — listan renderar i dark). Inga blockerande fynd. Två
Minor är icke-blockerande och lämnas till nextjs-ui-engineer för bedömning.

**Korpus-täckningsbegränsning (ej fynd, dokumenterad):** fixturen har
`count > 0` för samtliga 10 statusar. Det inerta `count === 0`-spannet
(dämpad icke-fokuserbar `<span>`) exekveras därför aldrig visuellt i denna
korpus. Den inert-vs-länk-distinktionen — kärnan i ADR 0047-flödesfrågan för
tomma statusar — är verifierad i **kod** (separat `<span>` utan `href`,
`text-text-secondary` vs `<a>` `text-text-primary` + hover-underline), inte i
bild. Detta är inte ett VETO-fynd (implementationen är korrekt), men noteras
som rendervalideringslucka: nästa korpus bör inkludera en fixtur med ≥1 tom
status så det dämpade tillståndet kan render-granskas.

---

## Faktiskt inspekterade filer/viewports/teman

| Yta | light | dark | 1280 | 1920 | 3440 |
|---|---|---|---|---|---|
| `ansokningar-lista` (primär) | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ansokningar-detalj-jobad-kopplad` | ✓ (1280, 1920) | ✓ (1280, 1920)* | ✓ | ✓ | — |
| `ansokningar-detalj-manuell` | — | ✓ (1920) | — | ✓ | — |
| `ansokningar-detalj-submitted-radiogrupp` | ✓ (1280) | — | ✓ | — | — |

\* `jobad-kopplad__dark__*` renderades i **light mode** (vit canvas, blå
knapp) — känd CDP-instrumentartefakt (defer-not, Klas-acknowledged). `__light`-
varianten renderar korrekt som light; light-vägen är frisk. Detalj-vyer ej
modifierade av denna batch (commit 40a413a rör endast `page.tsx` +
ny `applications-pipeline.tsx` + test + AGENTS.md) — kontrollerade som
regressionskydd, ingen regression.

Övriga sidor i katalogen (jobb-*, landing, logga-in, sokningar-*, vantelista,
registrera, ansokningar-ny, ansokningar-status-*) = ej denna batch, ej
granskade per uppdrag.

---

## Punkt 1 — Flödesbegriplighet (ADR 0047)

**Förstagångsanvändare förstår översiktsraden utan gissning:** Ja.
Översiktsraden renderar (verifierat 1920 light + dark, 3440 båda) som en
horisontell rad mellan två hairlines direkt under leden, med alla 10 statusar
i klartext-svensk etikett + mono-räknare: "Utkast 29 · Skickad 6 · Bekräftad 8
· Intervju bokad 8 · Pågående intervju 8 · Erbjudande 8 · Accepterad 8 · Nekad
8 · Återtagen 4 · Inget svar 4". Mönstret läses som en innehållsförteckning,
inte som dashboard-pills — ingen ifylld bg, ingen ram, ingen färgkodning.
Etikett+siffra-formen kommunicerar "X ansökningar i status Y" direkt. `<nav
aria-label="Statusöversikt">` ger skärmläsare rätt landmärkeskontext.

**Inert vs klickbar (kod-verifierad, ej i bild — se täckningslucka):**
`count === 0` ⇒ `<span>` utan `href`, ej i tab-ordning, `text-text-secondary`,
ingen hover-underline. `count > 0` ⇒ `<a href="#status-X">`,
`text-text-primary`, `hover:underline`. Distinktionen är korrekt
implementerad: tomma statusar är genuint inerta (ingen död affordans — ADR
0047-konformt), icke-tomma är ankarlänkar. I korpus är alla 10 klickbara
(ingen tom status i fixturen), vilket renderar konsekvent.

**Disclosure-affordans begriplig:** Ja. Varje grupprubrik ("Utkast",
"Skickad", …) har en `ChevronDown` (size-4, 150ms rotation till -90° vid
kollaps) följd av rubriktext, med mono-räknare till höger om en
`border-strong`-hairline. Chevron + rubrik är **en** `<button>` —
hela rubriken är klick-yta, vilket är god civic-disclosure (verbatim-mönster
från `job-ad-filters.tsx`). Default expanderat verifierat i alla 6
list-skärmbilder (alla synliga grupper visar sina rader). `sr-only`-text
(", minimera/expandera gruppen {label}") ger skärmläsare explicit handling
utöver `aria-expanded`.

**Verdikt punkt 1:** Inga fynd. Flödet är självförklarande för §1.1-
målanvändaren.

## Punkt 2 — CTO-ram-efterlevnad (punkt 1–3)

| Krav | Status | Evidens |
|---|---|---|
| Alla 10 PIPELINE_ORDER, fast ordning | ✓ | `PIPELINE_ORDER`-array, översiktsraden mappar samtliga; 1920/3440 visar 10 i ordning Draft→Ghosted |
| 0-count = dämpad inert `<span>` (ej `<a>`, ej fokuserbar) | ✓ (kod) | `applications-pipeline.tsx:144-158` — `<span>` utan href; ej i korpus (alla count>0) |
| icke-tom = `<a href="#status-X">` ankarnav | ✓ | `:160-172`; native fragment-scroll, `scroll-mt-6` på `<section>` |
| Format `{svensk label} {count}`, mono siffra | ✓ | `getStatusLabel` + `font-mono text-[13px]`; renderat korrekt 1920/3440 |
| Ren in-page-ankarnav (ej filter, ej expandera+scrolla) | ✓ | Inga filter-side-effekter i koden; `<a href="#…">` enbart |
| Alla grupper expanderade default | ✓ | `useState(true)`; alla list-skärmbilder visar expanderade grupper |
| Minimera opt-in per grupp, ingen persistens/auto-kollaps | ✓ | Lokalt `useState` per `PipelineSection`, ingen storage/threshold |
| RSC page.tsx + client-ö ApplicationsPipeline | ✓ | `page.tsx` är RSC (auth/error/total===0), `"use client"` endast i pipeline-ön |
| ApplicationRow server-renderad via serialiserbar slot | ✓ | `rowSlots: Record<status, ReactNode[]>` byggs i RSC, slås upp i ön — ingen render-prop-funktion |
| count>0-sektionsfiltrering behålls | ✓ | `sections = …filter(g => g.count > 0)`; korpus visar endast icke-tomma sektioner |
| total===0-tomtillstånd orört | ✓ | `page.tsx:82-88` oförändrad gren; ej trigg i korpus (29+ ansökningar) |

Slot-map-transporten (CTO a316ed539d85b2f79) är korrekt: `ApplicationRow`
server-renderas i `page.tsx` och passas som serialiserbar `ReactNode[]` keyad
på status — ingen funktion över RSC↔Client-gränsen. AGENTS.md-tillägget
(`pnpm build` som mandatory pre-push-gate för RSC/client-boundary-ändringar)
är en korrekt processhärdning mot återfall av eece124-incidenten.

**Verdikt punkt 2:** Full ram-efterlevnad. Inga fynd.

## Punkt 3 — Civic-utility (principles/-components)

GOV.UK/1177-lugn hålls. Översiktsraden är civic — flat rad mellan två
hairlines, fält i naturlig bredd, ingen chrome-box, inga "dashboard-pills"
(`jobbpilot-design-principles` regel 2 "Information är design" + bra/dåligt-
jämförelsen efterlevd: ingen inramad metric, ingen färgad chip). Disclosure
är ingen AI-accordion: ingen bg-fyllning, ingen skugga, ingen rundad panel —
bara chevron + hairline (`border-strong` på rubrik, `border-default` på
panel-topp). Inga gradienter, ingen glow, ingen glasmorfism någonstans i
list-renderingen. Radius ≤ token-skala (inga >6px-hörn synliga). Skuggor:
inga på rader/sektioner (papper-metaforen hålls). Mono används korrekt som
signal (räknare, datum i rader) — aldrig brödtext/rubrik. Ikon (ChevronDown,
Lucide stroke) är funktionell (disclosure-state), ej dekorativ — regel 3
efterlevd. Svensk copy: leden "Pipeline över alla ansökningar. Klicka på en
rad för detaljer." — saklig, du-implicit, ingen emoji, inget utropstecken,
ingen AI-fras. Tomtillstånds-copy ("Inga ansökningar" + "Skapa din första
ansökan för att komma igång.") ej trigg i korpus men kod-verifierad som
konkret nästa steg.

**Verdikt punkt 3:** Civic-utility-konformt. Inga fynd.

## Punkt 4 — a11y (jobbpilot-design-a11y) + svensk copy

| Kontroll | Status | Not |
|---|---|---|
| Disclosure `aria-expanded` | ✓ | `aria-expanded={open}` på rubrik-`<button>` |
| Disclosure `aria-controls` | ✓ | `aria-controls={panelId}` (`useId`), panel har matchande `id` |
| Native `<button type="button">` för disclosure | ✓ | Ingen `<div onClick>`; korrekt semantik |
| `<a href="#…">` för in-page-nav (ej button) | ✓ | Ankarnav via riktiga länkar — korrekt nav-semantik |
| `<nav aria-label="Statusöversikt">` landmärke | ✓ | Distinkt från global nav |
| `<section id aria-label>` per status | ✓ | `id="status-X"` ankar-target, `aria-label={label}` |
| Fokus-ordning = visuell ordning | ✓ | DOM-ordning = pipeline-ordning; ingen `tabIndex>0` |
| Skärmläsar-handlingstext | ✓ | `sr-only` minimera/expandera-text utöver `aria-expanded` |
| Dekorativ ikon dold | ✓ | `ChevronDown aria-hidden="true"` |
| Fokusring (ärvd global `:focus-visible`) | ✓ | Ingen `outline:none`-override i komponenten |
| Kontrast light — översiktslänk text-primary/white | ✓ | ~17.9:1 (AAA) |
| Kontrast light — inert text-secondary/white | ✓ | ~7.4:1 (AA); dämpad men ej under-tröskel |
| Kontrast dark — text-primary/secondary på #020617 | ✓ | ~18.1:1 / ~6.5:1 (AAA/AA); verifierat i dark-render |
| Hairline `border-strong` på info-bärande rubrikgräns | ✓ | Rubrik-divider = `border-border-strong` (≥3:1); panel-topp = `border-default` (dekorativ, exempt) |
| Status aldrig endast färg | ✓ | Översikt = label+siffra (ingen färg alls); rader = StatusDot prick+text |
| Svensk copy — ingen emoji/utropstecken | ✓ | Leden + rubriker + `sr-only` sakliga, du-ton |
| `prefers-reduced-motion` | ✓ | Chevron-rotation 150ms; global reduced-motion-regel i globals.css neutraliserar |

Ingen dark-strukturell-kant-regression: de nya hairline-gränserna
(`border-strong` rubrik, `border-default` panel) följer ADR 0041-amendment-
tokens och renderar med korrekt kontrast i dark (verifierat
`ansokningar-lista__dark__1920/3440`). Ingen kontrast- eller strukturell
regression mot föregående dark-border-spår.

**Verdikt punkt 4:** a11y-strukturen är korrekt och WCAG 2.1 AA-konform i
både light och dark. Inga blockerande a11y-fynd. (Re: lucka — inert-spannets
dämpade kontrast bör render-verifieras i nästa korpus med tom-status-fixtur.)

## Punkt 5 — Dark mode (RSC-incident-bekräftelse)

`ansokningar-lista__dark__{1280,1920,3440}` renderar **korrekt och utan fel**.
Listan errorade tidigare (RSC-serialiseringsfel, commit eece124, reverterad);
slot-map-fixen (40a413a) är bekräftat verksam: i dark renderar översiktsraden,
disclosure-rubriker, hairlines och rader fullständigt. Ingen serialiserings-
artefakt, ingen tom sida, ingen RSC-runtime-error i dark. Kontrast och
struktur konsekventa light↔dark (papper-metaforen: mörkare sunken-yta,
text-primary ~18:1). Ingen strukturell regression från föregående
dark-kant-spår.

**Verdikt punkt 5:** Dark-mode-incidenten åtgärdad och verifierad. Inga fynd.

---

## Minor (icke-blockerande — nextjs-ui-engineer bedömer)

### Minor 1 — Översiktsradens räknare saknar `tabular-nums`-paritet med grupprubrikens

**Fil:** `applications-pipeline.tsx:78` vs `:139`
**Observation:** Översiktsradens räknare har `tabular-nums`
(`font-mono text-[13px] font-medium tabular-nums`), men grupprubrikens
räknare (`PipelineSection`, rad 78) har `font-mono text-[13px] font-medium`
**utan** `tabular-nums`. Mono-siffror i JetBrains Mono är redan
mono-spaced, så visuell effekt är försumbar i korpus, men för
konsekvens (samma datatyp, samma kontext) bör båda räknarna ha identisk
klassuppsättning. Ej a11y-/civic-fynd — ren konsekvens-polish.
**Förslag:** lägg `tabular-nums` på grupprubrik-räknaren (rad 78) så de
två räknar-renderingarna är identiska, eller dokumentera medvetet avsteg.

### Minor 2 — Översiktsraden saknar visuell separation från grupprubrikens hairline-täthet på 1280

**Skärmbild:** `ansokningar-lista__{light,dark}__1280` (downscaled — bedömd
på 1920 där läsbart)
**Observation:** På 1920/3440 är översiktsradens `border-y` +
`gap-y-2`-radbrytning luftig och tydligt avgränsad. Den fasta `gap-x-5`
mellan items är generös och skannbar. På smala viewports (1280, ~3
items/rad-wrap) blir radbrytningen tät men fortfarande korrekt mellan
hairlines. Ej ett brott (mellanrummen följer 4px-skala via Tailwind-steg),
men på 1280 light är översiktsraden av en sekundär-grå tunn rad på vit
canvas som i den nedskalade full-page-thumbnailen ligger nära
upplösningsgränsen — detta är en **thumbnail-skalningsartefakt**, inte
saknad rendering (samma DOM, tydligt synlig 1920 light). Noteras enbart så
nästa korpus helst kompletteras med en beskuren/zoomad
överblicksrad-vy för 1280 light, eftersom full-page-thumbnailen där inte
tillåter pixel-nivå-granskning av den dämpade raden.
**Förslag:** ingen kodändring krävd; tooling-not till render-pipeline
(beskuren overview-crop för smala viewports) för framtida granskbarhet.

---

## Bra gjort

- Slot-map-transporten är arkitektoniskt ren: ApplicationRow förblir
  server-renderad, ingen funktion korsar RSC↔Client-gränsen, rad-utseendet
  ägs aldrig av klient-ön. Incident-roten (eece124) korrekt åtgärdad.
- Disclosure-mönstret återanvänder verbatim `job-ad-filters.tsx` —
  konsekvens över komponenter, ingen ny art uppfunnen.
- Inert-vs-länk-distinktionen är genuint semantisk (`<span>` vs `<a href>`,
  ej bara CSS-styling) — ingen död affordans, ADR 0047-konformt.
- Översiktsraden är äkta civic: label+siffra mellan hairlines, ingen
  dashboard-pill-fällan (regel 2 efterlevd exemplariskt).
- AGENTS.md `pnpm build`-gate-tillägget är rätt processhärdning — fångar
  RSC-serialiseringsfel som vitest/tsc/eslint strukturellt inte kan.
- Dark mode renderar listan fullständigt — den tidigare incidenten är
  bevisat borta i alla tre dark-viewports.
- Svensk copy genomgående saklig: ingen emoji, inget utropstecken, du-ton,
  konkret tomtillstånds-nästa-steg (kod-verifierad).

## Sammanfattning

**GODKÄND — 0 Block / 0 Major / 2 Minor.** Area 5-grinden passerad.
Statusöversikt + opt-in-disclosure är begripliga utan gissning, CTO-ramen
punkt 1–3 helt efterlevd i renderad korpus, civic-utility-tonen hålls,
a11y-strukturen WCAG 2.1 AA-konform light+dark, dark-mode-incidenten
bevisat åtgärdad. De två Minor är icke-blockerande konsekvens-/tooling-
noteringar. **Rendervalideringslucka (ej fynd):** korpusens fixtur har
`count > 0` för alla 10 statusar — det inerta tom-status-spannet är
kod-verifierat men ej bild-verifierat; nästa korpus bör inkludera en
tom-status-fixtur så det dämpade inerta tillståndet kan render-granskas.
Delegera Minor till nextjs-ui-engineer för bedömning; ingen re-render krävs
för stängning.
