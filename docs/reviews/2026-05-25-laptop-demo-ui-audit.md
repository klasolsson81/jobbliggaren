# Laptop-demo UI-audit (HP EliteBook 850 G8, 1920×1080, projektor)

**Datum:** 2026-05-25
**Auditerare:** Claude Code (CC), audit-uppdrag från Klas 2026-05-25
**Demo-mål:** 2026-05-26
**HEAD vid audit:** `9608870` (origin/main, status clean)
**Capture-korpus:** `C:/tmp/jobbpilot-audit/20260525-0906/` — 68 screenshots (17 routes × light/dark × full-page+viewport-fold)
**Capture-skript:** `web/jobbpilot-web/scripts/audit-capture.ts` (engångs-audit, beslut om bevarande efter Klas-triage)
**Datakällor:**
- Publika sidor: `http://localhost:3000` (lokal `pnpm dev` mot HEAD-koden)
- Auth-gated sidor: `https://www.jobbpilot.se` (prod-deploy + dev-test-konto via `VISUAL_AUTH_EMAIL/_PW`; backend = `https://dev.jobbpilot.se`)

> **OBS:** `dev.jobbpilot.se`-frontenden returnerar HTTP 404 (deploy-dev efter `v0.2.72-dev` ej rullad). Auth-screenshots kommer därför från www.jobbpilot.se (prod) som kan ligga några commits efter HEAD. Skillnaden mot HEAD är begränsad till F-Pre Punkt 6 brand-paket (ej landed på prod). Inga av fynden nedan är specifika för brand-paket-deltat.

---

## Sammanfattning för Klas

12 fynd identifierade. Två klassade som **dark-mode-blocker för demo** (Hög 1+2), tre **HÖGA dark-mode/projektor-riskar** (Hög 3+ Medel 4+7), resten polish/observation. **Inget bryter civic-utility-estetiken** — DESIGN.md-tonen är intakt. Fynden är distribuerade enligt:

- 3 **hög** (pre-existerande Blocker B1 + M1 från F-Pre Punkt 6 + dark-border-kontrast)
- 6 **medel** (layout-dead-zones, label-storlek, empty-state-inkonsistens)
- 3 **låg / observation** (Platsbanken-emoji, container-bredd, dev-test-namn i kicker)

**3 av 12 fynd kräver DESIGN.md-token-territorium** (Hög 3, Medel 7) eller marketing-hero scope-token (Hög 1) — STOPP, Klas `approve-spec-edit.sh`-väg. Resten är fixbart in-block i CSS/JSX utan token-ändring.

**Rekommenderad demo-strategi vid laptop:** kör **ljust läge** för demo. Dark-mode har 3 pre-existerande issues som bekräftades i denna audit (B1, M1, dark-border-kontrast). Att fixa allt tre innan morgonen ligger på gränsen — Klas-prioritering avgör.

---

## 1. Route-inventering (full lista)

### Publika
| Route | Komponent | Capture (light/dark) | Anteckning |
|-------|-----------|----------------------|------------|
| `/` | LandingPage (RSC) → LandingTopbar + LandingHeroSection + LandingFeatures + LandingFooter | `landing__*__{full,fold}.png` | navy-hero + auth-card höger |
| `/logga-in` | LoginPage | `logga-in__*__{full,fold}.png` | centrerad 440px-form |
| `/registrera` | RegisterPage | `registrera__*__{full,fold}.png` | navy-pagehero + form |
| `/vantelista` | VantelistaPage | `vantelista__*__{full,fold}.png` | navy-pagehero + form |
| `/villkor` | VillkorPage | (saknas i denna capture) | marketing-inner, ej kritisk |
| `/cookies` | CookiesPage | `cookies__*__{full,fold}.png` | marketing-inner |

### Gäst (anonym demo)
| Route | Capture | Anteckning |
|-------|---------|------------|
| `/gast/oversikt` | `gast-oversikt__*` | DEMO-banner + page-hero + onboarding-modal |
| `/gast/jobb` | `gast-jobb__*` | DEMO-banner + jobb-hero + 8 exempelannonser |
| `/gast/ansokningar` | `gast-ansokningar__*` | DEMO-banner + pipeline-pattern |
| `/gast/cv` | `gast-cv__*` | DEMO-banner + CV-mock |

### Auth-gated (app shell)
| Route | Capture | Anteckning |
|-------|---------|------------|
| `/oversikt` | `oversikt__*` | navy-pagehero + I-dag-kort + Notiser + Sammanfattning 3-col |
| `/jobb` | `jobb__*` | full-bredds-jobb-hero + sökrad + lista |
| `/ansokningar` | `ansokningar__*` | navy-pagehero + tabs + status-grupper med kort |
| `/cv` | `cv__*` | navy-pagehero + empty-state-kort |
| `/sokningar` | `sokningar__*` | empty-state (BE-perf hindrade populerad capture) |
| `/sparade` | `sparade__*` | empty-state dashed-border-kort |
| `/installningar` | `installningar__*` | 2-col formulär + toggles |

### Routes som EJ capturerades (utanför audit-scope)
- `/admin/granskning` — admin-only, ej demo-mål
- `/cv/[id]`, `/cv/ny`, `/ansokningar/[id]`, `/ansokningar/ny`, `/jobb/[id]` (detalj-sidor) — Klas verifierar interaktivt
- `/gast/jobb/[id]`, `/gast/ansokningar/[id]` — gäst-detaljer
- Modal-states (notifikationsbell, usermeny, drawer, hero-popovers)

---

## 2. Statisk scan — riskmönster vid 1920×1080

### 2.1 Layout-tokens
- **Container max-width 1200px** (`.jp-shell-transitional-container` `globals.css:456`, `.jp-container` `globals.css:464`, `.jp-header__inner` `globals.css:522`, `.jp-pagehero__inner` `globals.css:990`, `.jp-land-top__inner` `globals.css:2541`)
  → 360px gutter på vardera sida vid 1920. Per ADR 0052 v3-spec. **Observation only** — civic-utility-paritet med 1177/Digg.
- Inga `vh`-baserade höjder utom `.jp-auth-wrap min-height: 100vh` (acceptabelt för auth-läge).
- Inga hårdkodade Tailwind arbitrary widths (`w-[NNNpx]`/`min-w-[NNNpx]`) i `src/**`. Discipline holds.
- **Inga `vw`-värden** som kan klippa innehåll.

### 2.2 Hårdkodade px-bredder i CSS
| Selector | Värde | Källa | Status |
|----------|-------|-------|--------|
| `.jp-shell-transitional-container`, `.jp-container`, `.jp-header__inner`, `.jp-pagehero__inner`, `.jp-land-top__inner` | `max-width: 1200px` | globals.css | Per ADR 0052 |
| `.jp-hero__searchblock` | `max-width: 760px` | `globals.css:902` | **Medel-fynd #4** dead-zone höger |
| `.jp-auth-card` | `max-width: 440px` | `globals.css:2474` | Medvetet smalt — auth-form |
| `.jp-land-hero__inner` | grid `1fr 420px` | `globals.css:2622` | Funkar 1920+ |
| `.jp-drawer` | `width: 240px` | `globals.css:730` | Mobil-drawer, ej kritisk för 1920 |
| `.jp-notif` | `width: 320px` | `globals.css:2412` | Notif-dropdown |

### 2.3 Typografi-storlekar < 14px
DESIGN.md §4 (ADR 0038) tillåter:
- Mono caps labels: **11.5px**/500/0.08–0.16em UPPERCASE
- Mono inline data (datum/ID/räknare): **13px**/500

**Riskfynd:**
- `.jp-land-top__stat__label` **10.5px** (`globals.css:2574`) — UNDER 11.5px-golvet → **Medel-fynd #7**
- `.jp-notif__item__time` **11.5px** mono (`globals.css:2440`) — på golvet, OK per spec
- Flertalet `.jp-pill__*`, `.jp-cell-meta`-stilar runt **11–13px** mono — alla inom spec

### 2.4 Border-tokens (kontrast)
ADR 0041 + amendment 2026-05-18:
- `--jp-border-modal` (modal/popover) **≥3:1 i dark** = `#64748B` per amendment
- `--jp-border-structural` (yt-chrome: kort/sektion/panel/sidebar där kanten är enda boundary) **≥3:1 i dark** = `#64748B` per amendment

**v3-omkalibrering (`globals.css:138-139`):** båda token re-homed på `--jp-border` (skiftar med tema). I dark är `--jp-border = #44598A` (`globals.css:161`).

**Kontrast-check `#44598A` vs canvas `#0B1525` (dark):**
- L_fg ≈ 0.075, L_bg ≈ 0.0084 → ratio ≈ **2.6:1** → **UNDER WCAG 1.4.11 3:1-golvet för UI-komponenter**

Detta strider mot ADR 0041-amendment-intentionen. v3-omkalibreringen prioriterade visuell harmoni över amendment-värdet `#64748B`. Det är **DESIGN.md/ADR-territorium** → **Hög-fynd #3** med Klas-`approve-spec-edit.sh`-väg om Klas vill återgå till amendment-värdet.

### 2.5 Color-kontrast — text på muted backgrounds
- `--jp-ink-3` dark = `#8DA0BD` vs canvas `#0B1525` ≈ **6.4:1** ✓ AA stora text
- `--jp-ink-3` light = `#7C8AA0` vs surface `#FFFFFF` ≈ **3.7:1** — under 4.5:1 AA för brödtext. Per DESIGN.md §4 är `text-tertiary` "endast dekorativt — informationsbärande text alltid text-secondary" → spec respekterad, men kräver verifiering att inga månads/veckodags-labels läcker in på tertiary (observation, kan inte säkert verifieras utan röntgen).

### 2.6 Border-token-misanvändning på strukturella ytor
Inga JSX-callsites använder `border-border-default` där `border-border-structural` borde stå — eftersom båda re-homed till samma underliggande `--jp-border`-värde i v3 (se 2.4). Före v3-omkalibreringen var distinktionen vital; efter den är den nominellt strukturell. **Per amendment-intentionen är detta drift.** Se Hög-fynd #3.

---

## 3. Fynd — sorterade per severity

> **Reading convention:** "Source: " = capture-fil-stam (suffix `__{light,dark}__{full,fold}.png`). "Spec-edit: Ja/Nej" = kräver DESIGN.md/ADR-token-ändring (= STOPP, Klas `approve-spec-edit.sh`).

### HÖG-1 — Dark-mode "Anmäl till väntelista" CTA osynlig på landing-hero (BLOCKER)
- **Source:** `landing__dark__fold` (line ~390px), `landing__dark__full`
- **Symtom:** Knapptexten "Anmäl till väntelista" är i `--jp-navy-800` (#0A2647) på button-bg `--jp-surface` som i dark **resolvas till #1B2B47**. Navy-på-navy = ~1.1:1 kontrast, **praktiskt osynlig**. Hero-bg-överlapp gör knapp-ramen knappt synlig.
- **Status:** Dokumenterad B1-Blocker från F-Pre Punkt 6 (`docs/sessions/2026-05-25-0641-fpre-punkt6-brand-paket.md` §"Pre-existerande issues"). Deferred-not vid Punkt 6-stängning.
- **Projektor-impact:** ✗ TROLIG — projektor förstärker low-contrast och Klas-användare i dark-mode kommer att tappa CTA helt.
- **Föreslagen fix:** Scoped dark-mode override i `.jp-land-hero` på sekundär-CTA — sätt `background` till en explicit ljus färg (`#FFFFFF` eller `#EAF1FA` = `--jp-navy-50` light) + behåll navy text. Alternativt navy-bg + vit text (matchar primär-knapp men förlorar hierarki). Multi-approach → **CTO-rond rekommenderas** innan fix.
- **Spec-edit:** Nej (scoped CSS-override i `.jp-land-hero` är layout-fix, inte token-edit). Om Klas väljer att introducera ny token (`--jp-button-secondary-on-navy-bg`) → ja.
- **Berör:** `web/jobbpilot-web/src/components/landing/landing-hero-section.tsx:50-77` (inline-style), `web/jobbpilot-web/src/app/globals.css:2611-2674` (`.jp-land-hero`)

### HÖG-2 — Vit `.jp-land-top` / `.jp-header` på dark body skapar synlig vit söm överst
- **Source:** `landing__dark__fold` (line 0–68px), `oversikt__dark__fold` (line 0–68px), alla auth-gated dark-screenshots
- **Symtom:** Header-shellen är vit i båda teman (medveten ADR 0052 Beslut 6 + Punkt 6 design-reviewer M2 brand-lock). I dark mode ser detta ut som en **vit reflekterande list överst** på en mörk sida. Klas-internt påpekat i Punkt 6-svans (M1-pre-existerande).
- **Status:** Dokumenterad M1-deferred från F-Pre Punkt 6.
- **Projektor-impact:** ✗ HÖG — vit yta projiceras starkast, drar blicken från innehållet, ser ut som buggad dark-mode.
- **Föreslagen fix:** Tre alternativ (per Punkt 6-deferral-notat):
  - **A:** Dark-aware `.jp-header` (egen scoped override som blir mörk i dark) — nödvändigt om vi vill att header följer canvas
  - **B:** Behåll vit topbar + förstärkt 1px border-separator + skugga underifrån för att etablera intent
  - **C:** Helt ta bort visuell brytning genom att alltid vit-canvas dark-mode (men då tappar vi dark-mode helt — inte ett alternativ)
- **Multi-approach** → **CTO-rond obligatorisk** innan fix (CLAUDE.md §9.6).
- **Spec-edit:** Tveksamt. A = scoped override per ADR 0052-trail, antagligen Nej (samma idiom som dark-lock i Punkt 6). B = ren CSS, Nej.
- **Berör:** `web/jobbpilot-web/src/components/shell/app-shell.tsx:359` (.jp-header), `globals.css:494` (border-bottom på .jp-land-top), och dark-overrides i samma fil

### HÖG-3 — `--jp-border-structural` dark `#44598A` under ADR 0041-amendment-golvet 3:1
- **Source:** `cv__dark__fold` (empty-state-kort har osynlig ram), `ansokningar__dark__fold` (utkast-kort har svag ram), `sokningar__dark__fold` (empty-state-kort försvinner)
- **Symtom:** ADR 0041-amendment 2026-05-18 låste `--jp-border-structural` till `#64748B` ≈3.6:1 i dark. v3-omkalibreringen (`globals.css:138-139`) re-homed den på `--jp-border` (=#44598A ≈2.6:1 i dark). Strukturella ytor (kort, dropdown, panel-kanter) tappar definition projicerat.
- **Projektor-impact:** ✗ HÖG — projicerade kort-ramar försvinner = innehållet flyter ut i canvas.
- **Föreslagen fix:** Återgå till amendment-värdet `#64748B` på `--jp-border-structural` (+ ev. `--jp-border-modal`) i dark, ELLER bumpa `--jp-border` dark från `#44598A` till `#64748B` för hela paletten.
- **Spec-edit:** ✓ **JA** — DESIGN.md §3-token-territorium / ADR 0041-amendment. **STOPP — Klas `approve-spec-edit.sh`-väg.**
- **CTO-rond rekommenderas** för approach-val (lokal token-override för `--jp-border-structural` i dark vs global `--jp-border`-bump).
- **Berör:** `web/jobbpilot-web/src/app/globals.css:138-139` (alias-rader) ELLER `:161` (dark-värdet)

---

### MEDEL-4 — `/jobb` hero har 600px tom navy dead-zone höger
- **Source:** `jobb__light__fold`, `jobb__dark__fold`, `gast-jobb__light__fold`
- **Symtom:** `.jp-hero__searchblock max-width: 760px` (`globals.css:902`). På 1920px-canvas (1200px innerwidth efter 360px-gutter) = sökrutan tar 760px vänster, ~440px tom navy-yta höger. Lookar AI-empty / wasted. Chips "Senaste sökningar"/"Sparade annonser" är top-right men nere på filterraden = dead.
- **Projektor-impact:** Mild — tom yta läser som "ofärdig" snarare än "lugn".
- **Föreslagen fix (multi-approach — CTO-rond):**
  - **A:** Utöka `.jp-hero__searchblock max-width` till 1000–1100px (sökrutan får andas vidare)
  - **B:** Flytta "Senaste sökningar" + "Sparade annonser" chip-paret ovanför filterraden och höger-justerad bredvid sökrutan (re-balanserar hero-höjd)
  - **C:** Lägg in en sekundär informationsmodul höger om sökrutan (t.ex. "Aktiva annonser-räknare" mini-stat — civic-utility-style)
- **Spec-edit:** Nej (max-width-edit i scoped class).
- **Berör:** `web/jobbpilot-web/src/app/globals.css:900-983`

### MEDEL-5 — Auth-pages (logga-in, registrera) lägger 1400px+ tom yta
- **Source:** `logga-in__light__fold`, `logga-in__dark__fold`, `registrera__light__fold`
- **Symtom:** `.jp-auth-card max-width: 440px` centrerad i 1920px = ~740px tom yta på varje sida.
- **Projektor-impact:** Mild — civic-utility-konvention (Digg/Skatteverket gör samma), men på projektor ser det dramatiskt ut.
- **Föreslagen fix (multi-approach — CTO-rond, men Klas-direktiv låter detta vara om civic-paritet är viktigare):**
  - **A:** Lämna som är (civic-paritet med Digg/Skatteverket)
  - **B:** Lägg in en subtil dekorativ vänsterspalt (illustration eller civic-trust-text "Sluten beta — vi släpper in användare när vi har kapacitet") som balanserar
  - **C:** Mid-page placering med kort dekoration ovanför (inte hero, bara en monogram-rad)
- **Spec-edit:** Nej.

### MEDEL-6 — `/installningar` ojämnt spacing vid 1920
- **Source:** `installningar__light__fold`, `installningar__dark__fold`
- **Symtom:** 2-col layout med Personuppgifter vänster + Visning/Aviseringar höger. Personuppgifter-kortet är ~660px brett, Visning ~680px brett, Aviseringar pressad nedanför, Sekretess och data längre ner. Vid 1920 luftigt, men inte balanserat.
- **Projektor-impact:** Låg — funktionellt.
- **Föreslagen fix:** Antingen 3-col grid (Personuppgifter | Visning + Aviseringar | Sekretess och data) eller 1-col fram till 1280 (förenklar).
- **Spec-edit:** Nej.

### MEDEL-7 — `.jp-land-top__stat__label` 10.5px under DESIGN.md §4 mono-caps-golv 11.5px
- **Source:** `landing__*__fold` (line 30px topbar), `globals.css:2574`
- **Symtom:** "AKTIVA ANNONSER" + "NYA IDAG" caps-labels i topbar är 10.5px (under 11.5px-golvet i ADR 0038/DESIGN.md §4 — *"mono caps labels = 11.5px / 500 / letter-spacing 0.08–0.16em UPPERCASE"*).
- **Projektor-impact:** Liten — i topbar, perifert. Men under spec.
- **Föreslagen fix:** Bumpa till 11.5px (1-rads-edit i globals.css). Matchar spec.
- **Spec-edit:** Nej (DESIGN.md §4 säger 11.5 = golv, så bump = möter spec, ej spec-edit).
- **Berör:** `web/jobbpilot-web/src/app/globals.css:2574`

### MEDEL-8 — Empty-state-inkonsistens (`/sokningar` solid border, `/sparade` dashed border)
- **Source:** `sokningar__dark__fold` (solid border-kort), `sparade__light__fold` (dashed border-kort)
- **Symtom:** Två civic-utility-empty-state-kort med olika visual treatment. Ingen DESIGN.md-regel som dikterar valet → drift.
- **Projektor-impact:** Låg — visuellt brus.
- **Föreslagen fix:** Välj en konvention (förslag: **dashed** är mer 1177/Digg, signalerar "tom container — kan fyllas"). Ändra `.jp-empty-state` i globals.css så båda routes använder samma. ELLER motivera distinktionen i DESIGN.md.
- **Spec-edit:** Nej för CSS-konsistens. Ja om Klas vill kodifiera valet i DESIGN.md.
- **CTO-rond** lämplig för stance-val.

### MEDEL-9 — `/oversikt`-pagehero "I dag"-kort tar 320px höger — bra på 1920, kollapsar < 1280
- **Source:** `oversikt__light__fold`, `oversikt__dark__fold`
- **Symtom:** `.jp-oversikt__today width: 320px` (`globals.css:3066`) sitter top-right i navy-pagehero. På 1920 är balansen bra. Det är en flex-1+320px-layout — på smalare viewport stackar det (flex-wrap).
- **Projektor-impact:** Ingen vid 1920. Observation only.

---

### LÅG-10 — Emoji "🚛" i Platsbanken-annons-titel
- **Source:** `jobb__light__fold` (Distributionschaufförer sökes 🚛), `jobb__dark__fold`
- **Symtom:** Emoji i annonstitel. DESIGN.md §10.3 "Aldrig emoji" gäller JobbPilot-copy — annonsdata från Platsbanken är extern. **Inte en violation**, men ser oprofessionellt ut på projektor.
- **Föreslagen fix:** Acceptera (extern data — kan inte sanera utan att klippa innehåll och bryta GDPR-data-integritet) ELLER lägg server-side regex-strip på annonstitlar (skala-relaterat).
- **Spec-edit:** Nej.
- **Föreslagen:** **Acceptera (observation).**

### LÅG-11 — Container max-width 1200px = 360px gutter vid 1920
- Per ADR 0052 v3-spec. Observation.

### LÅG-12 — `/oversikt`-kicker "INLOGGAD SOM VISUAL VERIFY DEV TEST"
- Det är dev-test-kontots display-name. På Klas riktiga konto blir det "INLOGGAD SOM KLAS OLSSON" (eller motsvarande). Inget audit-fynd — bara orientering. Klas är medveten.

---

## 4. Issue-tabell (Klas-triage)

Markera **GO** (fixa innan demo) / **SKIP** (efter demo / aldrig) / **Q** (diskutera) per rad.

| Sev | # | Titel | Spec-edit? | CTO-rond? | Klas-GO/SKIP/Q |
|-----|---|-------|------------|-----------|----------------|
| Hög | 1 | Dark "Anmäl till väntelista" osynlig på landing hero | Nej | Ja (multi-approach) | ☐ |
| Hög | 2 | Vit `.jp-header` på dark body — synlig söm | Tveksamt | Ja (A/B/C) | ☐ |
| Hög | 3 | `--jp-border-structural` dark `#44598A` < amendment 3:1 | **Ja** | Ja | ☐ |
| Med | 4 | `/jobb` hero 600px dead-zone höger | Nej | Ja (A/B/C) | ☐ |
| Med | 5 | Auth-pages tom yta vid 1920 | Nej | Mild (eller skip) | ☐ |
| Med | 6 | `/installningar` ojämnt spacing | Nej | Liten | ☐ |
| Med | 7 | Topbar stat-label 10.5px < spec 11.5px | Nej | Liten | ☐ |
| Med | 8 | Empty-state-inkonsistens (solid vs dashed) | Nej | Ja (stance-val) | ☐ |
| Med | 9 | `/oversikt` I-dag-kort layout | Nej | — | Observation |
| Låg | 10 | Platsbanken-emoji | Nej | — | Acceptera |
| Låg | 11 | Container 1200px gutter | Nej | — | Per ADR 0052 |
| Låg | 12 | Dev-test-namn i kicker | Nej | — | Orientering |

---

## 5. Förslag på minimal demo-säkring (om Klas vill köra med så små fixar som möjligt)

Om Klas väljer ljust läge för demo (rekommenderat):
- **Inga av Hög 1–3 är blockers i ljust läge.** Hög 1+2 är dark-specifika, Hög 3 mest synlig i dark.
- **Medel 4** (jobb-hero dead-zone) är synlig i ljust läge också. CTO-rond + B-variant (flytta chips ovanför söket) är 30–60 min jobb.
- **Medel 7** (stat-label 11.5px) är 1-rads-edit i globals.css → in-block.

Om Klas väljer dark mode för demo:
- **Hög 1+2+3 kritiska.** Kräver CTO-rond + minst 2 commits + design-reviewer-render-veto innan push.
- Inte realistiskt att lösa alla tre + verifiera + få review på en kväll utan att forcera scope.

**CC:s rekommendation:** Demo i ljust läge. Fixa Medel 4 + Medel 7 in-block ikväll (1–2 commits). Skjut Hög 1–3 till separat session efter demo med ordentlig CTO-rond.

---

## 6. Discipline-trail

- Reused existing visual-verify-infrastruktur (`scripts/visual-verify.ts`) som mall för `scripts/audit-capture.ts`. Ingen ny npm-dep introducerad.
- Inga DESIGN.md/CLAUDE.md/BUILD.md-edits utförda under audit.
- Inga commits gjorda under audit.
- Inga TDs lyfta — alla fynd ovan är inom nuvarande fas och CLAUDE.md §9.6 säger "fixa in-block" som default. Fynd som kräver spec-edit STOPPas på Klas, inte lyfts som TD.
- Dev-creds lästes via `source "$USERPROFILE/.jobbpilot/dev-test-creds.env"` — inga creds skrivna till disk eller chat.
- Audit-capture-skriptet (`scripts/audit-capture.ts`) committas EJ utan separat GO. Det är ett engångs-audit-verktyg.

---

**Slut på audit-rapport.** Väntar Klas-triage per rad i tabellen i §4 innan Fas 2 (targeted fixes).
