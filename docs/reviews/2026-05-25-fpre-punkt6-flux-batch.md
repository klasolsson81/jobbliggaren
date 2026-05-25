# Flux-batch — F-Pre Punkt 6 (Brand-paket)

**Datum:** 2026-05-24 → 2026-05-25
**HEAD-baseline:** `f3c6f13`
**Modell:** `black-forest-labs/flux-1.1-pro-ultra` på Replicate
**Endpoint:** `POST https://api.replicate.com/v1/models/black-forest-labs/flux-1.1-pro-ultra/predictions`
**Pris:** $0.06/bild → totalt ≈ $2.04 (34 genereringar à $0.06)
**Hard cap:** 50 (höjt från 30 efter Klas-pivot 2026-05-25); 34 använda, 16 buffer kvar
**Output-format:** PNG 1:1 aspect-ratio
**Rate-limit-tier:** reduced (6 req/min, <$5 credit) — hanterat med 12s sleep mellan batches + retry-on-429 backoff

## Sammanfattning

Tre faser med Klas-STOPP mellan:

| Fas | Antal | Counter | Klas-STOPP-utfall |
|-----|-------|---------|--------------------|
| Fas 1 (text-led, ursprunglig CTO Beslut 1) | 10 | 0 → 10 | STOPP B-feedback: "Inte ett J — riktiga symboler enligt svenska myndigheter". CTO-Beslut 1 override:at → symbol-led. Budget höjd 30→50. 10 sunk cost. |
| Fas 1-rev1 (symbol-led pivot) | 12 | 10 → 22 | STOPP B-rev1: Klas valde två favoriter (B-vortex v2 + D-compass v2) för parallel Fas 2-förfining. |
| Fas 2 (förfining på båda linjerna) | 12 | 22 → 34 | STOPP C: Klas valde C4 (4-point compass + yellow accent-dot) som FINAL. |
| Fas 3 (asset-renders via Flux) | — | — | Skippad. Asset-derivat (apple-icon, OG, twitter) genereras via Next.js `next/og` ImageResponse Route Handlers från hand-skissad SVG-källa istället för Flux (sparar 10+ genereringar). 16 Flux-budget kvar oanvänd. |

## Fas 1 — Text-led (sunk cost)

CTO Beslut 1 (Variant A — text-led wordmark + minimal geometric J mark) gav 10
genereringar fördelade på 4 prompts (A/B/C/D). Resultat: 10 PNG-renders av J-monogram
+ wordmark "JobbPilot" på vit bakgrund. Klas-feedback 2026-05-25:

> Vi ska INTE ha ett J som logga. Ingen myndighet har det, t ex arbetsförmedling osv.
> Loggan i sig behöver inte vara blå enbart med vit bakgrund.

CTO-Beslut 1 override:at + 3 referens-screenshots (Arbetsförmedlingen, Försäkringskassan,
Skatteverket) — alla symbol-led med flerfärgad palett.

PNG-filer kvar i `web/jobbpilot-web/public/brand/raw/fas1-*.png` (historisk referens).

## Fas 1-rev1 — Symbol-led utforskning

6 arketyper × 2 variationer = 12 genereringar:

- **A — Öppen ring** (Arbetsförmedlingen-paritet, möjlighet/förbindelse)
- **B — Vortex/spiral** (Skatteverket-paritet, navy + warm yellow)
- **C — Sköld/badge** (Försäkringskassan-paritet, grön box + vit symbol)
- **D — Kompass-stjärna** (4-point navigation, navy + leaf-grön)
- **E — Karriär-trappa** (stigande blocks)
- **F — Portal/dörr** (öppning till möjlighet)

PNG-filer: `web/jobbpilot-web/public/brand/raw/fas1rev1-*.png`.

**Klas-val STOPP B-rev1:** B (vortex) + D (compass) → båda till förfining.

## Fas 2 — Förfining av två linjer

12 genereringar fördelade 6/6:

**Vortex-linjen (`web/jobbpilot-web/public/brand/raw/vortex/`):**
1. 4-blade sharp angular (navy + yellow alternating)
2. 3-blade open curved
3. 4-blade angular (navy + leaf-grön)
4. Strikt pinwheel (right-triangle blades)
5. Cirkulär medallion med vortex inuti
6. 2 interlocking blade-curl (yin-yang-style)

**Compass-linjen (`web/jobbpilot-web/public/brand/raw/compass/`):**
1. 4-point equal (alla navy, sharp diamonds)
2. 4-point two-tone (top/bottom navy + left/right leaf-grön)
3. 4-point thin elongated
4. **4-point med gul accent-dot i center** ← KLAS-VAL C4
5. 4-point svensk flag-palett (navy + yellow)
6. 4-point med inner ring runt center

**Klas-val STOPP C 2026-05-25:** `fas2-fas2-compass-4point-accent-v1-2026-05-24T23-20-56-222Z.png` →
4 deep navy diamond-points + small yellow center-dot (#FFCD00). Hand-skissad till SVG
via CTO Beslut 3 Variant I → `web/jobbpilot-web/src/app/icon.svg`.

## Cost-summary

| Fas | Genereringar | Kostnad |
|-----|--------------|---------|
| Fas 1 (sunk) | 10 | $0.60 |
| Fas 1-rev1 | 12 | $0.72 |
| Fas 2 | 12 | $0.72 |
| **Total använt** | **34** | **$2.04** |
| Buffer kvar | 16 | $0.96 (max-cost om buffer används) |
| **Max budget cap** | **50** | **$3.00** |

## Asset-derivat utan Flux

Per CTO Beslut 3 Variant I (hand-skiss > auto-trace > raw PNG) tas SVG-källan
hand-skissad från Klas-valda PNG. Asset-derivat (apple-icon, OG, twitter) renderas
via Next.js 16 file-convention Route Handlers (`next/og` ImageResponse + satori),
INTE via Flux. Sparar 10+ Flux-genereringar och ger pixel-perfekt deterministisk
rendering i target-sizes.

Asset-derivat:
- `app/icon.svg` — primary favicon (modern browsers)
- `app/apple-icon.tsx` — 180×180 iOS home screen (edge runtime)
- `app/opengraph-image.tsx` — 1200×630 social cards (edge runtime)
- `app/twitter-image.tsx` — 1200×630 Twitter cards (edge runtime)
- `app/favicon.ico` — Next.js default behållen (memory `feedback_dont_delete_auto_files`)
- `app/manifest.ts` — Web App Manifest med G2 vit splash default

## Loggar

Detaljerad generation-log: `web/jobbpilot-web/public/brand/raw/generation-log.txt`
(timestamp | tag | aspect-ratio | bytes | elapsed-ms | Replicate output-URL).

Counter: `web/jobbpilot-web/public/brand/raw/generations-used.txt` = 34.

## Skript

- `scripts/generate-brand-assets.mjs` — generic 1-prompt-runner mot Replicate (HARD_CAP 50, retry-on-429)
- `scripts/fas1-batch.mjs` — 9 prompt A-D runs (Fas 1 ursprunglig)
- `scripts/fas1-rev1-batch.mjs` — 12 prompt A-F runs (Fas 1 symbol-led pivot)
- `scripts/fas2-batch.mjs` — 12 vortex + compass förfinings-prompts (Fas 2)

Inga npm-dependencies tillagda. Ren `fetch` + `node:fs/promises` + `node:child_process`.

## Säkerhets-not

`REPLICATE_API_TOKEN` läses via `readEnvToken()` från `.env` (gitignored). Aldrig
loggad i klartext, aldrig i error-meddelanden, aldrig commit:ad. Verifiering: se
`docs/reviews/2026-05-25-fpre-punkt6-security-audit.md`.
