# Design-review: Logo-översyn "Sigillet" + BrandSpinner (branch `feat/jobbliggaren-mark-spinner`)

**Reviewer:** design-reviewer (veto: civic-utility, a11y, design-token discipline)
**Date:** 2026-06-13
**Base:** origin/main `017eb89`
**Scope:** 12 files — brand-mark-svg, brand-logo, brand-spinner (+3 tests), icon.svg,
apple-icon, opengraph-image, twitter-image, manifest.ts, globals.css
**Status:** ⚠ Changes requested (no code Blocker — 2 Major, 3 Minor + 1 Praise)
**Auktoritet:** DESIGN.md §1 (civic-utility), §11 (logotyp), jobbpilot-design-principles
(regel 1/5/6 + anti-pattern-katalog), jobbpilot-design-tokens, jobbpilot-design-a11y
(§4 kontrast, §7 motion), ADR 0070 (Accepted), ADR 0068-amendment.

---

## Verdict (kort)

Sigillet är ett **starkt civic-utility-val** — ett fyllt registersigill läser som
1177/Skatteverket-allvar, inte AI-trend. Token-disciplinen är exemplarisk: noll
hårdkodade hex i körande komponentkod (favicon/satori-undantagen är dokumenterade och
korrekta), ren `--jp-mark-*`-OCP-indirektion, grep-verifierad nollkonsumtion av den
pensionerade `--jp-brand-accent`/`#FFCD00`. Kontrasten håller i **alla fyra** kontexter
(light app, dark app, vit header, vit landing-topbar) eftersom marken är tema-stabil per
design. Inget veto-grundande fynd i körande UI.

De två Major-fynden är **inte** estetik: (1) DESIGN.md §11 + rad 66 är fortfarande
kompass-text trots att ADR 0070 rad 94 lovade spec-edit i samma PR — spec-vs-kod-drift
som bara Klas kan stänga (approval-hook); (2) `BrandSpinner` shippar **utan konsument**
och inför en animerad laddnings-indikator i en kodbas vars dokumenterade laddnings-
doktrin (`.jp-skeleton`) explicit är "ingen puls, inget animeras" — den motsättningen är
oförsonad och spinnern kan inte visuellt verifieras i kontext förrän den wire:as.

---

## Blockers

Inga.

---

## Major

### M1. DESIGN.md §11 + §-rad 66 beskriver fortfarande kompassen — spec-drift mot levererad kod
**Fil:** `DESIGN.md:66` och `DESIGN.md:188-192`
- **Nuvarande (rad 66):** "Logotypens kompass förblir navy + guldprick — varumärket byter
  inte färg." **Nuvarande (§11, rad 190-192):** "Prioriteras senare — designas inför
  klass-launch (fas 8) ... Föreslagna riktningar: stiliserad kompass (pilot-metafor),
  monogram JP, Platsbanken-aktig cirkel."
- **Krävs:** §11 + rad 66 uppdateras till Sigillet-marken (grön registersigill +
  `--jp-gold`, brand-mark levererad nu — inte fas 8). ADR 0070 rad 94 förband sig till
  detta: *"DESIGN.md §11 (logo-krav) uppdateras i samma PR separat under approval-hook
  (hanteras av Klas)."* Den editen har inte skett.
- **Motivering:** Edge case i min instruktion — "Deliberate DESIGN.md deviation requires
  an ADR **or** DESIGN.md update." ADR-delen är uppfylld (ADR 0070 Accepted supersederar
  ADR 0068 Beslut 1 + ADR 0068-amendment applicerad korrekt), så detta är **inte** ett
  kod-Blocker. Men spec-filen släpar och kontrakterades i samma PR. Detta är en
  Klas-approval-hook-åtgärd (agenter får ej editera DESIGN.md per CLAUDE.md §1.6/§9.2),
  inte en fix till nextjs-ui-engineer. Riktad till Klas.
- **Not:** Samma stale text finns i `jobbpilot-design-principles` (regel 5: "Logotypens
  kompass förblir navy + guldprick") — bör synkas av docs-keeper/adr-keeper i städ-svansen.

### M2. BrandSpinner shippar utan konsument + motsäger den dokumenterade `.jp-skeleton`-laddningsdoktrinen
**Fil:** `src/components/brand/brand-spinner.tsx` (hela), grep: noll `<BrandSpinner>`-call-sites
- **Nuvarande:** `BrandSpinner` förekommer endast i sin egen fil + test. Den existerande
  laddnings-primitiven `.jp-skeleton` (`globals.css:2740`) är explicit dokumenterad:
  *"ingen shimmer, ingen puls, ingen gradient, ingen glow ... Rent statiskt block ...
  Inget animeras."* BrandSpinner inför motsatsen — roterande guldbåge (`jp-spin 1.15s
  linear`) + sekventiell rad-puls (`jp-row-pulse 1.5s ease-in-out`, opacity 0.4↔1).
- **Krävs:** Antingen (a) wire:a BrandSpinner till ett verkligt laddningstillstånd så den
  kan visuellt verifieras i kontext per den obligatoriska `pnpm visual-verify`-grinden
  (AGENTS.md), och dokumentera **när** spinner vs skeleton används (t.ex. spinner =
  knapp/inline-action-pending, skeleton = list-/resultat-laddning); eller (b) håll
  komponenten tills en konsument finns. Reconcile:a motsättningen mot `.jp-skeleton`-
  "puls förbjuden"-rationalen — antingen i ADR 0070-not eller DESIGN.md.
- **Motivering:** civic-utility regel 3 ("inga fyllnadselement" — varje komponent ska bära
  funktion i kontext) + AGENTS.md ("visual-verification mandatory when ... markedly
  changing rendered UI") + jobbpilot-design-principles anti-pattern-listan ("auto-spelade
  animationer"). **Inte Blocker:** ADR 0070 Alt E dokumenterar Klas explicita val av "gold
  arc + pulsing rows" över "static seal", motion är reduced-motion-säker (se Praise P1),
  och en o-konsumerad komponent kan inte regrediera någon live-skärm. Men en animerad
  spinner mot en uttalat statisk laddnings-doktrin, levererad oanvänd och overifierad i
  kontext, måste lyftas.
- **Spinner-motion-bedömning (separat från konsument-frågan):** Själva rörelsen är
  **acceptabel** för en laddnings-indikator. `linear`/`ease-in-out` (ej bouncy/spring),
  opacity + rotation (ingen scale/translate/partikel), och en spinner är per definition en
  funktionell status-indikator vars syfte är att animera under en deferred operation —
  anti-pattern-regeln "auto-spelade animationer" siktar på dekorativ auto-play (hero-glitter),
  inte laddnings-status. Varje civic-referens (GOV.UK, 1177) har en animerad spinner.

---

## Minor

### m1. Komment-drift: brand-mark-svg.tsx säger `--jp-mark-paper → --jp-surface`, men token är hård `#FFFFFF`
**Fil:** `src/components/brand/brand-mark-svg.tsx:12`
- **Nuvarande:** `// paperFill inre ring + rader (--jp-mark-paper → --jp-surface, normalt vitt)`
- **Krävs:** `--jp-surface` → `#FFFFFF (fast papper, EJ --jp-surface — se globals.css)`.
  `globals.css:56` definierar medvetet `--jp-mark-paper: #FFFFFF` och INTE `--jp-surface`
  (eftersom raderna sitter på den gröna skivan, inte sid-ytan, och får ej skifta i dark).
  Komment-rad 12 motsäger den faktiska token-definitionen och hela tema-stabilitets-poängen.
- **Motivering:** Konsistens; risk för felaktig framtida refaktor om kommentaren tas som SSOT.

### m2. `--jp-mark-*` saknas i jobbpilot-design-tokens-skillen (gold står fortfarande "INGEN konsument")
**Fil:** dokumentation (`.claude/skills/jobbpilot-design-tokens`), inte kod
- **Nuvarande:** Token-skillen listar `--jp-gold` som "Signatur — INGEN konsument ännu
  (logo-översyn separat)" och saknar `--jp-mark-primary/-accent/-paper` helt.
- **Krävs:** docs-keeper synkar token-skillen: `--jp-gold` har nu sin första konsument
  (sigillet via `--jp-mark-accent`) + lägg till `--jp-mark-*`-trippeln med not om
  tema-stabilitet. Inte denna PR:s kod-scope; städ-svans.
- **Motivering:** Token-skillen säger "vid avvikelse vinner globals.css" — så ingen
  funktionell risk, men driften bör stängas.

### m3. Spinner `viewBox`/geometri dupliceras manuellt mot BrandMarkSvg SSOT
**Fil:** `src/components/brand/brand-spinner.tsx:25-72`
- **Nuvarande:** Spinnern återskapar disc (r45) + ring (r37) + tre rader + bock som
  litterala element istället för att komponera `BrandMarkSvg`. Geometri-lock finns nu på
  tre ställen (BrandMarkSvg, icon.svg-mirror, BrandSpinner).
- **Krävs:** Överväg att låta spinnern rendera `BrandMarkSvg` som statisk bas + lägga
  enbart den roterande `__arc`-cirkeln + rad-klasser ovanpå, så geometri-justeringar inte
  kräver trippel-synk. Inte blockerande (testerna geometri-lockar 3+3+1), men minskar
  drift-yta.
- **Motivering:** DRY/SSOT — samma rationale som icon.svg-mirror-kommentaren redan erkänner.

---

## Bra gjort (Praise)

### P1. Reduced-motion-hanteringen är bättre än baslinjen — inte bara "animation: none"
`globals.css:673` lägger `opacity: 1` på `.jp-brand-spinner__row` i reduced-motion-blocket,
utöver `animation: none`. Den globala `*`-guarden (rad 377) sätter bara
`animation-duration: 0.01ms` — vilket skulle kunna **frysa raderna på 0.4-opacity** (puls-
keyframens dim-läge). Den explicita `opacity: 1`-overriden garanterar att en reduced-motion-
användare ser ett fullt läsbart statiskt sigill, inte tre halvtransparenta rader. Det är
korrekt och omtänksam a11y (jobbpilot-design-a11y §7).

### P2. Tema-stabil mark via ren token-indirektion — robust i alla fyra kontexter
`--jp-mark-primary/-accent` pekar på `--jp-accent-800`/`--jp-gold` (ingen av dem
dark-skiftad), `--jp-mark-paper` är hård `#FFFFFF`. Verifierat: ingen av de tre
`[data-theme="dark"]`-blocken (globals.css:198, 470) eller de scopade vit-header/vit-
topbar-overriderna (rad 571, 2912) re-pinnar `--jp-mark-*` eller `--jp-gold` — så sigillet
renderar identiskt grönt/guld/vitt i light app, dark app, vit header och vit landing-topbar.
Det är exakt rätt: sigillet sitter på sin egen gröna skiva, inte sid-ytan. Korrekt
motiverat i token-kommentaren (rad 52-56).

### P3. Token-disciplin + clean retirement
Noll hårdkodade hex i `brand-logo.tsx`/`brand-mark-svg.tsx`-körkod (allt via `--jp-mark-*`).
Favicon (`icon.svg`) + satori-ytor (apple/og/twitter) använder litterala hex — korrekt och
dokumenterat undantag (favicon saknar CSS-kontext; satori `ImageResponse` löser ej CSS-vars).
`--jp-brand-accent`/`#FFCD00` grep-verifierat till noll körande konsumenter (finns bara i en
deprecation-kommentar) — ren pensionering, ingen död token kvar i bruk. ADR 0068-amendment
korrekt applicerad med ADR 0070-korsreferens.

### P4. Kontrast håller med marginal (beräknat, alla teman)
- Grön bock `#15603F` på guld `#E8C77B`: **4.64:1** (klarar 3:1 grafik-golv, även 4.5:1)
- Guld rad `#E8C77B` på grön skiva `#15603F`: **4.64:1** (informationsbärande grafik i marken)
- Vit papper `#FFFFFF` på grön skiva: **7.56:1**
- Grön skiva `#15603F` på vit sida: **7.56:1** (marken står av mot ytan)
- Wordmark `#0C1A2E` på vit: **17.46:1** (AAA); OG-tagline `#455366` på vit: **7.83:1**

Bocken ser svag ut i 180px-apple-rendern men det är anti-alias/storlek, inte kontrast —
4.64:1 är mätt. Eftersom marken är tema-stabil håller dessa tal i dark också.

### P5. Wordmark navy→ink-beslutet är korrekt löst i scoped-vit-topbar-dark
`.jp-brand color` navy `#133F73` → `--jp-ink-1`, och den dark-scopade vit-topbaren
(`globals.css:2930`) låser wordmarken till `#0C1A2E` (~17:1 mot vit) eftersom topbaren är
vit-på-vit oavsett tema. Utan den pinnen hade `--jp-ink-1` skiftat till `#F4F7FC` och
wordmarken blivit osynlig. Rätt analyserat, rätt löst, korrekt kommenterat.

---

## Pending verification (utanför min container — måste köras på Klas stack)

`pnpm visual-verify` (Playwright Chromium-header-screenshots, light/dark, viewports) kunde
**inte** köras här — CDN blockerad av nätverkspolicyn. Därför är **in-app header-lockupen
(BrandLogo i `.jp-header`/`.jp-land-top`) + wordmark ink-theming (light/dark + scoped-vit-
topbar) overifierad-av-screenshot**. Jag har bedömt det jag kan från koden (token-flödet
verifierat ovan, P2/P5) och från satori-rendren (`/home/user/jl-og.png`,
`/home/user/jl-apple.png` — sigill + wordmark + tagline korrekta). Den formella
screenshot-grinden (AGENTS.md, "design-reviewer reviews the screenshots") **måste köras
på Klas stack** innom header-lockupen och M2-spinnern (om den wire:as) godkänns visuellt.
Detta är ett konstaterande, inte ett veto — koden stödjer rätt beteende.

---

## Sammanfattning

**0 Blockers, 2 Major, 3 Minor, 5 Praise.** Inget veto-grundande fynd i körande UI —
Sigillet är civic-utility-korrekt, token-disciplinerat och kontrast-säkert i alla teman.

- **M1 (DESIGN.md §11/rad 66-drift):** Klas-approval-hook-åtgärd — agenter får ej editera
  DESIGN.md. ADR-täckningen finns (0070 Accepted), men spec-editen som ADR 0070 rad 94
  kontrakterade i denna PR är inte gjord. Stäng innan merge eller dokumentera varför den
  glider till uppföljnings-PR.
- **M2 (BrandSpinner utan konsument + skeleton-doktrin-motsättning):** Delegera till
  nextjs-ui-engineer — wire:a till ett verkligt laddningstillstånd (möjliggör
  visual-verify) **och** dokumentera spinner-vs-skeleton-användning, eller håll komponenten.
  Motionen i sig är godkänd.
- **m1-m3:** Komment-fix (nextjs-ui-engineer), token-skill-synk (docs-keeper),
  DRY-övervägande (nextjs-ui-engineer, ej blockerande).
- **Pending:** `pnpm visual-verify` måste köras på Klas stack för header-lockup +
  wordmark-theming + (ev.) spinner-i-kontext.

Re-review efter M2-beslut (wire eller håll) + DESIGN.md §11-edit. Ingen Fas-deferral-
manifest behövs — detta är pågående logo-scope, inte F4–F7-arbete.
