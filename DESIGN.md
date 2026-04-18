# DESIGN.md — JobbPilot design system

> **Syfte:** definiera exakta design-tokens, komponentbeteenden, och copy-riktlinjer så att shadcn/ui-basen kan omkonfigureras till civic-utility-estetiken och att all kod följer samma visuella språk.
>
> **Huvudspec:** [`BUILD.md`](./BUILD.md)
> **Coding conventions:** [`CLAUDE.md`](./CLAUDE.md)

---

## 1. Design-filosofi

### 1.1 Grundprincip

JobbPilot är ett verktyg för stressade jobbsökare. UI:t ska signalera **tillit och pålitlighet**, inte imponera eller underhålla. Målet är att en 55-årig processoperatör i Alingsås som söker sitt nästa jobb ska känna att appen är byggd för att fungera, inte för att sälja.

Referenser som ska kännas i allt vi bygger:
- **GOV.UK Design System** — typografisk hierarki, content-first, minimal dekoration
- **Digg / Sveriges designsystem** — svensk myndighetsprecedent
- **1177 Vårdguiden** — trygg, läsbar, accessible
- **Stripe Dashboard** — datatäthet utan kaos
- **Mercury Bank** — utility över branding

Referenser som ska **inte** kännas:
- Vercel / Linear / Arc — för trendigt, för mycket "vibe"
- Notion — för lekfullt
- Default shadcn/ui ur-lådan — standard-AI-look

### 1.2 Do / don't (snabbkort)

| ✅ Ja | ❌ Nej |
|-------|--------|
| Ljus bakgrund default | Dark mode default |
| Myndighetsblå primärfärg | Neon, lila, cyan-accenter |
| Rak svensk copy | Emojis, utropstecken, "Let's go!" |
| Tabeller och listor | Kort-layouter överallt |
| `border-radius: 4px` | 16px+ rundade hörn |
| Muted statusfärger | Glow, drop shadow, glasmorfism |
| Breadcrumbs + hierarki | Flata sidor utan kontext |
| Systemfont/Hanken Grotesk | Display-fonter, scripts |
| Content-first sidor | Hero-sektioner, vibey microcopy |
| Kvantifierad info | Vag "positiv" feedback |

---

## 2. Färgsystem

### 2.1 Rationale

Paletten är medvetet begränsad. Civic-produkter bygger tillit genom konsekvens — fler färger skapar kognitiv belastning. Vi håller oss till en primärblå, en neutralgrå-skala, och funktionella statusfärger.

### 2.2 Tokens (exakta hex)

Alla tokens definieras som CSS custom properties i `globals.css` och mappas till Tailwind 4 via `@theme`-block.

```css
:root {
  /* Ytor */
  --surface-primary:    #FFFFFF;   /* bakgrund huvudinnehåll */
  --surface-secondary:  #F7F7F5;   /* off-white, sekundära paneler */
  --surface-tertiary:   #EDECE7;   /* hover states, tables alt rows */
  --surface-inverse:    #1A1A1A;   /* dark highlights (icke-ramverksbunden) */

  /* Text */
  --text-primary:       #1A1A1A;   /* body */
  --text-secondary:     #5A5A5A;   /* hjälptexter, timestamps */
  --text-tertiary:      #8A8A85;   /* disabled, placeholder */
  --text-inverse:       #FFFFFF;

  /* Primärblå (myndighetsblå) */
  --brand-50:           #EAF2FB;
  --brand-100:          #C8DDF1;
  --brand-300:          #6BA1DC;
  --brand-500:          #1F6EB8;
  --brand-600:          #0B5CAD;   /* PRIMARY — använd för länkar, primärknappar, fokusring */
  --brand-700:          #094B8C;
  --brand-900:          #062F57;

  /* Status: grön */
  --success-50:         #E8F3EC;
  --success-600:        #0F7A2E;
  --success-700:        #0B5E24;

  /* Status: gul/amber (varning) */
  --warning-50:         #FAF2DE;
  --warning-600:        #946200;
  --warning-700:        #734D00;

  /* Status: röd (fel, avslag) */
  --danger-50:          #FBEBEB;
  --danger-600:         #B42121;
  --danger-700:         #8C1919;

  /* Status: neutral (info) */
  --info-50:            #EEF1F5;
  --info-600:           #4A5A7A;
  --info-700:           #384560;

  /* Borders */
  --border-default:     #D8D6D0;   /* alla dividers, input-border */
  --border-strong:      #B8B6B0;   /* hover, focus secondary */
  --border-brand:       var(--brand-600);

  /* Focus ring */
  --focus-ring:         var(--brand-600);
  --focus-ring-offset:  #FFFFFF;

  /* Shadows (minimalistiska) */
  --shadow-sm:          0 1px 2px rgba(0,0,0,0.04);
  --shadow-md:          0 2px 4px rgba(0,0,0,0.06);
  /* INGA större skuggor används. Djup skapas via border, inte shadow. */

  /* Radius */
  --radius-sm:          2px;      /* inputs, small chips */
  --radius-md:          4px;      /* knappar, cards, panels — DEFAULT */
  --radius-lg:          6px;      /* större ytor */
  --radius-pill:        999px;    /* ENDAST badges och pills */
}
```

### 2.3 Kontrastkrav

Alla text-bakgrund-par måste nå **WCAG AA**:
- Body text (≥14px): 4.5:1
- Stora rubriker (≥18.66px bold eller 24px regular): 3:1
- UI-komponenter och ikoner: 3:1 mot bakgrund

`--brand-600` (#0B5CAD) mot `--surface-primary` (#FFFFFF) = 6.1:1 — AA passerat.
`--text-primary` (#1A1A1A) mot `--surface-primary` = 17:1 — AAA passerat.
`--text-secondary` (#5A5A5A) mot `--surface-primary` = 7.4:1 — AA passerat.

Verifiera vid varje ny färgkombination med https://webaim.org/resources/contrastchecker.

### 2.4 Dark mode (sent i roadmap)

Dark mode **implementeras inte i v1**. Om det läggs till senare (v2+):
- Bakgrund: `#0F1014`, yta: `#181920`
- Text primär: `#F5F5F2`, sekundär: `#B8B6B0`
- Brand förblir samma `#0B5CAD` men används sparsamt
- Måste ha samma kontrast-krav som light mode

Däremot respekterar vi `prefers-color-scheme` från dag 1 genom att vägra auto-tema (appen är alltid light mode i v1) och informerar användare som vill ha dark mode att det är på roadmapen.

---

## 3. Typografi

### 3.1 Typsnitt

**Primär:** Hanken Grotesk (Google Fonts, gratis, open source)
- Viktklasser som används: 400 (regular), 500 (medium), 600 (semibold)
- Italic endast i citat / referenser — aldrig för emfas i body

**Fallback-kedja:**
```css
font-family: 'Hanken Grotesk', -apple-system, BlinkMacSystemFont,
             'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
```

**Monospace** (tabelldata, IDs, kodsnuttar): `'JetBrains Mono', 'SF Mono', Menlo, Consolas, monospace`

**Förladdning:**
- Hanken Grotesk 400 + 500 laddas via `next/font/google` i `app/layout.tsx`
- Subset: `latin` + `latin-ext` (täcker åäö)
- `display: 'swap'`

### 3.2 Skala

| Roll | Storlek | Line-height | Weight | Användning |
|------|---------|-------------|--------|-----------|
| `display` | 36px / 44px | 1.15 | 500 | Landing page-rubriker, aldrig i app-UI |
| `h1` | 28px / 36px | 1.25 | 500 | Sidhuvud per vy |
| `h2` | 22px / 28px | 1.3 | 500 | Sektionsrubriker |
| `h3` | 18px / 24px | 1.35 | 500 | Panel/kort-rubriker |
| `h4` | 16px / 22px | 1.4 | 500 | Mindre sektionsheaders |
| `body-lg` | 16px / 24px | 1.5 | 400 | Default body i stora kolumner |
| `body` | 14px / 22px | 1.55 | 400 | **Default överallt i app-UI** |
| `body-sm` | 13px / 20px | 1.5 | 400 | Sekundär info, timestamps |
| `caption` | 12px / 16px | 1.4 | 400 | Microcopy, metadata |
| `label` | 13px / 18px | 500 | 500 | Form labels |
| `mono` | 13px / 18px | 1.45 | 400 | IDs, org-nummer, tabell-värden |

### 3.3 Typografiska principer

- **En riktning**: all text flödar uppifrån och ned, ingen centrerad text i app-UI (centrerat är OK i landing)
- **Tight med rubrik-text**: `margin-bottom` under rubriker är 8–12px, inte 24px (civic-layout är tight)
- **Generöst med brödtext**: paragraf-`margin-bottom: 16px`, `max-width: 70ch` för läsbarhet
- **Aldrig all caps** — inte ens i labels eller badges
- **Inga letter-spacing-justeringar** (default `normal`)

### 3.4 Tailwind-mappning

```ts
// tailwind.config.ts fontSize
{
  'display': ['36px', { lineHeight: '44px', fontWeight: '500' }],
  'h1': ['28px', { lineHeight: '36px', fontWeight: '500' }],
  'h2': ['22px', { lineHeight: '28px', fontWeight: '500' }],
  'h3': ['18px', { lineHeight: '24px', fontWeight: '500' }],
  'h4': ['16px', { lineHeight: '22px', fontWeight: '500' }],
  'body-lg': ['16px', { lineHeight: '24px' }],
  'body': ['14px', { lineHeight: '22px' }],
  'body-sm': ['13px', { lineHeight: '20px' }],
  'caption': ['12px', { lineHeight: '16px' }],
  'label': ['13px', { lineHeight: '18px', fontWeight: '500' }],
  'mono': ['13px', { lineHeight: '18px' }],
}
```

---

## 4. Spacing och layout

### 4.1 Spacing-skala

4px-baserad. Använd Tailwind-spacing (`p-4` = 16px):

| Token | Pixel | Tailwind | Exempel |
|-------|-------|----------|---------|
| `xs` | 4px | 1 | Ikonmellanrum |
| `sm` | 8px | 2 | Tight padding, chip-inner |
| `md` | 12px | 3 | Button inner padding |
| `base` | 16px | 4 | **Default padding, gap** |
| `lg` | 24px | 6 | Sektions-padding |
| `xl` | 32px | 8 | Mellan major sektioner |
| `2xl` | 48px | 12 | Page-gutter uppe |
| `3xl` | 64px | 16 | Stora skikt på landing |

### 4.2 Grid och layout-system

- **App shell**: vänster sidebar 240px (collapsed: 60px), huvudinnehåll flex, max-width på content 1280px
- **Tabeller**: full-width i sin container, aldrig horizontal scroll på desktop (om det krävs, revidera kolumner)
- **Formulär**: max-width 640px, labels ovanför inputs
- **Detaljvyer**: två-kolumn-layout där huvudinnehåll är 66% och sido-panel 33% (eller full bredd på mindre skärmar)

### 4.3 Breakpoints

| Namn | Bredd | Syfte |
|------|-------|-------|
| `sm` | 640px+ | Liten tablet |
| `md` | 768px+ | Tablet |
| `lg` | 1024px+ | Liten desktop |
| `xl` | 1280px+ | Desktop (default) |
| `2xl` | 1536px+ | Stor desktop |

Desktop-first: alla vyer designas för `xl` och sedan anpassas nedåt (v1 behöver bara fungera, inte vara optimerad för mobil).

---

## 5. Komponenter

shadcn/ui är basen. Varje komponent omkonfigureras enligt nedan. Komponenter som inte listas här används som shadcn-default efter token-anpassning.

### 5.1 Button

```tsx
// Variants
variant: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger' | 'link'
size: 'sm' | 'md' | 'lg'
```

**primary** (cta):
- bg `--brand-600`, text `--text-inverse`, hover bg `--brand-700`
- `padding: 8px 16px`, `font-weight: 500`, `font-size: 14px`
- `border-radius: 4px`, `border: 1px solid --brand-600`
- Focus ring: 2px offset `--focus-ring-offset`, 2px `--focus-ring`

**secondary**:
- bg `--surface-secondary`, text `--text-primary`, hover bg `--surface-tertiary`
- Övrigt samma som primary

**outline**:
- bg transparent, text `--text-primary`, border `--border-strong`, hover bg `--surface-secondary`

**ghost**:
- bg transparent, text `--text-primary`, hover bg `--surface-secondary`
- Ingen border

**danger**:
- bg `--danger-600`, text white, hover bg `--danger-700`
- Används för "Radera", "Avsluta konto", etc.

**link**:
- bg transparent, text `--brand-600`, underline on hover
- Används i textlänkar

**Sizes:**
- `sm`: padding 6px 12px, font-size 13px, height 32px
- `md`: padding 8px 16px, font-size 14px, height 36px (default)
- `lg`: padding 10px 20px, font-size 15px, height 44px

**Ikoner i knappar**: Lucide, 16px, marginering 8px mellan ikon och text. Ikon-only knappar är `width: height` (32 / 36 / 44 px).

**Laddning**: ersätt ikon (eller text) med spinner, disable interaction, behåll bredd.

### 5.2 Input / Textarea / Select

- Height 36px (md) / 32px (sm)
- `padding: 0 12px`
- `border: 1px solid --border-default`, focus: `border-color: --brand-600`, `box-shadow: 0 0 0 2px --brand-100`
- `border-radius: 4px`
- `font-size: 14px`, text `--text-primary`
- Placeholder text `--text-tertiary`
- Label ovanför: `font-size: 13px`, `font-weight: 500`, `margin-bottom: 6px`
- Hjälptext under: `font-size: 12px`, `color: --text-secondary`, `margin-top: 4px`
- Felstatus: `border-color: --danger-600`, feltext under i `--danger-700`
- Required-indikator: liten asterisk i `--danger-600` efter label

### 5.3 Card / Panel

- bg `--surface-primary`
- `border: 1px solid --border-default`
- `border-radius: 4px`
- `padding: 24px` (lg paneler) / `16px` (tighta)
- **INGEN skugga** — djup skapas bara av border
- Rubrik inuti: `h3`-stil, `margin-bottom: 16px`

### 5.4 Table

- `border-collapse: collapse`, `width: 100%`
- Head: bg `--surface-secondary`, text `--text-secondary`, `font-size: 12px`, uppercase **NEJ** — använd `font-weight: 500` istället
- Head-padding: `8px 12px`, höjd 36px
- Body-rader: padding `12px`, höjd 44px, border-bottom `1px solid --border-default`
- Hover: bg `--surface-secondary`
- Selected row: bg `--brand-50`, border-left 3px `--brand-600`
- Alternating rows: **nej** (rent civic-utility-mönster, inte zebra)
- Sortable headers: klickbara, med sort-indikator (pil) i `--text-tertiary`
- Dense mode (Ctrl+D): `padding: 6px 12px`, height 32px per rad

### 5.5 Badge / Pill

- `border-radius: 999px` (pill)
- `padding: 2px 8px`, `font-size: 12px`, `font-weight: 500`
- Status-varianter:
  - Draft: bg `--info-50`, text `--info-700`
  - Submitted / Active: bg `--brand-50`, text `--brand-700`
  - Acknowledged: bg `--success-50`, text `--success-700`
  - InterviewScheduled / Interviewing: bg `--warning-50`, text `--warning-700`
  - OfferReceived / Accepted: bg `--success-50`, text `--success-700`
  - Rejected / Ghosted: bg `--danger-50`, text `--danger-700`
  - Withdrawn: bg `--surface-tertiary`, text `--text-secondary`

### 5.6 Navigation / Sidebar

- bg `--surface-secondary`
- width 240px expanded, 60px collapsed (icon-only)
- border-right `1px solid --border-default`
- Nav-items: `padding: 8px 12px`, `font-size: 14px`, radius 4px, aktiv: bg `--brand-50`, text `--brand-700`, border-left 3px `--brand-600`
- Ikoner: 16px Lucide
- Sektioner grupperade med små labels i `--text-tertiary`, `font-size: 11px`, `letter-spacing: 0` (ingen uppercase)

### 5.7 Breadcrumbs

- Font-size 13px, text `--text-secondary`
- Separator: `/` i `--text-tertiary`
- Senaste länken: `--text-primary`, ej underline
- Tidigare länkar: `--brand-600`, underline on hover
- Alltid synlig på sidor djupare än första nivån

### 5.8 Dialog / Modal

- `max-width: 560px` (default) / `720px` (stort) / `400px` (konfirmation)
- `border-radius: 6px`
- Bakgrund: `--surface-primary`
- Overlay: `rgba(26,26,26,0.45)` (mörk halvtransparent)
- Header: `padding: 24px 24px 0`
- Body: `padding: 16px 24px`
- Footer: `padding: 16px 24px`, knappar justified-end, 8px gap
- Stängknapp i övre höger, ghost-variant, 32px

### 5.9 Toast / Notification

- Position: `top-right` eller `bottom-right` (inte center)
- Max-width: 400px
- bg färg enligt status (samma 50/700-mönster)
- Auto-dismiss efter 5 sekunder (success) / 8 sekunder (warning/error) / aldrig (kritiska)
- Inga emoji, ingen animation-bounce — fade + slide-in 150ms

### 5.10 Empty state

- Ikon (32px, `--text-tertiary`) centrerad
- Rubrik h3 i `--text-primary`
- Beskrivning i `--text-secondary`, max 2 rader
- En primär CTA-knapp
- Exempel-copy: "Du har inga sparade sökningar än. Skapa en för att få nya jobb mejlade till dig." — **inte** "Oj, tomt här! 🎯 Dags att börja söka!"

---

## 6. Ikoner

- Bibliotek: **Lucide React** (https://lucide.dev)
- Storlek: 16px (default), 20px (fristående i stora ytor), 24px (navigation), 32px (empty states)
- Stroke-width: 1.5 (Lucide default)
- Färg ärvs från parent (`currentColor`)
- **Inga fyllda ikoner** (filled variants) — bara stroke/outline
- **Inga anpassade illustrationer** i v1 — konsekvent linjekänsla

---

## 7. Kritiska mönster

### 7.1 Formulärvalidering

- **Inline**: visa fel vid blur, inte vid varje keypress
- **Felmeddelande**: konkret och hjälpsam — "Ange en giltig e-postadress" inte "Ogiltigt värde"
- **Submit-knappen**: disabled om form ogiltigt, visar loading-state vid submit
- **Serverfel**: visa i toppen av formuläret som banner, inte som toast

### 7.2 Datatabeller

- Sortering: klickbara rubriker, default-sortering per vy (t.ex. publicerade-datum desc på jobb-lista)
- Paginering: ovanför + under tabellen, "Visar 21–40 av 156"
- Filter: kollapsbart sidopanel eller rad ovanför tabellen
- Rad-click: öppnar detaljvy (om det är en lista av aggregates)
- Bulk-actions: checkbox-kolumn vänster, action-bar syns när minst 1 markerad
- Column resize: **v2**, inte v1

### 7.3 Loading states

- **Skeleton**: för första render av listor/detaljvyer (neutrala grå rektanglar, ingen shimmer-animation)
- **Spinner**: för små inline-tillstånd (knappar, inline saves)
- **Progressbar**: för operationer med känd progress (CV-parsing)
- **Empty spinner stacks**: undvik — använd skeleton

### 7.4 Fel-hantering

- 4xx från backend → visa som banner eller inline i relevant fält
- 5xx → "Ett fel uppstod. Försök igen om en stund eller kontakta support om problemet kvarstår." med request-ID för support
- Nätverksfel → "Ingen anslutning. Kontrollera din nätverksanslutning."
- Aldrig visa stacktrace för användare

### 7.5 Destruktiva handlingar

- Alltid konfirmation-dialog innan:
  - Radering av resumé, ansökan, konto
  - Frånkoppling av Gmail/Calendar
  - Annullering av pågående AI-operation
- Knapp-text ska vara specifik: "Radera CV" inte bara "Bekräfta"
- Dialog-text ska vara konkret: "Radera Klas-CV-v3? Detta kan inte ångras efter 30 dagar."
- Destruktiv knapp: `danger`-variant

---

## 8. Copy-riktlinjer

### 8.1 Ton

- **Informell men professionell**: "du", aldrig "Du" eller "ni"
- **Direkt**: 10 ord där möjligt, inte 25
- **Konkret**: siffror, datum, namn
- **Opretentiös**: inga ordspråk, inga liknelser, inga "resan mot ditt drömjobb"

### 8.2 Vanliga formuleringar

| Situation | ✅ Ja | ❌ Nej |
|-----------|------|-------|
| Efter registrering | "Välkommen. Nästa steg: ladda upp ditt CV." | "Yay! Välkommen ombord! 🎉 Nu börjar resan!" |
| Ingen data | "Du har inga ansökningar än." | "Oj, det ser tomt ut här! 😅" |
| Success efter submit | "Ansökan skickad 14:32 den 18 april." | "Kör hårt! Vi håller tummarna! 💪" |
| Fel | "Inloggningen misslyckades. Kontrollera e-post och lösenord." | "Hoppsan! Det blev fel. Testa igen!" |
| Loading | "Hämtar jobbannonser…" | "Letar efter ditt drömjobb ✨" |
| AI-genererat | "Utkast genererat. Läs igenom och redigera innan du skickar." | "Your personal cover letter is ready! 🚀" |
| Matchningsscore hög | "89 % matchning mot din profil." | "Den här är till dig! ⭐" |

### 8.3 Svenska detaljer

- Datum i UI: "14 apr 2026" eller "2026-04-14" (aldrig "14/4/26" eller "April 14, 2026")
- Tid: 24-timmars, "14:32"
- Ordinaler: "3 dagar" inte "3 dagar sen" i absolut kontext
- Relativa tider: "3 dagar sen", "om 2 timmar" (via date-fns svensk locale)
- Företagsnamn på original (inte "Volvo AB" om det står "Volvo Cars Sverige AB")
- Kommatecken som decimaler i UI (33 456 kr, inte $33,456)

### 8.4 Microcopy i nyckellägen

**Empty states (exempel):**

- `/jobb` tom: "Inga jobbannonser matchar dina filter. Prova att bredda sökningen eller rensa filter."
- `/ansokningar` tom: "Du har inga aktiva ansökningar. Hitta jobb som passar din profil under Jobb."
- `/cv` tom: "Ladda upp ett CV för att komma igång. Vi stödjer PDF och Word."

**Påminnelser:**

- "Du har inte följt upp med Ericsson sedan 5 april. Skicka ett mejl?"
- "Intervjun med Klarna är i morgon 10:00. Förbered med vårt intervjutips-dokument."

**AI-samtycken:**

- "Denna åtgärd skickar ditt CV till Anthropics API i USA. Läs integritetspolicyn innan du fortsätter."
- "Du använder din egen API-nyckel. Anthropic fakturerar dig direkt."

---

## 9. Tillgänglighet

### 9.1 WCAG 2.1 AA som golv

Alla vyer måste klara:
- Tangentbords-navigation: tabba till alla interaktiva element, synlig fokusring
- Screenreader: meningsfulla etiketter, alt-text, landmarks (`<main>`, `<nav>`, `<aside>`)
- Kontrast: se §2.3
- Zoomning: upp till 200% utan horisontell scroll
- Ingen auto-play video/audio

### 9.2 Fokusring

```css
*:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 2px;
  border-radius: var(--radius-sm);
}
```

**Aldrig** `outline: none` utan ersättning.

### 9.3 Formulär

- `<label>` kopplad med `htmlFor` + `id`
- `aria-describedby` för hjälptext
- `aria-invalid` på felfält
- `aria-required` på obligatoriska

### 9.4 Verktyg

- **axe DevTools** browser extension kör på varje ny sida
- **Lighthouse a11y-score > 95** innan merge
- **NVDA** (Windows) + **VoiceOver** (Mac) manuellt testade för kritiska flöden

---

## 10. Motion

- **Minimalt**. Civic-produkter rör sig inte för att "kännas levande".
- Tillåtna animationer:
  - Fade 150ms (toast in/out, dropdown)
  - Slide 200ms (side panel open/close)
  - Opacity 150ms (hover states)
- **Ingen** bounce, spring, scale på hover, parallax, wiggle
- Respektera `prefers-reduced-motion: reduce` → stäng av alla animationer

---

## 11. Logotyp

Skjuts till designfas i fas 0. Krav:
- SVG
- Fungerar på både ljus och mörk bakgrund
- En-tonig + positiv/negativ-variant
- Monokrom fallback
- Passar civic-ton: geometrisk, stabil, inte lekfull

Föreslagna riktningar (att utforska):
- Stiliserad kompass (pilot-metafor)
- Enkel typograf-monogram "JP"
- Platsbanken-aktig cirkel med subtil twist

---

## 12. Token-mappning till Tailwind 4

`app/globals.css`:

```css
@import "tailwindcss";

@theme {
  --color-surface-primary: #FFFFFF;
  --color-surface-secondary: #F7F7F5;
  --color-surface-tertiary: #EDECE7;
  --color-text-primary: #1A1A1A;
  --color-text-secondary: #5A5A5A;
  --color-text-tertiary: #8A8A85;
  --color-brand-50: #EAF2FB;
  --color-brand-500: #1F6EB8;
  --color-brand-600: #0B5CAD;
  --color-brand-700: #094B8C;
  --color-success-50: #E8F3EC;
  --color-success-600: #0F7A2E;
  --color-success-700: #0B5E24;
  --color-warning-50: #FAF2DE;
  --color-warning-600: #946200;
  --color-warning-700: #734D00;
  --color-danger-50: #FBEBEB;
  --color-danger-600: #B42121;
  --color-danger-700: #8C1919;
  --color-border-default: #D8D6D0;
  --color-border-strong: #B8B6B0;

  --radius-sm: 2px;
  --radius-md: 4px;
  --radius-lg: 6px;

  --font-sans: 'Hanken Grotesk', -apple-system, BlinkMacSystemFont,
               'Segoe UI', Roboto, sans-serif;
  --font-mono: 'JetBrains Mono', 'SF Mono', Menlo, monospace;
}

body {
  background: var(--color-surface-primary);
  color: var(--color-text-primary);
  font-family: var(--font-sans);
  font-size: 14px;
  line-height: 1.55;
  -webkit-font-smoothing: antialiased;
}
```

shadcn/ui-komponenter konfigureras via `components.json` och egen `tailwind.config.ts` som mappar CSS-variabler ovan till shadcn-tokens (primary, secondary, destructive, ring, etc).

---

## 13. Design review-checklista (före merge)

För varje PR som ändrar UI:

- [ ] Ingen emoji i UI-text
- [ ] Inga utropstecken i UI-text
- [ ] Radius ≤ 6px (utom pills)
- [ ] Ingen box-shadow utöver `--shadow-sm` / `--shadow-md`
- [ ] Alla färger från tokens, inga hårdkodade hex utanför `globals.css`
- [ ] Fokusring synlig på alla interaktiva element
- [ ] Kontrast ≥ AA verifierad
- [ ] Lighthouse a11y-score ≥ 95
- [ ] Svensk copy, kvantifierad där möjligt
- [ ] Tangentbordsnavigation fungerar
- [ ] Mobile-layout fungerar (även om ej optimerad i v1)
- [ ] Empty state finns om vyn kan vara tom

---

**Slut på DESIGN.md.** Huvudspec i [`BUILD.md`](./BUILD.md). Coding conventions i [`CLAUDE.md`](./CLAUDE.md).
