# CTO-dom — F-Pre Punkt 6 (Brand-paket)

**Datum:** 2026-05-24
**HEAD:** `f3c6f13` (origin/main)
**Baseline-tag:** `v0.2.71-dev` → ny `v0.2.72-dev`
**Agent:** senior-cto-advisor (decision-maker per CLAUDE.md §9.6)
**Scope:** logo + favicon + apple-icon + PWA-icons + OG/twitter-image + manifest + BrandLogo-komponent + 3 callsite-refactors
**Budget:** 30 Flux-genereringar (Klas hård cap)

---

## Beslut 1 — Logo-koncept-arketyp: ~~Variant A~~ → **KLAS-OVERRIDE 2026-05-25: Variant B (Symbol-led)**

**ORIGINAL CTO-VAL:** Variant A (text-led wordmark + minimal geometric J mark).

**KLAS-OVERRIDE 2026-05-25** efter Fas 1-granskning av 10 PNG-renders:
> "Vi ska INTE ha ett J som logga. Ingen myndighet har det, t ex
> arbetsförmedling osv. Loggan i sig behöver inte vara blå enbart med
> vit bakgrund, då hade jag lika gärna kunnat ha kvar nuvarande J."

Klas levererade 3 referens-screenshots: Arbetsförmedlingen (lime-grön
öppen ring + navy wordmark), Försäkringskassan (vit stiliserad
geometrisk symbol i mörkgrön box + vit wordmark), Skatteverket
(blå+gul vortex/spiral + navy wordmark). Alla 3 är symbol-led med
abstrakt geometrisk mark + flerfärgad palett. CTO:s avvisning av
Variant B ("AI-cliché-risk") visade sig vara fel kalibrering mot
svensk myndighets-konvention.

**Per CLAUDE.md §9.6 sista paragraf:** "Klas har alltid sista ordet — CTO
motiverar tydligt så Klas-override är medveten." Override är medveten
(Klas läste CTO-domen + Fas 1-renders + levererade 3 referenser för
konkret riktning) och budgeten höjs samtidigt 30→50 bilder för att
tillåta pivot.

**NY RIKTNING (CTO-godkänd revidering):** abstrakt geometrisk symbol-mark
till vänster om wordmark "JobbPilot". Symbol har semantisk kärna (rutt,
navigation, möjlighet, sammankoppling), tvåfärgs palett (navy + svensk
leaf-grön eller navy + svensk blå-gul), kan ha färgad bakgrund (ej
endast vit). Wordmark sansserif, navy eller vit beroende på bakgrund.

**Konsekvenser för andra beslut:**

- **Beslut 2 (theming)** revideras: monokrom-`currentColor`-strategin
  räcker inte för tvåfärgs symbol. Ny strategi: två CSS-vars
  (`--jp-brand-primary` + `--jp-brand-accent`) på SVG-paths, dark-mode
  flippar accent via token-skift. Detaljer i Beslut 2-revidering nedan.
- **Beslut 3-6** står oförändrade (SVG-hand-skiss, RSC-komponent,
  budget-fördelning relativt 50-cap, Flux Pro Ultra).

**Motivering:**

- Civic-utility-identitet (CLAUDE.md §1): "1177 eller Digg, inte Linear eller
  Vercel". GOV.UK och 1177 är båda text-led — kronan/korset är sekundära.
- YAGNI (Hunt/Thomas 1999, kap. 7): vi bygger jobbansökningshanterare, inte
  brand-driven konsument-startup. Symbol-led identitet är *brand-engineering*
  utan validerat behov.
- AI-cliché-undvikande (jobbpilot-design-principles): Variant B:s
  "navigations-symbol" (kompass/pilspets) sitter mitt i AI/SaaS-cliché-zonen
  2024-2026. Varje karriär-AI-startup har en variant.
- Robusthet < 32px: geometriskt J i navy-800 på vit är extremt robust i
  16/32px (motsv. 1177:s kors-mark). Variant C (dubbelmetafor) kollapsar i
  16px till "bara ett bokstavsmonogram".

**Avvisat:** Variant B (symbol-led — civic-utility-brott + AI-cliché),
Variant C (hybrid dubbelmetafor — för högt creative-risk för 30-cap).

**Trade-off accepterad:** marken blir avsiktligt "tråkig" (geometrisk J).
Det är poängen — Linear/Vercel är spännande, vi är inte de.

---

## Beslut 2 — Light/dark-strategi: **Variant γ (monokrom + CSS-var via currentColor)**

SVG med `fill="currentColor"` på hela mark + wordmark. Färg-control via CSS
på `.jp-brand`. Ingen accent-färg i logon — medvetet.

**Motivering:**

- DRY (Hunt/Thomas 1999) + SoC (Dijkstra 1974): färg-knowledge tillhör
  design-tokens (`globals.css`), inte SVG-källfilen. `currentColor` är enda
  mekanismen som delegerar färg till befintligt token-system utan duplicering
  eller theme-runtime-läsning.
- Server-component-first (Next.js 16 / Microsoft Learn RSC best practices):
  Variant β kräver theme-prop → client-island. Variant γ funkar i ren RSC.
- Dark-mode-token-integritet (ADR 0052): navy-800 ej skiftad i dark
  (medvetet för primärknapp-kontrast). Logo ska därför skifta till
  `--jp-navy-700` (#4F8AD0 i dark per globals.css:166) automatiskt via
  `.jp-brand`-color-token.

**Avvisat:** Variant α (CSS `filter: invert()` — bryter anti-aliasing/HSL),
Variant β (två separata SVG — bryter DRY, drar mot client-island).

**Trade-off accepterad:** strikt monokrom — ingen "navy J + grön leaf-accent".
I linje med Beslut 1 (civic-trust över brand-excitement).

---

## Beslut 3 — SVG-pipeline: **Variant I (hand-skissad SVG från Flux-koncept)**

CC läser Klas-godkänd Flux-PNG, konstruerar SVG-paths manuellt utifrån
koncept-geometrin. Inga auto-trace-verktyg.

**Motivering:**

- Clean Code (Martin 2008, kap. 1): produktions-SVG ska vara läsbar
  (`<path d="...">` + kommentarer). Auto-trace ger 50-200 path-segment där
  hand-skiss har 5-15. Framtida 2px-justering på J:ets serif ska vara möjlig.
- Bytes-budget (Microsoft Learn — Web performance): hand-skissad J = 800-1500
  bytes; auto-trace = 8-25 KB. Mätbart i LCP vid 5+ rendering/sida.
- Normalisering > exakt reproduktion av AI-artefakter (sub-pixel-blur,
  dithering, inkonsekvent stroke).
- SVG-favicon i 16px renderas crisp med 8-path-SVG (grid-snapped), inte med
  200-path auto-trace.

**Avvisat:** Variant II (auto-trace — kvalitetsbrott), Variant III (PNG
överallt — förlorar SVG-favicon-precision i browser-tab).

**Trade-off accepterad:** CC-arbetsinsats ~30-60 min för manuell SVG.

---

## Beslut 4 — BrandLogo-arkitektur: **Ren RSC med inline SVG**

`<BrandLogo />` är server-component, returnerar inline
`<svg fill="currentColor">` + wordmark. Inget `"use client"`, inget
theme-context. CSS via `.jp-brand` styr färg.

**Motivering:**

- Next.js RSC-default (Microsoft Learn / Next.js 16 docs): server-component
  är default; client-island opt-in vid interaktivitet. Logo har ingen state.
- Inline > `next/image` för <2KB SVG: snabbare (ingen extra request), möjliggör
  `currentColor`. `next/image` med SVG kräver `dangerouslyAllowSVG` —
  säkerhets-yta vi inte vill öppna.
- RSC-kompatibel komponent fungerar i båda kontexter (RSC kan renderas inuti
  client-island; omvänt fungerar inte). `guest-shell.tsx` (client) kan
  rendera `<BrandLogo />` (RSC) utan problem.
- SRP (Martin 2017, kap. 7): brand-uttryck = en change-reason. Theme-flip är
  CSS-concern, inte komponent-concern.

**Komponent-placering:** `web/jobbpilot-web/src/components/brand/brand-logo.tsx`.
Ny mapp `components/brand/` för framtida brand-primitiver.

**Refactor-omfång:** ersätt 3 `<span jp-brand__mark>J</span><span
jp-brand__word>JobbPilot</span>`-block med `<BrandLogo />`. Behåll
`.jp-brand`-wrapper på `<Link>`. Ta bort `.jp-brand__mark` + `.jp-brand__word`
CSS efter callsite-refactor verifierad (REP, Martin 2017 kap. 13).

---

## Beslut 5 — Budget-fördelning över ~~30-cap~~ → **50-cap (revised 2026-05-25)**

**Original budget:** 30 bilder (Fas 1: 10 / Fas 2: 8 / Fas 3: 8 / buffer: 4).
**Klas-revidering 2026-05-25:** ökat till 50 efter pivot till symbol-led.
10 sunk-cost-PNGs från ursprunglig Fas 1 (text-led) räknas inte mot ny
fördelning utan står som historisk referens i `public/brand/raw/`.

**Reviderad fördelning (40 nya bilder):**

| Fas | Antal | Syfte | Klas-STOPP efter |
|-----|-------|-------|------------------|
| **Fas 1-rev1 — Symbol-arketyper** | 12 | 6 prompts × 2 variationer för 5-6 symbol-arketyper (ring/öppning, vortex/spiral, sköld, kompass, knapp/portal, väg/trappa). Bred utforskning per Klas-budget-uppdatering. | **Ja (STOPP B)** |
| **Fas 2 — Förfining** | 12 | Iterera valt koncept: proportioner, färgpalett, stroke, symbol-detalj. 4-6 prompts × 2-3 variationer. | **Ja (STOPP C)** |
| **Fas 3 — Asset-renders** | 10 | OG (1200×630), apple-icon (180×180), twitter-image, favicon-validering @ 16/32px, ev. dark-bg-variant. | **Ja (STOPP D — efter impl)** |
| **Buffer** | 6 | Reservera för pivot-omtag, alternativ OG-komposition, dark-mode-validering. | — |
| **Total nya** | **40** | | |
| Sunk cost (text-led, override:ad) | 10 | Inte använda — finns i `raw/` för historik | — |
| **Grand total** | **50** | | |

**Spill-regel:** om Fas 1 < 10 (Klas hittar koncept tidigt), rulla över till
Fas 2-buffer, inte Fas 3. Förfining är där 30-cap-investering ger högst
marginal-värde.

---

## Beslut 6 — Flux-modell: **Pro Ultra (4MP, $0.06)**

Flux 1.1 Pro Ultra genomgående för alla 30 genereringar. Total kostnad ≈ $1.80.

**Motivering:**

- Källa-kvalitet: 4MP ger bättre hand-skiss-grund (Beslut 3).
- OG-image kräver 1200×630: Pro Ultra stöder aspect-ratio direkt; Pro 1.1
  (1024×1024) måste upsamplas + croppas.
- Kostnad ej beslutspunkt: $1.80 vs $1.20 försumbart mot CC + Klas tid.

---

## In-block-fixar (CLAUDE.md §9.6, inga TDs)

Allt nedan hör till F-Pre Punkt 6:

1. Skapa `web/jobbpilot-web/src/components/brand/brand-logo.tsx` (RSC, inline
   SVG, `currentColor`, `size?: number` prop default 32)
2. Skapa `web/jobbpilot-web/src/app/icon.svg` (mark utan wordmark, för
   Next.js auto-favicon)
3. Skapa `web/jobbpilot-web/src/app/apple-icon.png` (180×180 från Flux Fas 3)
4. Skapa `web/jobbpilot-web/src/app/opengraph-image.png` (1200×630 från Fas 3)
5. Skapa `web/jobbpilot-web/src/app/twitter-image.png` (samma eller variant)
6. Skapa `web/jobbpilot-web/src/app/manifest.ts` (Next.js Manifest med
   name/short_name/icons-array PWA 192/512, theme_color `#0A2647`,
   background_color `#FFFFFF`)
7. Refactor 3 callsites — ersätt `<span jp-brand__mark>J</span><span
   jp-brand__word>JobbPilot</span>` med `<BrandLogo />` i
   [app-shell.tsx:361](web/jobbpilot-web/src/components/shell/app-shell.tsx#L361),
   [landing-topbar.tsx:27](web/jobbpilot-web/src/components/landing/landing-topbar.tsx#L27),
   [guest-shell.tsx:49](web/jobbpilot-web/src/components/guest/guest-shell.tsx#L49)
8. Ta bort dead CSS `.jp-brand__mark` (globals.css:537-549) + `.jp-brand__word`
   (globals.css:550-554) efter callsite-refactor verifierad
9. Justera `.jp-brand` color (globals.css:535) — från `var(--jp-ink-1)` till
   `var(--jp-navy-700)` för dark-mode-anpassning via befintlig token-skift
10. Snapshot-tests för BrandLogo (rendering, `size`-prop, a11y)
11. Visual-verify via dev-test-konto (memory `project_dev_test_account`) i
    light + dark över alla 3 shells

---

## Klas-STOPP-flaggor (utöver A-E)

- **F (Asset-folder-konvention):** Next.js 16 file-convention-magi
  (`app/icon.svg` etc.) — verifiera att `pnpm build` plockar upp dem efter
  Fas 3. Fallback: manuella `<link>`-tags i `app/layout.tsx`. Risk-låg.
- **G (Manifest theme_color = navy-800):** PWA-splash blir mörk navy med vit
  mark. Standard för civic-tjänster är ofta vit splash + färgad mark. Klas
  bekräftar inriktning innan manifest.ts skrivs.
- **H (Tagline i OG-image):** Beslut 5 Fas 3 — Klas bestämmer innan Fas 3 om
  OG ska innehålla copy ("Den svenska jobbansökningshanteraren" eller
  liknande). Copy = i18n-låsning till svenska (rimligt, jfr CLAUDE.md §10).

---

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP), kap. 13
  (REP/CCP/CRP)
- Robert C. Martin, *Clean Code* (2008) — kap. 1
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — kap. 7 (DRY, YAGNI)
- Dijkstra (1974) — *On the role of scientific thought* (SoC)
- Kent Beck, *Test-Driven Development* (2002) — iterativ-utveckling-andan
- Microsoft Learn — *Architect modern web applications with ASP.NET Core
  and Azure*
- Next.js 16 docs — file-conventions: icon, apple-icon, opengraph-image,
  twitter-image, manifest
- CLAUDE.md §1, §5.2, §9.6, §10
- ADR 0052 (brand-token-arkitektur)
- DESIGN-skills: jobbpilot-design-principles, jobbpilot-design-tokens
- Memory: `feedback_dont_delete_auto_files`, `project_dev_test_account`

---

## M1-triage 2026-05-25 — Compass-mark geometri-DRY

**Fynd-källa:** code-reviewer rapport `docs/reviews/2026-05-25-fpre-punkt6-code-review.md` Major M1.

### Beslut: Variant B (`BrandMarkSvg`-render-komponent SSOT)

Skapa `web/jobbpilot-web/src/components/brand/brand-mark-svg.tsx` som ren
server-komponent med props `width/height/primaryFill/accentFill/className/ariaLabel/ariaHidden`.
Konsumeras av `brand-logo.tsx`, `apple-icon.tsx`, `opengraph-image.tsx`,
`twitter-image.tsx`. `app/icon.svg` förblir duplicerad (Next.js file-convention
kräver fysisk `.svg`) med explicit synk-marker som pekar på `BrandMarkSvg`.

Netto: 5 callsites → 1 komponent (SSOT) + 1 mirror (icon.svg med marker).

### Motivering

- **DRY (Hunt/Thomas 1999 kap. 7):** geometri är *knowledge piece*, inte
  coincidental likhet — kollapsbar via komponent-import
- **SRP (Martin 2017 kap. 7):** "geometrisk definition" är en change-reason;
  "användning per kontext" är fyra andra reasons
- **REP/CCP (Martin 2017 kap. 13):** things that change together belong together
- **OCP (Martin 2017 kap. 8):** props-driven fill ger nya kontexter (dark-OG,
  monokrom-print) utan modifikation av geometri
- **Next.js RSC + edge-runtime safe:** Satori använder React utan React-DOM
  (JSX-tree-conversion). Importerade RSC-komponenter är inom edge-runtime-scope

### Avvisade

- **Variant A (konstant + helper):** sparar bara koordinat-strängar, inte
  SVG-struktur-koden. Bryter DRY på rendering-pattern-nivå (Fowler 2018
  *Refactoring* — Extract Function > Extract Variable för upprepad logik)
- **Variant C (kommentar-marker):** "Disciplin" ersätter inte strukturell
  SSOT (Martin 2008 *Clean Code* kap. 17). Memory
  `feedback_td_lifting_discipline` relevant
- **Variant D (skjut till TD):** bryter §9.6 — fyndet hör till nuvarande fas,
  ingen saknad dependency, "scope-disciplin" ej legitimt TD-skäl

### Trade-offs accepterade

1. `icon.svg` förblir duplicerad med synk-marker (1→1, inte 1→4 som idag).
   Build-step för auto-gen avvisad som over-engineering för 12-rads SVG
   (YAGNI).
2. Edge-runtime-import-koppling. Vid framtida Satori-restriktion: degradering
   till Variant A trivial (~15 min refactor). Risk låg.
3. +1 fil i `components/brand/`. File count är inte design-värde.

### Genuina TDs

Inga. M1 är in-block.
