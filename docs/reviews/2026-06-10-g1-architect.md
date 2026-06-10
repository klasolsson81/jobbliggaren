# dotnet-architect — G1 token-arkitektur (grön accent + F4-gradient)

**Datum:** 2026-06-10 · **Agent:** dotnet-architect (token-arkitektur-dom) · **Underlag:** handoff + referens-HTML + globals.css on-disk

---

## 1. Accent-ramp: NY namnrymd `--jp-accent-*` (redefiniera INTE navy-värdena)

**Logotyp-fällan (hård blocker):** `.jp-brand { color: var(--jp-navy-700) }` + BrandLogo `primaryFill="currentColor"` → grön hex i navy-700 färgar kompassen grön (förbjudet). Navy-700 MÅSTE överleva blå. Dessutom: hue-namngiven token med fel hue = maintenance-fälla (samma lärdom som v2→v3; Fowler branch-by-abstraction). Rampen designas med navy-stegens semantik → mekanisk rename `var(--jp-navy-` → `var(--jp-accent-`.

**Mappning (light → dark):** 900 `#0B2A1E` (=hero-from)/ej skiftad · 800 `#15603F`/EJ skiftad (FILL-kontrakt) · 800-hover `#1E6B4C` (=hero-to)/ej skiftad · 700 `#15603F`/`#6EE7A8` (TEXT/BORDER/fokus) · 600 `#1E6B4C`/`#A7F3D0` (hover; A7F3D0=emerald-200, konservativ härledning) · 500 `#2E8B63`/`#3E8E68` (chart) · 300 `#74C29A`/`#2E5C46` · 100 `#D3E7DC`/`#0E2A1E` · 50 `#E9F2ED`/`#0E2A1E` (handoff).

**WCAG (beräknat):** #15603F på vitt 7.56:1, på canvas #F4F6FA 7.0:1, på accent-50 6.7:1; vit på #15603F 7.56:1, på hover #1E6B4C 6.4:1; #6EE7A8 på #0B1525 11.9:1, på #1B2B47 9.1:1; avatar 6.1:1. Dark-hover FÖRBÄTTRAS de facto (navy-700-fill #4F8AD0+vit ≈3:1 borderline → #1E6B4C 6.4:1).

**Navy-rampens öde:** behålls definierad (logo-substrat); efter rename är legitima konsumenter `.jp-brand` + scoped header-pins. Kommentera "ENDAST logotyp"; F-städ efter grep-nollkonsumtion.

## 2. Alias-flip (Tailwind/shadcn gratis)

`--jp-brand-50/100/300/500→accent-d:o; -600→accent-800 (shadcn --primary=knapp-fill); -700→accent-700; -900→accent-900; --jp-brand-accent #FFCD00 OFÖRÄNDRAD`. B5-overriden → accent-700/-600. Scoped vit-header-pins måste få light-accent-pins (#15603F/#1E6B4C/#D3E7DC/#E9F2ED) — annars ljusgröna #6EE7A8-länkar på vit header i dark (AA-fail, samma buggklass som design-reviewer M2 2026-05-25).

## 3. Gradient + pagehero-mekanik

Composite-token: `--jp-hero-gradient: linear-gradient(118deg, from 0%, mid 60%, to 100%)`; `--jp-hero-bg` omdefinieras till SOLID ankare `#14503A` (border/text-på-vit-knapp-konsumenter, 9.4:1 på vitt); `--jp-hero-ink-soft → rgba(255,255,255,0.78)` (var #BBCCE5 navy-tint — MÅSTE bytas). `--jp-hero-radius` behövs ej (6px = `--jp-r-md`). `.jp-pagehero`/`.jp-empty--brand`: `background: var(--jp-hero-gradient)` — solid-konsumenterna läser ankaret; hardkodade navy-hover-hex → accent-50/-900. **`.jp-hero` (/jobb):** wrapper `var(--jp-canvas)` + platta `--jp-hero-gradient` + `--jp-r-md`; **`--jp-hero-canvas` (#FAF9F6) UTGÅR** (E1a:s varma ton var bärande för content-first-heron som F4 ersätter; varm rand runt grön platta läser som smuts) — supersede-kedjan noteras i ADR så design-reviewern inte revertar. Dark: gradient oförändrad + 1px `--jp-border-soft`-hairline. Sök-knapp: ny tema-stabil `--jp-hero-sok-bg` (ink — inte grön).

## 4. Fokus: property-scoping, inte selector-override

`:root { --jp-focus: var(--jp-accent-700); }` (skiftar själv); TA BORT dark-blockets `#8FBEEF`. `.jp-hero__plate, .jp-pagehero, .jp-empty--brand { --jp-focus: #FFFFFF; }` — globala `*:focus-visible` löser vit automatiskt + komponenter som läser `var(--jp-focus)` följer med (ingen specificitets-kapplöpning). Explicita `outline: var(--jp-navy-700)`-regler → `var(--jp-focus)` (fokus är interaktionssemantik, inte brand). Vit mot #1E6B4C = 6.4:1 ≥ 3:1 (WCAG 2.4.7/1.4.11).

## 5. Dark-knappkontraktet består (fill/text-split 800 vs 700)

Fill: accent-800 #15603F båda teman + vit text; hover #1E6B4C båda teman. Text/länk/fokus i dark: #6EE7A8 — ALDRIG fill bakom vit text (1.4:1 total fail).

## 6. Guld

`--jp-gold: #E8C77B` införs UTAN konsument (bannern har ingen logga). `--jp-brand-accent #FFCD00` orörd (BrandLogo-konsument; "loggan ses över separat"). Konsolidering → logo-översynen.

## Fynd (hanteras i G1)

- **[Viktigt] Matchchip-grönkollision** (globals.css ~1502): `.jp-matchchip--mid` → accent-grön kolliderar med success-high (mid/high-distinktion kollapsar; WCAG 1.4.1-tänk). Föreslagen: mid → neutral (`--jp-ink-2`+`--jp-surface-2`+`--jp-border-strong`); design-dom — flagga Klas/design-reviewer, implementera ej tyst.
- **[Viktigt] shadcn `--ring` osynlig i dark** (rad ~400): `--ring: var(--jp-brand-600)` → mörkgrön på mörkt (~2.6:1; latent redan med navy). Fix: `--ring: var(--jp-focus)` + `--sidebar-ring` d:o. In-block per §9.6.
- **[Viktigt] Referensens placeholder #94A3B8 bryter AA** (2.8:1 på vitt) — on-disk `--jp-placeholder #626B78` (design-reviewer-rotfix samma dag) är rätt värde. WCAG > referens. *(Not: Klas-direktiv senare samma dag: ingen placeholder-TEXT alls — färgfrågan moot för bannern men tokenen består för andra inputs.)*
- **[NTH]** Dark-blockets `--jp-hero-canvas`-override blir död kod när E1a-heron rivs — ta bort i samma batch.
