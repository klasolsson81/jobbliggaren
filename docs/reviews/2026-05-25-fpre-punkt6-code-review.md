# Code-review — F-Pre Punkt 6 (Brand-paket)

**Status:** Changes requested (1 Major, 5 Minor — alla in-block-fix, inga blockers)
**Granskat:** 2026-05-25
**Reviewer:** code-reviewer
**Auktoritet:** CLAUDE.md §2.1, §4 (TypeScript/Next.js), §5.2 (frontend anti-patterns), §9.6 (in-block-fix)
**HEAD-baseline:** `f3c6f13` (origin/main) — inga commits ännu, ren working-tree-granskning
**Scope:** 7 nya filer + 6 ändrade filer (brand-pipeline pivot till symbol-led per CTO-dom 2026-05-24 + Klas-override 2026-05-25)

---

## Sammanfattning

Brand-paketet är **kvalitativt välbyggt** och följer både CTO-domen och CLAUDE.md.
Inga Clean-Arch-brott, inga sync-over-async, inga secrets, inga `any`, ingen
`useEffect`, ingen `console.log`, inga utropstecken/emoji i copy. RSC-boundary
korrekt — `BrandLogo` är ren RSC och kan importeras från client-island
(verifierad: `guest-shell.tsx` `"use client"` importerar `BrandLogo` utan
problem eftersom Server Components alltid får renderas inuti Client Components
när de inte själva har klient-direktiv).

Den enda **Major-frågan** är duplicering av SVG-markup (4 ställen: `icon.svg`,
`apple-icon.tsx`, `opengraph-image.tsx`, `twitter-image.tsx`, plus 5:e gång i
`brand-logo.tsx` — totalt 5 ställen). DRY-brott i form av magic-koordinater
`50,8 56,30 50,47 44,30` (etc.) duplicerade. Motiverad delvis av Next.js
Route Handler-konvention (varje endpoint är sin egen ImageResponse), men
koordinaterna kan extraheras till en delad konstant.

Resterande är Minor: små polish-fynd kring a11y, defaults i Route Handlers
och kommentarsklarhet.

---

## Blockers

Inga.

---

## Major (bör fixas in-block innan commit per §9.6)

### M1 — DRY-brott: SVG-koordinater duplicerade på 5 ställen

**Filer:**
- `src/app/icon.svg:7-11`
- `src/app/apple-icon.tsx:24-28`
- `src/app/opengraph-image.tsx:28-32`
- `src/app/twitter-image.tsx:29-33`
- `src/components/brand/brand-logo.tsx:36-40`

**Fynd:**

Compass-mark-geometrin (4 polygon-punkter + center-circle) återupprepas
verbatim i 5 filer:

```tsx
<polygon points="50,8 56,30 50,47 44,30" fill="..." />
<polygon points="92,50 70,56 53,50 70,44" fill="..." />
<polygon points="50,92 44,70 50,53 56,70" fill="..." />
<polygon points="8,50 30,44 47,50 30,56" fill="..." />
<circle cx="50" cy="50" r="5" fill="..." />
```

Risk: om compass-formen behöver justeras (t.ex. tjockare diamond-stroke per
Fas 3-finetune) måste 5 filer ändras synkront. Glömd uppdatering = OG och
favicon driftar isär från header-logon. Robert Martin (Clean Code, kap. 17
G5): "Duplication should be eliminated".

**Föreslagen åtgärd (in-block-fix):**

Extrahera `BRAND_MARK_PATHS` till `src/components/brand/brand-mark-paths.ts`:

```ts
export const BRAND_MARK_VIEWBOX = "0 0 100 100";
export const BRAND_MARK_DIAMONDS = [
  "50,8 56,30 50,47 44,30",
  "92,50 70,56 53,50 70,44",
  "50,92 44,70 50,53 56,70",
  "8,50 30,44 47,50 30,56",
] as const;
export const BRAND_MARK_DOT = { cx: 50, cy: 50, r: 5 } as const;
```

Konsumeras av:
- `brand-logo.tsx` (.map över diamonds, currentColor)
- `apple-icon.tsx` / `opengraph-image.tsx` / `twitter-image.tsx` (samma .map,
  hex-fallback per render-kontext)
- `icon.svg` förblir statisk SVG (Next.js file-convention — fil måste vara
  ren SVG, kan inte importera TS). Acceptabelt undantag — dokumentera i
  konstant-filen att `icon.svg` är källans referens-spegling.

**Motivering att lyfta som Major och inte Minor:** brand-konsistens över
4 user-facing endpoints är en synlig regression om de driftar isär.
Refactor är ~10 min och stabiliserar pipelinen för Fas 3-justeringar
(CTO Beslut 5).

**Alternativt (CTO-fråga):** invokera senior-cto-advisor för att avgöra
om Route Handler-konventionen motiverar duplicering (Variant A: extrahera
nu, Variant B: acceptera duplicering med inline-kommentar "se brand-logo.tsx
för kanon", Variant C: TD för Fas 3-städning efter renders frysta).

---

## Minor

### m1 — `aria-hidden` på `<svg>` borde vara boolean `true`, inte sträng `"true"`

**Fil:** `src/components/brand/brand-logo.tsx:33`

**Fynd:**

```tsx
aria-hidden={variant === "full" ? "true" : undefined}
```

React/JSX accepterar både boolean och sträng på `aria-hidden`, men React 19 +
TypeScript-typerna föredrar `aria-hidden={true}` (boolean) — strängvarianten
är legacy. Tester på rad 9 (`expect(svg!.getAttribute("aria-hidden")).toBe("true")`)
fungerar i båda fall eftersom DOM serialiserar boolean → `"true"`-string ändå.

**Föreslagen åtgärd:**

```tsx
aria-hidden={variant === "full" ? true : undefined}
```

Inga testjusteringar krävs.

### m2 — `xmlns="http://www.w3.org/2000/svg"` onödig på inline SVG i HTML5

**Fil:** `src/components/brand/brand-logo.tsx:31`

**Fynd:** Inline SVG i HTML5 (alla moderna browsers, Next.js 16 RSC) ärver
SVG-namespace automatiskt. `xmlns`-attributet behövs bara för standalone SVG-
filer (som `icon.svg` på disk — där är det korrekt och nödvändigt).

**Föreslagen åtgärd:** ta bort `xmlns` från `brand-logo.tsx:31` och de tre
ImageResponse-SVG:erna (apple-icon, opengraph-image, twitter-image). Behåll i
`icon.svg`.

Liten optimering (-30 bytes per render), främst stilrent.

### m3 — Dubbel-renderad title-block (apple-icon utan title men icon.svg har)

**Fil:** `src/app/icon.svg:2`

**Fynd:** `icon.svg` har `<title>JobbPilot</title>` (korrekt — favicon kräver
det för screen-readers). Men `apple-icon.tsx`, `opengraph-image.tsx`,
`twitter-image.tsx` har inget `<title>` i sina inline SVG.

För OG/Twitter är det OK (alt-text sätts via `export const alt`). För
apple-icon spelar det ingen roll (renderas som PNG via ImageResponse — SVG
inom är bara satori-input, inte slutligt format).

Acceptabelt — flagga endast som not-an-issue för completeness.

### m4 — `runtime = "edge"` i Route Handlers kräver verifiering mot deploy-target

**Filer:** `apple-icon.tsx:8`, `opengraph-image.tsx:11`, `twitter-image.tsx:9`

**Fynd:** Edge runtime är default-rekommendation i Next.js 16 docs för
`ImageResponse` (satori körs i edge-isolat). Men:

1. Vercel kör edge utan problem.
2. Om Klas planerar self-host via Node-server (ej Vercel edge) → kontrollera
   att satori har Node-runtime-stöd. Next.js 16 dokumentation noterar att
   `ImageResponse` *kan* köras i Node men edge är preferred.

`metadataBase` är korrekt satt i `layout.tsx:21-23` med fallback till
`https://dev.jobbpilot.se`.

**Föreslagen åtgärd:** ingen ändring nu — fungerar i Vercel. Verifiera vid
första produktions-deploy. Inte ett blockerande problem.

### m5 — Manifest `icons`-array saknar `purpose: "any maskable"` för Android-adaptiv-stöd

**Fil:** `src/app/manifest.ts:17-28`

**Fynd:** Android PWA-installation använder adaptive icons. Utan
`purpose: "any maskable"` på minst en icon-entry rendar Android med vit
padding runt logon, vilket kan se klippt ut.

**Föreslagen åtgärd:** lägg till `purpose` på SVG-iconen:

```ts
{
  src: "/icon.svg",
  type: "image/svg+xml",
  sizes: "any",
  purpose: "any maskable",
},
```

Liten polish. Endast relevant om PWA-install ska kunna ske på Android — för
F-Pre Punkt 6 MVP är inte PWA-install primärt scope (skickas till Fas 2 per
CTO-dom). Acceptabelt att skjuta upp.

### m6 — Inkonsistent referens till "currentColor-mönster" i brand-logo.tsx kommentar

**Fil:** `src/components/brand/brand-logo.tsx:5-7`

**Fynd:** Kommentaren säger "ärver färg från CSS-vars (.jp-brand color-token)
via currentColor-mönster på primary diamonds". Men de 4 diamonds använder
`fill="currentColor"` (rad 36-39) — så de ärver `.jp-brand { color }`-tokenen
direkt, inte CSS-vars. Center-doten (rad 40) använder explicit
`fill="var(--jp-brand-accent)"`. Beskrivningen är korrekt men formuleringen
"CSS-vars via currentColor" kan läsas felaktigt — `currentColor` är CSS-keyword,
inte en var.

**Föreslagen åtgärd:** justera kommentar:

```tsx
// Ren RSC + inline SVG: 4 compass-diamonds ärver färg via `currentColor` från
// förälderns `.jp-brand { color }`-token. Center-dot använder separat
// `--jp-brand-accent` CSS-var för att kunna avvika från ink-tokenen.
```

Lite-poleringsfynd.

---

## Granskning per scope-fråga från Klas

### 1. Clean Code (Martin 2008): läsbarhet, DRY, naming

- **Läsbarhet:** OK. `BrandLogoProps`-typer dokumenterade med JSDoc. Komponenten
  är 23 rader kropp — under SRP-trösken.
- **DRY:** M1 (SVG-koordinater × 5). Övrigt OK.
- **Naming:** `BrandLogo`, `BrandLogoVariant`, `markSize` — alla tydliga. `markSize`
  istället för bara `size` är medveten precision (mark-storlek, inte wordmark-storlek)
  och kommenterad — bra.

### 2. TypeScript-stränghet

- Inga `any` någonstans. ✓
- Inga implicita returns. ✓
- `BrandLogoProps` är explicit interface med JSDoc. ✓
- `manifest.ts` returnerar `MetadataRoute.Manifest` (typad). ✓
- ImageResponse Route Handlers returnerar implicit `Response` — Next.js-typad. ✓

### 3. RSC-boundary-säkerhet

- `BrandLogo` är ren RSC (ingen `"use client"`-direktiv, inga hooks). ✓
- `guest-shell.tsx` är `"use client"` och importerar `BrandLogo` — fungerar
  eftersom Server Component utan klient-bara API:er kan renderas inuti Client
  Component (RSC är då "client-safe sub-component"). ✓
- `landing-topbar.tsx`, `site-header.tsx`, `app-shell.tsx` — RSC eller mixed,
  importen funkar i båda kontexter. ✓

### 4. Anti-patterns (CLAUDE.md §5.2)

- Ingen `any`. ✓
- Ingen `useEffect` för data-fetch. ✓
- Ingen `console.log`. ✓
- Inga utropstecken / emoji i copy ("JobbPilot", "Den svenska
  jobbansökningshanteraren" — neutralt civic-utility). ✓
- Inga gradients, glow, glasmorfism. ✓
- Inga inline-styles utan motivation — `apple-icon.tsx`, `opengraph-image.tsx`,
  `twitter-image.tsx` använder inline `style={{}}` men det är **påtvingat** av
  satori (ImageResponse stöder inte className/CSS-import). Acceptabelt och
  korrekt mönster.
- Hårdkodade hex `#0A2647`, `#FFFFFF`, `#FFCD00`, `#133F73` i ImageResponse-filer
  och `icon.svg` — **medveten avvikelse** eftersom ImageResponse körs i
  edge-isolat utan CSS-tokens; SVG-favicon kan ha CSS-vars men behöver hex-
  fallback för standalone-rendering (browser-tab). Skulle kunna refaktoriseras
  till en `BRAND_COLORS`-konstant samtidigt som M1 (DRY-fix). Inte ett
  separat fynd.

### 5. A11y

- `<BrandLogo variant="full">`: SVG `aria-hidden="true"` + wordmark är synlig
  text → screen-reader läser "JobbPilot" från wordmark. ✓
- `<BrandLogo variant="mark">`: SVG `aria-label="JobbPilot"`, ingen wordmark →
  screen-reader läser "JobbPilot" från SVG-label. ✓
- `<Link aria-label="...">` på alla 4 callsites — ger en extra accessible name
  till länken (t.ex. "JobbPilot — till start"). Detta läses **istället för**
  BrandLogo:s innehåll (länkens aria-label vinner). ✓
- `icon.svg`: `role="img"` + `aria-label="JobbPilot"` + `<title>` redundans —
  acceptabel försvar-i-djup för olika user-agents.

A11y-strukturen är medveten och korrekt — `aria-hidden` på full-variant är
exakt rätt (annars skulle screen-reader läsa "JobbPilot" två gånger).

### 6. Test-täckning

- 5 testfall (full-variant, mark-variant, markSize=48, default=32, struktur). ✓
- Saknar test för `fill="currentColor"` på diamonds + accent på circle (struktur
  ja, attribut nej). Liten lucka — om någon byter färg-strategi går det att
  märka.
- Saknar test för att wordmark inte renderas vid variant=mark (täcks indirekt
  via `queryByText("JobbPilot")).toBeNull()`). ✓
- Ingen ImageResponse-test för apple-icon / OG / twitter — **acceptabelt**
  eftersom satori-render är out-of-process och svår att meningsfullt testa
  utan visual-regression-pipeline. Visual verify via Klas är rätt mekanism.

Test-täckning OK.

### 7. CSS-cleanup

Verifierat via diff (`git diff -- globals.css`):

- `.jp-brand__mark` har gått från text-J-style (`width:32px; height:32px;
  border-radius:8px; background:var(--jp-navy-800); color:#fff;
  display:flex; align-items:center; justify-content:center; font-weight:700;
  font-size:15px; letter-spacing:-0.02em;`) → SVG-stil (`flex-shrink:0;
  display:block;`). ✓ Korrekt ersatt.
- `.jp-brand__word` oförändrad — den används fortfarande av BrandLogo full-variant. ✓
- `.jp-brand` color ändrad från `var(--jp-ink-1)` → `var(--jp-navy-700)` med
  kommentar. ✓ Per CTO Beslut 2-rev — currentColor flippar via token-skift.
- `--jp-brand-accent: #FFCD00` tillagd på rad 120. ✓

Cleanup korrekt utförd.

### 8. Dead code (J-span-callsites)

Grep `>J<|className="jp-brand` bekräftar: **inga gamla `<span class="jp-brand__mark">J</span>`-callsites kvar**.
Endast 4 `<Link className="jp-brand">`-wrappers (app-shell, landing-topbar,
guest-shell, site-header) — alla med `<BrandLogo />` som child. ✓

### 9. Next.js 16 file-convention-korrekthet

- `app/icon.svg` — statisk SVG, auto-picked av Next.js 16 som favicon (16/32/SVG). ✓
- `app/apple-icon.tsx` — Route Handler med `export const size`, `contentType`,
  `runtime`. ✓ Per Next.js 16 docs.
- `app/opengraph-image.tsx` / `app/twitter-image.tsx` — samma mönster + `export const alt`. ✓
- `app/manifest.ts` — exporterar default function `manifest(): MetadataRoute.Manifest`. ✓
- `metadataBase` satt i layout.tsx med fallback `https://dev.jobbpilot.se`. ✓
- `runtime = "edge"` är OK för Vercel-deploy. Se m4.
- `default favicon.ico` behållen i `app/favicon.ico` (per memory
  `feedback_dont_delete_auto_files`). ✓

File-conventions korrekt implementerade.

### 10. Saknad funktionalitet enligt CTO-domens 11 in-block-fixar

| # | Fix | Status |
|---|-----|--------|
| 1 | BrandLogo-komponent | ✓ skapad |
| 2 | icon.svg | ✓ skapad |
| 3 | apple-icon | ✓ skapad (.tsx istället för .png — Route Handler är giltig variant per Next.js 16) |
| 4 | opengraph-image | ✓ skapad (.tsx) |
| 5 | twitter-image | ✓ skapad (.tsx) |
| 6 | manifest.ts | ✓ skapad — Klas-val G2 (vit splash) med flip-kommentar |
| 7 | Refactor 3 callsites | ✓ + bonus 4:e (site-header.tsx upptäckt av CC) |
| 8 | Ta bort dead `.jp-brand__mark` CSS (text-J-style) | ✓ ersatt med SVG-stil |
| 9 | Justera `.jp-brand` color → navy-700 | ✓ med kommentar |
| 10 | Snapshot-tests | ✓ 5 testfall för BrandLogo |
| 11 | Visual-verify via dev-test-konto | ⏳ pending — Klas-uppgift efter commit |

PWA-icons 192/512 och favicon.ico-ersättning skippade per Klas-direktiv —
**acceptabelt och dokumenterat** i scope-noten.

---

## Bra gjort

- **RSC-default + client-island-kompatibilitet** korrekt löst — BrandLogo
  funkar i båda kontexter utan boilerplate (per Beslut 4).
- **Token-strategi tvåfärgs** (CTO Beslut 2-rev) elegant löst — diamonds via
  `currentColor` (tema-aware), accent via separat `--jp-brand-accent` (medveten
  konstant gul). Dark-mode-flip "gratis" via befintlig navy-700-token-skift.
- **Kommentarer hänvisar till CTO-dom + Klas-val** (rad-nummer + datum) — det
  här är granskningsvänlig kod, lätt att försvara senare.
- **Hex-fallback i `icon.svg`** (`fill="var(..., #0A2647)"`) — korrekt
  defensive coding för standalone-rendering där CSS-vars inte resolveras.
- **`alt`-text på OG/Twitter** med tagline ("JobbPilot — Den svenska
  jobbansökningshanteraren") — civic-utility-ton, ingen marknadsföringsklang. ✓
- **`lang: "sv"` i manifest.ts** — locale-korrekt PWA. ✓
- **`title.template: "%s | JobbPilot"`** i layout.tsx — alla sidor får
  konsekvent title-suffix utan duplicering. ✓
- **`metadataBase` med env-var-fallback** — production/dev/preview-säker. ✓
- **Inga utropstecken, inga emoji, ingen AI-klang** — copy-disciplin följd. ✓
- **Acceptabel medveten duplicering av ImageResponse-koordinater** är
  åtminstone medvetet hanterad (inga försök att introducera satori-imports
  från shared module — vilket skulle bryta edge-runtime-kontraktet).

---

## Delegationer

- **M1 DRY-fix:** in-block-fix av CC, eller invokera `senior-cto-advisor`
  för Variant A/B/C-val om Klas vill ha CTO-dom på trade-offen
  (`Route Handler-isolering vs DRY`). Min rekommendation som code-reviewer:
  CTO-fråga eftersom det är en arkitektonisk principfråga (DRY vs file-
  convention-isolering) som påverkar Fas 3-finetune-pipelinen.
- **m1–m6:** fixa in-block av CC. Triviala.
- **Visual-verify (CTO in-block-fix #11):** Klas via dev-test-konto efter
  commit + Vercel-deploy.

---

## Sammanfattning för commit-beslut

Inga blockers, 1 Major (DRY), 5 Minor (polish). Per CLAUDE.md §9.6 default =
fixa in-block innan commit. Major M1 behöver CTO-dom eller direkt
CC-implementation; Minor m1, m2, m6 fixas i samma batch (5 min); m3, m4, m5
är acceptabla att skjuta (m4 produktions-verifiering, m5 PWA-install Fas 2).

Klar för commit efter M1 + m1/m2/m6 åtgärdade.
