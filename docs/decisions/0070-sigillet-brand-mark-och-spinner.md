# ADR 0070 — Sigillet: ny brand-mark och spinner (logo-översyn 2026-06-13)

**Datum:** 2026-06-13
**Status:** Accepted
**Beslutsfattare:** Klas Olsson (produktägare)
**Supersedes (delvis):** [ADR 0068](./0068-gron-accent-identitet-f4-banner.md) **Beslut 1 — logo-mark-noten** ("logotypens kompass-mark förblir blå med guldprick `#FFCD00`; `--jp-gold` INGEN konsument ännu, logo-översyn separat"). Det enda som supersedas är just det undantaget — ADR 0068:s gröna accent-identitet (Beslut 1–5) och alla övriga beslut i ADR 0068 **består oförändrade**.
**Relaterad:** [ADR 0068](./0068-gron-accent-identitet-f4-banner.md) (grön accent-identitet — reserverade `--jp-gold` för denna oversyn), [ADR 0069](./0069-product-rename-jobbpilot-to-jobbliggaren.md) (produktbyte → Jobbliggaren — explicit signalerade att 0068:s logo-mark-not "måste omprövas när det arbetet börjar").

---

## Kontext

ADR 0068 (grön accent-identitet, 2026-06-10) genomförde ett app-wide accentbyte blå/navy → mörkgrön. Beslutet innehöll ett explicit undantag för logotypens mark: *"logotypens kompass-mark förblir blå med guldprick `#FFCD00`"* och reserverade guld-token `--jp-gold` (`#E8C77B`) *"för logo-översynen separat"*. Navy-rampen behölls definierad som substrat till kompassen.

ADR 0069 (produktbyte till Jobbliggaren, 2026-06-13) noterade explicit att ADR 0068:s logo-mark-not *"måste omprövas när det arbetet börjar"*. Produktbytet stärkte motivet: den 4-punktiga kompassen bar associationer till det pensionerade varumärket JobbPilot och det AI-clichéade "pilot"-suffixet. "Jobbliggaren" — ett officiellt register/liggare — kallar på en annan semantik.

Den 13 juni 2026 genomförde Klas en dedikerad logo-utforskning i Claude Design. Tre formkoncept utvärderades (kompass behållen, abstraherad kompass, civilt registersigill). Koncept B "Sigillet" valdes, med smooth disc som kantval (milled-stamp-edge-varianten avvisades som för retro vid små storlekar). Spinner-animationen itererades: golden arc + pulsing rows valdes över white arc + static seal. Implementationen är klar (12 filer: brand-mark-svg, brand-logo, brand-spinner + tester, icon.svg, apple-icon, opengraph-image, twitter-image, manifest.ts, globals.css); verifiering grön: tsc, vitest 874/874, eslint 0 errors, next build, satori OG/apple render.

### Semantisk koppling till produktnamnet

"Liggare" = officiellt register, förteckning. Sigillet är formspråket för ett sådant instrument: en fylld disc med inner ring och tre liggar-rader (ledger rows), varav mittenraden är guld med ett litet bock-tecken — en loggad post. Sigillet läser som ett civic register-seal och är semantiskt samstämmigt med Jobbliggaren-namnet och produktens `.jp-table--flat`-designspråk på ett sätt kompassen aldrig kunde vara.

---

## Beslut

Ersätt den 4-punktiga kompassen (navy + guldprick `#FFCD00`) med **Sigillet** — ett fyllt civilt registersigill i grön + guld — som Jobbliggaren-varumärkets primära mark. Introducera **BrandSpinner** ("Sigillet i rörelse") som dedikerad laddningsindikator. Uppdatera alla brand-ytor (icon.svg, apple-icon, opengraph-image, twitter-image, manifest.ts, globals.css) till Sigillet-formen.

---

## Alternativ som övervägdes

### Alt A — Behåll kompassen, byt bara färg navy → grön
**För:** Minst churn; kompassen är välkänd i codebasen; inga nya component-kontrakt.
**Emot:** Kompassen bär "pilot"-navigations-semantik som aktivt motverkar Jobbliggaren-positioneringen (liggare = register, inte navigator). Färgbytet ensamt löser inte semantik-konflikten. Klas avvisade: "Kompassen hör till JobbPilot-eran."

### Alt B — Sigillet (smooth disc + inner ring + liggar-rader med guld-bock) — vald
**För:** Semantiskt exakt — ett officiellt registersigill kodar civic trust och register-metaforen direkt. Smooth disc är läsbar vid alla storlekar (16px–180px). Guld-bocken ger en distinkt "loggad post"-signifierare som binder ihop UI:ets primära åtgärd (logga ansökan) med varumärket. Konsumerar `--jp-gold`-token som ADR 0068 reserverade.
**Emot:** Kräver ny komponent-arkitektur (3-fills istället för 2; explicita `--jp-mark-*`-tokens istället för `currentColor`). Accepted.

### Alt C — Milled-stamp-edge-variant av Sigillet (tandad kant)
**För:** Traditionellt sigill-uttryck; stark autenticitets-signal.
**Emot:** Kants-tänderna förlorar läsbarhet under 32px och skapar brus i favicon/apple-icon-kontexterna. Klas: "för retro vid små storlekar." Avvisad till förmån för smooth disc.

### Alt D — Abstrakt kompass (inga pilar, geometrisk cirkel + kors)
**För:** Bevara visuell kontinuitet från kompassen; modernisera uttrycket.
**Emot:** Löser inte semantik-konflikten; "kors i cirkel" är för generiskt som seal utan distinktivt element. Avvisad.

### Alt E — White arc för spinnern (istället för gold arc)
**För:** Mer neutral, enklare animation; vit arc försvinner inte i mörkt läge.
**Emot:** Vit arc har ingen semantisk koppling till guld-bocken och liggar-raden. Klas valde explicit gold arc + "rader pulserar" som parad animation — guld-arken roterar längs inner ring medan rad-pulserna sekventiellt aktiveras, vilket speglar "en post registreras"-metaforen. Avvisad.

---

## Konsekvenser

### Positiva
- Varumärket är semantiskt samstämmigt med Jobbliggaren-namnet och `.jp-table--flat`-designspråket.
- `--jp-gold` (`#E8C77B`) får sin första konsument — token-reservationen i ADR 0068 är infriад.
- BrandSpinner är en ren CSS-animation utan JS-beroende, `prefers-reduced-motion`-säker (statisk seal vid motion-off).
- Navy-rampen har nu noll aktiva konsumenter på brand-ytor och kan städas i en separat F-städ (explicit deferred, se nedan).
- OG (1200×630) och apple-icon (180×180) renderas korrekt i satori; manifest `theme_color` uppdaterad till grön `#15603F`.

### Negativa
- `BrandMarkSvg` går från 2 fills till 3 (primary/accent/paper). Konsumerande sidor måste exponera tre `--jp-mark-*`-tokens; CSS-kontraktet är uppbruten mot den gamla 2-fills-API:n.
- `brand-logo.tsx` slutar använda `currentColor` och passerar explicita `--jp-mark-*`-tokens — kan bryta dark-mode-scoped overrides om fler ytor läggs till framöver (kräver token-scopning, inte hardkodade hex).
- Wordmark-färg byter navy → ink (`--jp-ink-1`); den dark-scoped white-topbar-override pekas om navy `#133F73` → ink `#0C1A2E`. Alla ställen som nyttjar den gamla navy-wordmark-färgen måste uppdateras.
- Navy-kompassen (`#FFCD00` guldprick, `--jp-brand-accent`-token) är nu officiellt pensionerad. Token `--jp-brand-accent` har inga kvarvarande konsumenter — men rampens definition (`--jp-navy-*`) lämnas orensad till F-städ.

### Deferred: navy-ramp-städ
Navy-rampen (`--jp-navy-*`) definierades i ADR 0068 som substrat för kompassen. Nu när kompassen är ersatt av Sigillet är navy-rampen konsumentlös. Städning (grep-verifierad nollkonsumtion → ta bort token-definitionerna) deferreras till separat F-städ-PR — samma mönster som ADR 0068 etablerade för oaccesserade accent-steg.

---

## Implementation

Beslutet levereras i **två faser** (design-reviewer Major 2 + Klas-beslut 2026-06-13: shippa
marken nu — ren, godkänd, fullt verifierbar — och spinnern i en fokuserad följ-PR där den
wire:as till ett verkligt laddnings-läge, spinner-vs-skeleton-doktrinen dokumenteras, och den
visual-verifieras på Klas stack).

**Fas 1 — brand-marken** (branch `feat/jobbliggaren-mark-spinner`, från main `017eb89`):

| Fil | Förändring |
|---|---|
| `brand-mark-svg.tsx` | Ny Sigillet-form: smooth green disc `--jp-accent-800` (`#15603F`) + thin inner ring + 3 liggar-rader paper `#FFFFFF`; mittenrad guld `--jp-gold` (`#E8C77B`) med bock-ikon; 3 CSS-fills (primary/accent/paper) via `--jp-mark-*`-tokens |
| `brand-logo.tsx` | Slutar använda `currentColor`; passerar explicita `--jp-mark-*`-tokens; wordmark-färg navy → `--jp-ink-1`; dark scoped-white-topbar-override repoints navy `#133F73` → ink `#0C1A2E` |
| `icon.svg` | Grön Sigillet-favicon |
| `apple-icon.tsx` (satori) | 180×180 grön Sigillet; satori-verifierad |
| `opengraph-image.tsx` (satori) | 1200×630 grön Sigillet; satori-verifierad |
| `twitter-image.tsx` (satori) | 1200×630 grön Sigillet; satori-verifierad |
| `manifest.ts` | `theme_color` navy `#0A2647` → grön `#15603F` |
| `globals.css` | `--jp-mark-primary`, `--jp-mark-accent`, `--jp-mark-paper` tokens; dött `--jp-brand-accent` (`#FFCD00`) retired; navy-ramp deprecated-kommenterad |
| Tester (2 st) | brand-mark + brand-logo geometri-lock — vitest 874/874 grönt |

**Verifiering grön (Fas 1):** tsc 0 fel, vitest 874/874, eslint 0 errors, next build, satori OG 1200×630 + apple 180×180 runtime-renderade korrekt.

**Fas 2 — BrandSpinner** (följ-PR): "Sigillet i rörelse" (gold arc roterar längs inner ring +
rader pulserar sekventiellt; ren CSS, `prefers-reduced-motion` → statisk seal). Motionen är
design-reviewer-godkänd (Alt E). Levereras wire:ad till ett verkligt laddnings-läge med
dokumenterad spinner-vs-skeleton-doktrin (civic-doktrinen är annars skeleton-baserad/icke-
animerad) och visual-verifierad. Komponent-koden är prototypad och låst i chatt 2026-06-13.

**DESIGN.md §11 + rad 66** (logo-krav + accent-not) uppdateras i samma Fas 1-PR under approval-hook.

---

## Referenser

- Klas logo-utforskning 2026-06-13 (Claude Design — koncept A/B/C, iterering smooth vs milled edge, spinner-val gold arc + pulsing rows)
- [ADR 0068](./0068-gron-accent-identitet-f4-banner.md) — grön accent-identitet; reserverade `--jp-gold`; logo-mark-noten som supersedas av denna ADR
- [ADR 0069](./0069-product-rename-jobbpilot-to-jobbliggaren.md) — produktbyte Jobbliggaren; signalerade att 0068:s logo-mark-not måste omprövas
- [ADR 0052](./0052-design-system-v3-modern-civic.md) — designsystem v3; `@theme inline`-brygga + OCP-indirektion som möjliggör `--jp-mark-*`-tokenkontraktet
- [ADR 0016](./0016-civic-design-language.md) — civic utility som arkitekturkrav (Sigillet-formen speglar detta)

---

## Amendment 2026-06-13 — Fas 3: header-lockup + tagline

Fas 1 levererade den statiska marken; Fas 2 (#72) wirade BrandSpinner. **Fas 3**
förstorar headern och lägger taglinen i lockupen — Klas: vår wordmark/mark var
mycket mindre än Platsbankens header.

- `BrandLogo` `full`-varianten är nu den fulla header-lockupen: mark + en staplad
  [wordmark / tagline]. Taglinen "Den svenska jobbansökningshanteraren" (= OG-/
  social-taglinen, Klas-STOPP A val H2 2026-05-25) sätts som under-rad, likt
  Platsbankens "SWEDISH PUBLIC EMPLOYMENT SERVICE".
- **Behandling (Klas-val 2026-06-13 efter visual-verify):** sentence-case (12px,
  vikt 500, `--jp-ink-2`) — konsekvent med `opengraph-image.tsx`, INTE versal.
  Variant B (versal/tecken-spärrad, Platsbanken-trogen) övervägdes och valdes bort.
- Mark 32 → 40px, wordmark 19 → 24px, båda header-barerna (`.jp-header__inner` =
  app + gäst-shell, `.jp-land-top__inner` = landing + site-header) 68 → 88px.
  Appliceras konsekvent på alla fyra brand-headers via `BrandLogo` (Platsbanken
  visar taglinen överallt).
- `mark`-varianten (minimala kontexter) oförändrad — bara sigillet, ingen tagline.
- a11y: taglinen `aria-hidden` (dekorativ; `.jp-brand`-länken bär accessible name).

Levererad i logo-Fas-3-PR (egen change-reason, SoC). Inga nya tokens; rena
brand-/header-CSS-ändringar. DESIGN.md §11-spegling pending Klas approve-spec-edit.

---

*ADR-index underhålls av docs-keeper. ADR 0070 fastställer Sigillet som Jobbliggarens brand-mark, supersederar ADR 0068 Beslut 1:s logo-mark-undantag (kompassen pensionerad, `--jp-gold` aktiverad), och introducerar BrandSpinner som ren CSS-animation.*
