---
session: F-Pre Punkt 6 — Brand-paket (compass-mark + favicon/apple/OG/twitter + manifest + BrandLogo)
datum: 2026-05-25
slug: fpre-punkt6-brand-paket
status: levererad
commits:
  - d4a6acb feat(web): F-Pre Punkt 6 — brand-paket (compass-mark + favicon/apple/OG/twitter + manifest + BrandLogo + 4 callsites)
tags:
  - v0.2.72-dev (på `d4a6acb`)
---

# F-Pre Punkt 6 — Brand-paket

## Sammanfattning

Ersätter `<span class="jp-brand__mark">J</span>`-monogrammet på 4 ställen
(app-shell, landing-topbar, guest-shell, site-header) med riktig brand-mark:
4-point compass-stjärna i navy + gul accent-dot. Komplett asset-paket via
Next.js 16 file-conventions (`app/icon.svg`, `app/apple-icon.tsx`,
`app/opengraph-image.tsx`, `app/twitter-image.tsx`, `app/manifest.ts`).
Asset-derivat renderas via `next/og` ImageResponse Route Handlers
(edge-runtime) — inga Flux-API-anrop i produktion.

**1 feature-commit** (`d4a6acb`, 25 filer, +2052/-30). Tag `v0.2.72-dev` pushad
→ deploy-dev-workflow triggad.

## Fas-flöde

| Fas | Antal | Counter | Klas-STOPP-utfall |
|-----|-------|---------|--------------------|
| Strategi (CTO + prompts + budget) | 0 | 0 | STOPP A GO: Variant A text-led, 10-bild Fas 1, H2 tagline |
| Fas 1 (text-led, ursprunglig) | 10 | 0 → 10 | STOPP B-feedback: "Inte J-monogram — myndighets-paritet kräver symbol-led" → pivot, budget 30→50 |
| Fas 1-rev1 (6 symbol-arketyper × 2) | 12 | 10 → 22 | STOPP B-rev1: Klas valde 2 favoriter (vortex + compass) för parallell förfining |
| Fas 2 (förfining: 6 vortex + 6 compass) | 12 | 22 → 34 | STOPP C: C4 (4-point compass + yellow center-dot) FINAL |
| Fas 3 (asset-renders) | 0 | 34 | **Skippad** — hand-skissad SVG + Route Handlers ersätter Flux-renders, sparar 10+ genereringar |
| **Total Flux-genereringar** | **34/50** | **34** | Buffer 16 oanvänd ($0.96 saved) |

## Klas-STOPP-kedja

- **STOPP A:** GO på CTO Beslut 1 Variant A (text-led) + 4 prompts + budget 30
- **STOPP A-rev (Klas-pivot):** budget 30→50, ny riktning symbol-led per
  Arbetsförmedlingen/Försäkringskassan/Skatteverket-referenser
- **STOPP B-rev1:** vortex + compass favoriter → båda till Fas 2-förfining
- **STOPP C:** C4 (4-point compass + yellow accent-dot) FINAL
- **STOPP D:** GO på 1 commit + acceptera B1/M1 som pre-existerande separat work + G navy theme_color
- **STOPP E:** GO tag-push v0.2.72-dev på d4a6acb

## CTO-domar denna session

### Ursprunglig dom (2026-05-24)

Sex beslut i `docs/reviews/2026-05-24-fpre-punkt6-cto.md`:

1. **Beslut 1** — Variant A (text-led) — **KLAS-OVERRIDE 2026-05-25** → Variant B
   (symbol-led) per civic-tjänst-paritet med svenska myndigheter
2. **Beslut 2** — Variant γ (monokrom currentColor) — reviderad till
   tvåfärgs-strategi (--jp-brand-primary + --jp-brand-accent) post-pivot
3. **Beslut 3** — Variant I (hand-skissad SVG) — orörd
4. **Beslut 4** — Ren RSC + inline SVG — orörd
5. **Beslut 5** — Budget 30/10-8-8-4 → reviderad till 50/12-12-10-6 efter pivot
6. **Beslut 6** — Flux 1.1 Pro Ultra ($0.06/bild, 4MP) — orörd

### M1-triage 2026-05-25

Code-reviewer Major M1 (DRY-brott på compass-koordinater 5 ställen) → CTO
beslut **Variant B (BrandMarkSvg-komponent SSOT)**. Edge-runtime-import av
RSC-komponent fungerar i Satori (next/og ImageResponse). icon.svg förblir
duplicerad med synk-marker (file-convention kräver fysisk fil).

Netto: 5 callsites → 1 komponent + 1 mirror.

## Reviews-utfall (4 rapporter)

| Reviewer | Fynd | Resolution |
|----------|------|------------|
| **senior-cto-advisor** | Ursprunglig dom + M1-triage append | Variant B SSOT |
| **code-reviewer** | 1 Major M1 (DRY) + 5 Minor | Alla in-block-fixade |
| **security-auditor** | 1 Major M1 (public/brand/raw 40MB → prod-bundle) + 4 Minor | Mekanisk flytt till `tmp/brand-iterations/raw/` (gitignored). OUT_DIR i scripts uppdaterad. |
| **design-reviewer** | 1 Blocker B1 + 2 Major (M1, M2) + 3 Minor + 1 F4+ | **M2 + m2 + m3 in-scope-fixade. B1 + M1 PRE-EXISTERANDE** (F-Pre 5 / Steg 5 svans 2) — out-of-scope per FAS-DEFERRAL-MANIFEST, lyfts som separat work-item nästa session |

## In-block-fixar levererade

### CTO M1 (Variant B SSOT)

- Skapad: `src/components/brand/brand-mark-svg.tsx` (props för fill-strategi)
- Skapad: `src/components/brand/brand-mark-svg.test.tsx` (+7 tester)
- Refactor: `brand-logo.tsx`, `apple-icon.tsx`, `opengraph-image.tsx`,
  `twitter-image.tsx` — alla importerar BrandMarkSvg
- `app/icon.svg` — synk-marker XML-kommentar tillagd

### Code-review

- **m1:** `aria-hidden="true"` → `aria-hidden={true}` (boolean, React 19 idiom)
- **m2:** `xmlns` behållen i icon.svg + Route Handlers (satori-säkert)
- **m6:** kommentar i brand-logo.tsx förtydligad
- m3/m4/m5 skippade (motiverat acceptabelt)

### Security-audit

- **M1 BLOCKER fixed:** 26 PNG-filer flyttade `web/jobbpilot-web/public/brand/raw/*`
  → `tmp/brand-iterations/raw/*`. `tmp/` tillagt i `.gitignore`.
  `scripts/generate-brand-assets.mjs` OUT_DIR uppdaterad till tmp-path.

### Design-review

- **M2 fix:** `[data-theme="dark"] .jp-land-top .jp-brand { color: #133F73; }`
  låser brand-mark till light-värdet (~10:1) inom scoped-vit-topbar (eliminerar
  3.2:1 WCAG-borderline från default token-skift `#4F8AD0`)
- **m2 fix:** `.jp-brand:focus-visible` outline 2px navy-700 (WCAG 2.4.7-paritet
  med `.jp-btn`)
- **m3 fix:** wordmark-span får `aria-hidden={true}` när variant=full
  (eliminerar dubbel-uppläsning "JobbPilot — startsida, link, JobbPilot")

### Klas-G fråga

- **manifest.ts `theme_color`:** satt till `#0A2647` navy per design-rec +
  Klas-STOPP D-bekräftelse (PWA splash matchar landing-hero + OG-image,
  brand-igenkänning > sektor-konvention)

## Pre-existerande issues (separat work nästa session)

### B1 — Landing dark sekundär-CTA "Anmäl till väntelista" osynlig

Vit-på-navy = osynlig knapptext. Token-styling i `LandingHeroSection`. Infördes
i F-Pre Punkt 5 (`f02524e`). Kräver dark-mode-token-pair för sekundär-CTA på
navy hero.

### M1 — Vit `.jp-land-top` på dark body skapar visuellt skarvgap

Scoped-override-strategi från F-Pre Steg 5 svans 2 (`6104b7d`). Avsiktlig men
ser ut som dark-mode-bug på inre marketing-sidor. Två alternativ:
- A: dark-aware SiteHeader (egen `.jp-site-header`-klass)
- B: behåll vit topbar + förstärkt border-separator

## Implementation-detaljer

- **BrandMarkSvg SSOT** (`src/components/brand/brand-mark-svg.tsx`):
  4 polygons + 1 circle, viewBox 0 0 100 100, props `width/height/primaryFill/accentFill/className/ariaLabel/ariaHidden`
- **BrandLogo wrapper** (`brand-logo.tsx`): RSC, variant=`full|mark` + `markSize`
  prop default 32
- **app/icon.svg**: file-convention mirror med synk-marker XML-kommentar
- **app/apple-icon.tsx**: 180×180 ImageResponse, navy bg + vit mark + yellow dot
  (iOS home screen)
- **app/opengraph-image.tsx**: 1200×630 ImageResponse, vit bg + navy mark +
  navy wordmark + tagline "Den svenska jobbansökningshanteraren"
- **app/twitter-image.tsx**: paritet med OG (summary_large_image)
- **app/manifest.ts**: Next.js MetadataRoute.Manifest, theme_color navy,
  background_color vit, icons {icon.svg + apple-icon}
- **app/layout.tsx metadata**: utökat med title.template ("%s | JobbPilot"),
  openGraph (siteName, locale: sv_SE), twitter (card: summary_large_image),
  metadataBase med env-fallback (`NEXT_PUBLIC_SITE_URL` ?? `dev.jobbpilot.se`),
  applicationName
- **globals.css**: `--jp-brand-accent: #FFCD00`, `.jp-brand color: navy-700`,
  `.jp-brand:focus-visible`, dark-mode brand-lock
- **4 Flux-scripts**: raw `fetch` mot Replicate REST API, ingen `replicate`-npm-dep,
  HARD_CAP 50, retry-on-429 backoff, 12s sleep mellan calls (rate-limit-tier
  6 req/min vid <$5 credit)
- **Default `app/favicon.ico`** behållen (memory `feedback_dont_delete_auto_files`)

## Tester

- vitest: 708 → **715 PASS** (+7 nya: BrandMarkSvg render/fill/viewBox/dimensions/props/center-geometri)
- `pnpm build` PASS — alla nya file-convention-routes registrerade:
  - `/icon.svg` (static)
  - `/apple-icon` (dynamic edge)
  - `/opengraph-image` (dynamic edge)
  - `/twitter-image` (dynamic edge)
  - `/manifest.webmanifest` (static)
- Inga BE-ändringar, inga migrations, inga nya npm-dependencies

## TDs lyfta

**Inga.** Alla fynd pressade mot §9.6 (annan fas / saknad dep) och fixade in-block.
B1 + M1 (design-reviewer pre-existerande) är inte TDs utan separata work-items
för nästa session (de tillhör annan F-Pre Punkt scope).

## Replicate-batch-cost

| Fas | Genereringar | Kostnad |
|-----|--------------|---------|
| Fas 1 (sunk text-led) | 10 | $0.60 |
| Fas 1-rev1 (symbol-led pivot) | 12 | $0.72 |
| Fas 2 (förfining) | 12 | $0.72 |
| **Total använt** | **34** | **$2.04** |
| Buffer kvar | 16 | $0.96 (oanvänd) |
| **Max cap** | **50** | **$3.00** |

Iteration-PNGs sparade i `tmp/brand-iterations/raw/` (gitignored) för
historisk referens. Detaljerad log i `docs/reviews/2026-05-25-fpre-punkt6-flux-batch.md`.

## Disciplin

- CC gav inte egen rekommendation vid multi-approach-val (CTO decision-maker
  per §9.6) — 6 ursprungliga beslut + M1-triage
- Klas-override på CTO-Beslut 1 medvetet motiverad med 3 myndighets-referenser
- Explicit pathspec på `git add` (25 filer)
- `.claude/settings.json` aldrig committad
- Memory-användning: `feedback_dont_delete_auto_files` (default favicon.ico),
  `feedback_design_reviewer_deferral_manifest` (FAS-DEFERRAL-MANIFEST i
  design-reviewer prompt), `feedback_pathspec_commit_parallel_cc`,
  `feedback_subagent_hook_bypass_watch` (inga hook-bypasses),
  `feedback_klas_can_override_adr_verbatim_source` (Klas-override på CTO-Beslut 1)

## Pending Klas-operativt

1. **deploy-dev-workflow stable-verify** för `v0.2.72-dev` (queued vid tag-push)
2. **Post-deploy visual-verify** på dev.jobbpilot.se:
   - Favicon i browser-tab (modern browsers ska visa SVG-version)
   - OG-preview via Slack/LinkedIn-card-debugger
   - Apple-touch-icon vid "Add to home screen" på iOS
   - Brand-mark på alla 4 shells (landing, app-shell, guest, site-header) i
     light + dark
3. **PWA installation-test** (Android): theme_color navy splash
4. **B1 + M1 separat work-item** för nästa session (landing-CTA dark + topbar-shell-strategi)
5. **G-flip-möjlighet:** om navy PWA-splash inte önskas, 1-rads-edit i manifest.ts
   `theme_color: "#FFFFFF"`

## Nästa

Per Klas-direktiv: F-Pre Punkt 7 eller nästa identifierad scope. Kvarstående
kandidater:
- B1 + M1 design-fix (landing dark CTA + topbar-shell-konsekvens)
- Pending Punkt 5b operativt (deploy-dev stable-verify v0.2.71-dev)
- `/jobb` LIVE-vertikal för gäst (ADR 0005-amendment-väg)
- F4 AI-grind (ADR 0051)

Session avslutas. Inga TDs eskalerade, inga ADRs skapade.
