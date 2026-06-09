# JobbPilot v3 — Handover till Claude Code

> **Status:** Beslutat av produktägaren (Klas). Designspec har **veto** över befintliga ADRs och CC-default-preferenser.
> **Källfiler:** `JobbPilot v3.html` + `jobbpilot-v3.css` + `src-v3/*.jsx` (klickbar prototyp i Claude Design).
> **Målbilder:** se `handover/01-…11-*.png` (refereras inline nedan).
> **Uppdrag:** Refactor av live-frontend (`web/jobbpilot-web/`) så att den matchar v3-prototypen. **Uppdatera DESIGN.md** med alla regler nedan. Bryt mot dessa endast efter explicit Klas-godkännande.

---

## Målbilder

Varje skärmdump är slutgiltig — pixel-nära dessa är acceptanskriteriet.

| # | Vy | Tema | Fil |
|---|----|------|-----|
| 01 | Landing | Light | `handover/01-landing-light.png` |
| 02 | Landing | Dark | `handover/02-landing-dark.png` |
| 03 | /jobb (sökresultat) | Light | `handover/03-jobb-light.png` |
| 04 | /jobb (sökresultat) | Dark | `handover/04-jobb-dark.png` |
| 05 | /jobb — Ort-popover öppen | Light | `handover/05-filter-popover.png` |
| 06 | /ansokningar | Light | `handover/06-ansokningar-light.png` |
| 07 | /ansokningar | Dark | `handover/07-ansokningar-dark.png` |
| 08 | /ansokningar — modal öppen | Light | `handover/08-application-modal.png` |
| 09 | /cv | Light | `handover/09-cv-light.png` |
| 10 | /installningar — Visning + Aviseringar | Dark | `handover/10-installningar-dark.png` |
| 11 | /jobb — jobbmodal öppen | Light | `handover/11-job-modal.png` |

![Landing light](handover/01-landing-light.png)
![Landing dark](handover/02-landing-dark.png)
![Jobb light](handover/03-jobb-light.png)
![Jobb dark](handover/04-jobb-dark.png)
![Ort-popover](handover/05-filter-popover.png)
![Ansökningar light](handover/06-ansokningar-light.png)
![Ansökningar dark](handover/07-ansokningar-dark.png)
![Ansökan modal](handover/08-application-modal.png)
![CV light](handover/09-cv-light.png)
![Inställningar dark](handover/10-installningar-dark.png)
![Jobbmodal](handover/11-job-modal.png)

---

## 0. Veto-regler (icke-förhandlingsbara)

Följande står fast oavsett tidigare ADRs, agent-feedback eller CC-preferenser:

1. **Header-meny i stället för sidebar.** Synliga primärlänkar (Jobb · Mina ansökningar · CV) — *ingen sidebar, ingen burger på desktop*. Drawer från höger på mobil.
2. **Pop-up-modaler ÄR tillåtna.** Jobbdetaljer och ansökningsdetaljer öppnas i modal — inte på egen route. CC ska inte argumentera mot detta. Routning för djuplänkning kan finnas parallellt (`/jobb/:id` förblir möjlig), men UI:t öppnar modal vid klick i listan.
3. **Filter-popovers med Platsbanken-mönster** är obligatoriska. Två-kolumns-layout, ingen Använd/Stäng-knapp, markeringar sparas live, per-kolumn Rensa, "Välj alla X" i höger kolumn.
4. **Civic utility (myndighetston) går före modern SaaS-trend.** Inga AI-typiska tropes (Sparkles-ikoner som primär indikator, gradient-hero, "Så funkar det"-numrerade cirklar, trust-pills, drift-indikatorer som dekoration).
5. **Match-score finns kvar** men aldrig som rund procent-cirkel. Liten siffra/chip i meta-raden eller i modalen.
6. **Header förblir vit i både light och dark mode.** En vit remsa förstör inte dark mode-känslan.
7. **Tema-toggle (Ljust/Mörkt) och språk (SV/EN) hör hemma i Inställningar och landing-footern.** Inte i header.

---

## 1. Designfilosofi (v3 vs v2)

v2 var "civic utility" på papperet men landade i ren GOV.UK-imitation: hairlines, ingen kontrast, för stram. Användartest visade att 55-åriga jobbsökare hade svårt att avgöra var korten började och slutade.

**v3-justering:** behåll civic-tonen men **bumpa kontraster, borders och input-fält**. Modern civic — tänk DigID (Nederländerna) eller Australia.gov.au snarare än GOV.UK från 2014. Platsbanken är den primära referensen för layout-rytm.

| Inte | Utan |
|------|------|
| Hairlines `#E2E8F0` mellan rader | **Synliga borders `#C9D2E0`** runt rader |
| Border-radius 2-4 px överallt | **6 px** på rader/kort, 4 px på inputs |
| Surface = canvas (osynliga kort) | **Surface ≠ canvas** alltid |
| Mono-caps-labels överallt | Mono **endast** för IDs, datum, antal |
| Flat slate hero | **Djup navy hero** (#0A2647) på /jobb och landing |
| Drop-shadows | Borders och bg-skift — undantag: popover/modal får skugga |

---

## 2. Färgsystem

### 2.1 Light mode tokens

```css
/* Brand — djup navy */
--jp-navy-900: #08213F;
--jp-navy-800: #0A2647;   /* primärknapp, hero bg */
--jp-navy-700: #133F73;   /* länkar, titlar */
--jp-navy-600: #1B5396;
--jp-navy-500: #2E6CC2;
--jp-navy-300: #7FA9DF;
--jp-navy-100: #D6E3F4;
--jp-navy-50:  #EAF1FA;

/* Surfaces */
--jp-surface:    #FFFFFF;   /* kort, popovers, modal */
--jp-surface-2:  #F4F6FA;   /* page bg under canvas */
--jp-surface-3:  #E8EDF4;   /* hover, rader */
--jp-canvas:     #F4F6FA;   /* sidans baslager */

/* Text */
--jp-ink-1: #0C1A2E;   /* primary — h1, brödtext */
--jp-ink-2: #455366;   /* secondary — meta */
--jp-ink-3: #7C8AA0;   /* tertiary — disabled, mono labels */
--jp-ink-inverse: #FFFFFF;

/* Borders */
--jp-border:        #C9D2E0;   /* synliga rad-borders */
--jp-border-soft:   #E3E8F0;   /* sub-separators inom rad */
--jp-border-strong: #97A4B8;
--jp-border-input:  #94A3B8;   /* 1.5 px på alla input-fält */

/* Status */
--jp-success:    #16793B;  --jp-success-bg: #DFF3E5;
--jp-warning:    #B4540B;  --jp-warning-bg: #FCE9D1;
--jp-danger:     #BE1B1B;  --jp-danger-bg:  #FBE0E0;
--jp-info:       #1B5396;  --jp-info-bg:    #DEE9F8;

/* Accenter */
--jp-leaf-600: #2C8A3F;    /* "Ny"-flagga, "Utforska som gäst" CTA */
--jp-leaf-50:  #DFF3E5;
```

### 2.2 Dark mode tokens

```css
[data-theme="dark"] {
  --jp-surface:    #1B2B47;
  --jp-surface-2:  #142136;
  --jp-surface-3:  #283C5E;
  --jp-canvas:     #0B1525;        /* mörk navy-grå, INTE svart */

  --jp-ink-1: #F4F7FC;
  --jp-ink-2: #C2CFE2;
  --jp-ink-3: #8DA0BD;

  --jp-border:        #44598A;     /* synliga, inte hairlines */
  --jp-border-soft:   #2C3F65;
  --jp-border-input:  #6F86A8;

  --jp-navy-700: #4F8AD0;          /* länkar — ljusare i dark */
  --jp-navy-50:  #1F3866;

  /* Statusfärger har ljusare ramp så de syns mot mörk canvas */
  --jp-success: #5DD894;  --jp-success-bg: #143E29;
  --jp-warning: #FBC267;  --jp-warning-bg: #3F2A0B;
  --jp-danger:  #FB8989;  --jp-danger-bg:  #3F1419;
  --jp-info:    #8FBEEF;  --jp-info-bg:    #1B3358;
}
```

### 2.3 Kontrast-regler (icke-förhandlingsbara)

- WCAG AA är **golvet**, inte målet
- Primärknapp `navy-800` på vit = 14:1 ✅ — och **vit text på navy-800** i dark mode (aldrig invertera till "ljusblå knapp med mörk text")
- Brödtext mot canvas ≥ 12:1 i båda lägena
- Borders är `var(--jp-border)`, ALDRIG `var(--jp-border-soft)` på primära rad-/kort-kanter
- **Hero-input fältet är ALLTID vit bg med mörk text** i båda lägena — hero äger sin egen färgvärld

### 2.4 Tema-/färgöverrides (scoped)

Vissa ytor behåller light-mode-färger även i dark:
- **Header** (`.jp-header` / `.jp-land-top`) — token-override scopad till elementet
- **Auth-kort på landing** (`.jp-land-auth`) — samma trick

Detta är **medvetet**, inte en bugg.

---

## 3. Typografi

| Roll | Familj | Storlek | Vikt | Användning |
|------|--------|---------|------|------------|
| H1 page title | Hanken Grotesk | 32 px | **700** | "Mina ansökningar", "Sök bland aktiva annonser" |
| H1 hero (landing) | Hanken Grotesk | clamp(40px, 5vw, 56px) | **700** | Hero-rubrik |
| H1 hero (/jobb) | Hanken Grotesk | 40 px | **700** | "Sök bland aktiva annonser" |
| H2 section | Hanken Grotesk | 22 px | **700** | "Funktioner" |
| H3 card | Hanken Grotesk | 18 px | **700** | Kort-titlar |
| Job/app titel | Hanken Grotesk | 18 px | 600 (light) / **700** (dark) | Jobbrubrik på rad |
| Body | Hanken Grotesk | 16 px | 400 | Brödtext |
| Lede | Hanken Grotesk | 17-18 px | 400 | Under H1 |
| Body-sm | Hanken Grotesk | 14 px | 400 | Meta-text |
| Caption | Hanken Grotesk | 13 px | 400 | Hint, fotnoter |
| Mono | JetBrains Mono | 13 px | 500-700 | **endast IDs, datum, antal** |
| Mono caps | JetBrains Mono | 11-12 px | 600 | UPPERCASE labels (`UPPDATERAD · MAJ 2026`) |

**Regler:**
- Aldrig system-fonts som primär
- Aldrig Inter, Roboto, Arial
- Aldrig italic för emfas i body
- Mono **endast** för data — aldrig rubriker eller knapptext
- Mono caps **endast** för kolumnhuvuden och kickers — inte överallt
- Tracking `-0.005em` globalt (optisk täthet)
- I dark mode: titlar i jobblistan och Mina ansökningar går från weight 600 → **700** automatiskt (kompenserar färgskift)

---

## 4. Spacing och radius

- 4 px-baserad skala: 4, 8, 12, 16, 20, 24, 28, 32, 48, 64, 80
- **Radius:**
  - sm 4 px — inputs, badges
  - md 6 px — kort, rader, knappar
  - lg 8 px — modaler, större paneler
  - xl 12 px — bara hero-element
  - pill 9999 — status-pills, hjärt-/spara-knapp
- **Inga andra radier.** Inte 10, inte 14, inte 16
- **Container** max-width 1200 px, padding inline 32 px (desktop), 20 px (mobil)
- **Page** padding 40 px topp / 96 px botten

---

## 5. Komponentregler

### 5.1 Buttons

| Variant | Bg | Text | Border | Höjd |
|---------|----|----|--------|------|
| `jp-btn--primary` | `--jp-navy-800` | `#FFFFFF` | `--jp-navy-800` | 44 px |
| `jp-btn--secondary` | `--jp-surface` | `--jp-ink-1` | `--jp-border-input` | 44 px |
| `jp-btn--ghost` | transparent | `--jp-ink-2` | transparent | 44 px |
| `jp-btn--danger` | `--jp-danger` | `#FFFFFF` | `--jp-danger` | 44 px |
| Varianter | `--lg` 52 px, `--sm` 36 px | | | |

**I dark mode:** primary förblir `navy-800` med vit text (inverteras INTE). Aldrig "ljusblå knapp med mörk text".

**Hero-kontext:** vit knapp med navy text för primär CTA (Sök, Skapa konto). Grön leaf-600 endast för "Utforska som gäst".

### 5.2 Inputs

- Höjd 48 px (sm 40 px)
- Border 1.5 px `--jp-border-input`
- Radius 6 px
- Font-size 16 px (förhindrar zoom på iOS)
- Focus: `border-color: var(--jp-navy-700); box-shadow: 0 0 0 3px rgba(46,108,194,0.20);`
- **I dark mode: ljus bg `#F0F4FB` + mörk text** (icke-förhandlingsbart krav)

### 5.3 Job row / App row (identiskt mönster)

```
.jp-job, .jp-app {
  background: var(--jp-surface);
  border: 1px solid var(--jp-border);
  border-radius: var(--jp-r-md);    /* 6 px */
  padding: 18px 22px;
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 18px;
}
.jp-job:hover { border-color: var(--jp-navy-700); }
.jp-jobs, .jp-applist { gap: 8 px; }  /* gap mellan rader */
```

**Båda måste se identiska ut.** Avvik aldrig.

### 5.4 Filter-popover (Platsbanken-mönster — OBLIGATORISKT)

- Två kolumner: vänster = kategorier (län / yrkesområden), höger = val (kommuner / yrken)
- Aktiv vänsterrad: fylld leaf-grön bg + vit text + chevron höger
- Höger kolumn första rad: **"Välj alla kommuner"** checkbox (eller "Välj alla yrken")
- Per-kolumn header: titel + **Rensa**-länk (visas endast vid val i den kolumnen)
- **Ingen footer**, ingen "Använd" eller "Stäng"-knapp
- Markeringar sparas direkt vid klick
- ESC eller klick utanför stänger
- Bredd 580 px

Samma mönster gäller alla 3 popovers (Ort, Yrke, Filter).

### 5.5 Hero (/jobb och landing)

- **Bg: flat navy `#0A2647`**, ingen gradient
- Input: vit bg, mörk text, 56 px höjd
- Sökknapp: vit bg, navy text, höger om input (samma rad)
- Pill-knappar Ort/Yrke/Filter: vit bg, navy text, 40 px höjd, count som mono `(3)` efter labeln
- Hero-chips uppe till höger (Senaste sökningar / Sparade annonser): vit bg, navy text, mono count

### 5.6 Modal

- Bredd 760 px max
- Max-höjd 86vh
- Radius 8 px
- Skugga: `0 30px 80px rgba(8, 23, 48, 0.35)`
- Scrim: `rgba(8, 23, 48, 0.55)`
- Animation: fade 140 ms + rise 200 ms
- Foot: actions höger-justerade (`Stäng` ghost · `Spara annons` secondary · `Har ansökt` primary)
- ESC stänger, klick på scrim stänger

### 5.7 Status pills

- Höjd 26 px, padding 0 11px, radius pill
- Border 1 px i status-färg (för kontrast mot dark)
- Bg = `--jp-{status}-bg`, color = `--jp-{status}`
- Statusprick 8 × 8 px `currentColor`

### 5.8 Match-score

- **Aldrig stor procent-cirkel.** Inte 56 × 56 ring med stor siffra.
- I jobblistans rader: visas **inte** (för subtilt för att tillföra värde)
- I jobbmodalen: mono `92% match` + förklarande text under (3 nivåer: stark / delvis / svag)
- Färg: success över 75, navy 40-74, ink-3 under 40

---

## 6. Navigation

### 6.1 Desktop

- Header 68 px hög, vit bg, 1 px border-bottom
- Vänster: brand (J-monogram + ord) — länk till landing
- Mitten: nav-länkar Jobb · Mina ansökningar · CV
- Höger: notiser-bell + avatar (öppnar user-menu)

### 6.2 Mobil (< 900 px)

- Nav-länkarna döljs, burger-ikon syns
- Klick öppnar **drawer från höger** med samma länkar + Inställningar
- Drawer-bredd `min(340px, 88vw)`

### 6.3 User-menu (klick på avatar)

- Inställningar
- Senaste sökningar
- Mina CV
- — separator —
- Logga ut

### 6.4 Landing-header

- Endast brand + live-stats (`45 580 aktiva annonser` · `312 nya idag`)
- **Inga inloggningsknappar** här (de finns i auth-kortet)
- **Inga theme/lang-toggles** här (flyttade till landing-footer + Inställningar)

---

## 7. Sidor

### 7.1 Landing (route `/`)

1. Header (logo + stats)
2. Hero — navy bg, kortrubrik ("Verktyg för svenska jobbsökare"), 1-meningslede, två CTAs (vit "Skapa konto" + grön "Utforska som gäst"). Auth-kort höger.
3. Funktioner-sektion (mono-key + text)
4. Footer — länkar + tema-toggle + SV/EN

**Bort:** "Så funkar det"-sektion, CTA-banner-card, version-kicker, trust-pill, drift-indikator.

### 7.2 Jobb (route `/jobb`)

1. Hero (navy) — chips uppe höger, h1 "Sök bland aktiva annonser", input + Sök, pill-knappar Ort/Yrke/Filter
2. Aktiva filter-chips (om val finns)
3. Resultatrubrik: `N träffar` · sorteringsdropdown höger
4. Jobblista — rader med vit bg, border, hover navy
5. Klick → modal med full annons

### 7.3 Mina ansökningar (route `/ansokningar`)

1. Page-titel + "Ny ansökan"-knapp
2. Statusbar med antal per status (segmenterad)
3. Sektioner per status (endast om count > 0)
4. Rader matchar jobblistans struktur (vit bg, border, gap)
5. Klick → modal med statusbyte, tidslinje, anteckningar

### 7.4 CV (route `/cv`)

1. Page-titel + "Nytt CV"-knapp
2. Grid 320 px min, 1fr fill
3. Varje CV-kort: titel + roll, skill-chips, mono-meta, actions (Redigera / Förhandsgranska)
4. Banner längst ned: navy-50 bg + Edit-ikon (**inte Sparkles**) + "Öppna"-CTA

### 7.5 Senaste sökningar (route `/sokningar`)

- Listas som rader i samma stil som jobblistan
- Klick på "Kör igen" → tar till /jobb med samma sökparametrar

### 7.6 Inställningar (route `/installningar` — tidigare `/konto`)

- Grid 1fr 1fr
- Vänster: Personuppgifter
- Höger: Visning (Tema-segment + Språk-segment), Aviseringar, Sekretess och data, Logga ut

---

## 8. Vad CC SKA göra

1. **Läsa hela detta dokument** + öppna prototypen `JobbPilot v3.html` i Claude Design-projektet och studera den
2. **Uppdatera `DESIGN.md`** så den speglar v3 i sin helhet (radera inte tidigare ADRs men låt detta dokument vara giltigt över dem)
3. **Skapa nya ADRs** för:
   - v2 → v3 design-system-byte
   - Modal-baserad detaljvy för jobb och ansökningar (supersedes route-based detail om relevant ADR finns)
   - Filter-popover-mönster (Platsbanken-stil, ingen Använd-knapp)
   - Tema-toggle bort från header (landing-footer + Inställningar)
   - Header-meny i stället för sidebar (supersedes shell variant B om sidebar var beslutat)
4. **Refaktorera frontend** (`web/jobbpilot-web/src/`) lager för lager:
   - Tokens i `globals.css` ersätts med v3-paletten
   - `app-shell.tsx` skrivs om till header-shell utan sidebar
   - `JobAdCard` förenklas till v3-radstilen
   - `JobAdFilters` byggs om till Platsbanken-popovers
   - `ApplicationRow` matchar JobRow visuellt
   - Detalj-rutterna behålls (för djuplänkning + SEO) men listorna öppnar modal som default; route-detalj-sidan kan vara samma komponent renderad fullskärm
5. **Bekräfta varje större refaktor med Klas innan commit.** Plan-design först. STOPP-disciplin gäller.

## 9. Vad CC INTE ska göra

- **Inte argumentera mot modaler** med hänvisning till "anti-pattern" eller WCAG-bekymmer. Modaler är beslutade; gör dem tillgängliga i stället.
- **Inte återinföra sidebar** under något skäl
- **Inte ändra färger** utan att uppdatera detta dokument först
- **Inte tillåta att jp-job och jp-app drifter isär** visuellt — de ska se identiska ut
- **Inte introducera AI-typiska tropes** (Sparkles-knappar som primär CTA, "Generera med AI"-banderoller, gradient-bg, runda match-cirklar)
- **Inte lägga theme/lang i header** — de hör hemma i Inställningar och landing-footer
- **Inte lägga till spara-sökning-knappen** i /jobb — senaste sökningar fångas automatiskt

---

## 10. Acceptanskriterier

Refaktorn är klar när:

- [ ] Alla 5 routes (`/`, `/jobb`, `/ansokningar`, `/cv`, `/installningar`) ser pixel-nära v3-prototypen
- [ ] Dark mode validerar samma kontrastregler som light
- [ ] Header har inga theme/lang/login-knappar (utom user-menu)
- [ ] Filter-popovers har ingen Använd-knapp
- [ ] Jobbmodal öppnas vid klick på rad, ESC stänger
- [ ] Ansökan-modal öppnas vid klick på rad, ESC stänger
- [ ] DESIGN.md uppdaterad med alla §2–§7 ovan inline (inte bara länk)
- [ ] Minst 3 ADRs skapade för paradigm-skiften (modal, popover, header)
- [ ] design-reviewer-agenten godkänner sliderna mot v3-prototypen

---

## 11. Referensfiler i Design-projektet

| Fil | Syfte |
|-----|-------|
| `JobbPilot v3.html` | Mountpoint för klickbar prototyp |
| `jobbpilot-v3.css` | Komplett designsystem (kopiera till `globals.css` som utgångspunkt) |
| `src-v3/data.jsx` | Mock-data — speglar samma DTO-struktur som live |
| `src-v3/icons.jsx` | Lucide-stroke ikoner (samma set som `lucide-react` i live-koden) |
| `src-v3/shell.jsx` | Header + drawer + theme/lang-toggles |
| `src-v3/jobb.jsx` | Jobblista + modal + filter-popovers |
| `src-v3/pages.jsx` | Mina ansökningar (+ modal), CV, Inställningar, Sokningar |
| `src-v3/landing.jsx` | Landing-sida med auth-kort |
| `src-v3/app.jsx` | Lightweight router för prototypen |

CC har läsåtkomst till hela detta projekt. Bekräfta att alla filer är öppnade och förstådda innan kod skrivs i `web/jobbpilot-web/`.

---

**Klas Olsson · produktägare · 2026-05-18**
