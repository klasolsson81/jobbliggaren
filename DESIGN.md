# DESIGN.md — Jobbliggaren design system

> Pedagogisk inramning för Jobbliggarens civic-utility-design.
> Fullständiga specer finns i Claude Code-skills under `.claude/skills/jobbpilot-design-*`.
>
> **Huvudspec:** [`BUILD.md`](./BUILD.md)
> **Coding conventions:** [`CLAUDE.md`](./CLAUDE.md)

---

## 1. Design-filosofi

### 1.1 Grundprincip

Jobbliggaren är ett verktyg för stressade jobbsökare. UI:t ska signalera **tillit och pålitlighet**, inte imponera eller underhålla. Målet är att en 55-årig processoperatör i Alingsås som söker sitt nästa jobb ska känna att appen är byggd för att fungera, inte för att sälja.

Referenser som ska kännas i allt vi bygger:
- **GOV.UK Design System** — typografisk hierarki, content-first, minimal dekoration
- **Digg / Sveriges designsystem** — svensk myndighetsprecedent
- **1177 Vårdguiden** — trygg, läsbar, accessible
- **Stripe Dashboard** — datatäthet utan kaos
- **Mercury Bank** — utility över branding

Referenser som **inte** ska kännas:
- Vercel / Linear / Arc — för trendigt, för mycket "vibe"
- Notion — för lekfullt
- Default shadcn/ui ur-lådan — standard-AI-look

### 1.2 Do / don't (snabbkort)

| ✅ Ja | ❌ Nej |
|-------|--------|
| Ljus default + dark mode stöds (auto via `prefers-color-scheme` + manuell toggle) | Forcerad dark utan användarval |
| Mörkgrön accent (`--jp-accent-700`, ADR 0068) | Neon, lila, cyan-accenter |
| Rak svensk copy | Emojis, utropstecken, "Let's go!" |
| Tabeller och listor | Kort-layouter överallt |
| `border-radius: 4px` | 16px+ rundade hörn |
| Muted statusfärger | Glow, drop shadow, glasmorfism |
| Breadcrumbs + hierarki | Flata sidor utan kontext |
| Systemfont/Source Sans 3 | Display-fonter, scripts |
| Content-first sidor (hero-bannern är en saklig sök-/orienterings-yta, ADR 0068) | Marketing-heros, vibey microcopy |
| Kvantifierad info | Vag "positiv" feedback |

---

## 2. Var finns vad?

Filosofi-sammanfattningar finns i denna fil. Fullständiga specer med tokens, varianter, kod och checklistor finns i skills:

| Område | Skill | Reference-filer |
|--------|-------|-----------------|
| Filosofi + do/don't + beslutramverk | `.claude/skills/jobbpilot-design-principles/SKILL.md` | — |
| Färg, typografi, spacing, radius, tokens | `.claude/skills/jobbpilot-design-tokens/SKILL.md` | tokens-full, contrast-table, dark-mode, theme-block |
| Komponenter (Button, Card, Table, Dialog…) | `.claude/skills/jobbpilot-design-components/SKILL.md` | variants-full, composition-examples |
| Svensk copy, microcopy, felkoder, locale | `.claude/skills/jobbpilot-design-copy/SKILL.md` | error-messages, microcopy-library, locale-formatting |
| Tillgänglighet (WCAG 2.1 AA) | `.claude/skills/jobbpilot-design-a11y/SKILL.md` | wcag-criteria, screen-reader-testing, testing-tools |

**Drift-skydd:** Sammanfattningarna §3–§9 är kurerade från skills-innehåll. När en skill uppdateras verifierar `docs-keeper` att relevant sammanfattning i denna fil fortfarande är i synk. Detta görs automatiskt vid session-end.

---

## 3. Färgsystem (sammanfattning)

Paletten är medvetet begränsad. Civic-produkter bygger tillit genom konsekvens — fler färger skapar kognitiv belastning. Kanon = `globals.css` (v3-neutraler + grön accent per ADR 0068); fullständiga värden i `jobbpilot-design-tokens`-skillen.

- **Accent (mörkgrön, ADR 0068):** `--jp-accent-800` `#15603F` fill (primärknapp, EJ dark-skiftad, vit text — aldrig ljus knapp/mörk text); `--jp-accent-700` `#15603F` light / `#6EE7A8` dark för länkar, aktiv nav, fokus **och indikator-barer för markerat/aktivt tillstånd** (`#6EE7A8` ALDRIG som fill); `--jp-accent-50` selektions-**fyllning**, aldrig ensam bärare — 1,14:1 mot vitt, under 1.4.11:s 3:1. Ett markerat tillstånd bärs alltid av `--jp-accent-700` som text eller kant; fyllningen är sekundär (`.jp-popover-row[aria-pressed]`, `.jp-btn--emphasis`, `.jp-tag--brand`). Ersätter tidigare blå/navy. Logo-marken (Sigillet, ADR 0070) bär grön skiva + guldsignatur `--jp-gold` `#E8C77B` — egen färgsättning utanför interaktions-accenten.
- **Hero-gradient (scoped undantag, ADR 0068):** `--jp-hero-gradient` (118° `#0B2A1E`→`#14503A`→`#1E6B4C`) ENBART på hero-banner-plattan/pagehero/landing-hero — gradients förbjudna överallt annars.
- **Fokus-tokenen är yt-scopad, indikator-tokenen är det inte (G1 + ADR 0068):** `--jp-focus`/`--color-focus-ring` re-scopas till `#FFFFFF` på gradient-ytorna (`.jp-hero__plate`, `.jp-pagehero__inner`) så ringen syns mot grönt — den scopingen gäller **fokus och inget annat**. En indikator som bär ett *tillstånd* läser `--jp-accent-700` direkt; läser den fokus-aliaset renderas den vit inuti plattan (mätt 1,18:1 mot en markerad `--jp-surface-3`-rad, 1,00:1 mot en vit yta). `--jp-accent-700` re-deklareras per tema, och en yta som pinnar den (`[data-theme="dark"] .jp-header`) pinnar till sitt eget läsbara värde — aldrig bort.
- **CV-mall-accenter (scoped undantag, PR-8b / ADR 0096):** de fyra kuraterade accentfärgerna för mallbyggarens CV-render — Marinblå `#1E3A5F`, Skogsgrön `#15603F`, Vinröd `#7A2E35`, Grafit `#3A4451` — gäller ENBART den exporterade CV-PDF:en (rubriker/streck på ljusa mallar; hela sidopanel-bakgrunden i Mörk panel under vit text, Klas 2026-07-12 "panelfärg = vald accent"). App-UI:t behåller den enda gröna interaktions-accenten (ADR 0068) — CV:t är ett användardokument, inte app-chrome, så en kuraterad flerfärgspalett där är en medveten avgränsad avvikelse (samma slag av scoped undantag som hero-gradienten). Varje accent WCAG-AA-gardad som par mot vitt (≥4.5:1, Skogsgrön ljusast = 7.56:1) via `CvPalette`-fitnessfunktionen; slutna `CvAccentColor`-SmartEnum-värden, aldrig fri hex. Bor i renderaren (`Infrastructure/Resumes/Rendering/CvPalette`), inte i `globals.css` (ingen app-yta konsumerar dem).
- **Neutraler (v3, ADR 0052; ink-3 mörkad i #296):** ink `#0C1A2E` / `#455366` / `#4F5D72` (text-tertiary mörkad från `#7C8AA0` till en hög-kontrast slate-navy så all 11–12.5px metadata-text klarar WCAG AA — min 5.45:1, issue #296; ink-1/-2 oförändrade av G1), surfaces `#FFFFFF` / `#F4F6FA` / `#E8EDF4`, canvas `#F4F6FA` light / `#0B1525` dark (mörk navy-grå, inte svart), placeholder `#626B78` (WCAG-motiverad).
- **Statusfärger:** success `#16793B`, warning `#A34A06` (mörkad från `#B4540B` för 4.5:1 pill-text, issue #193), danger `#BE1B1B`, info `#1B5396` + bg-varianter — endast för status (aldrig dekoration); oförändrade av accentbytet.
- **Bevakad-tillstånd (slate-teal, ADR 0116):** `--jp-follow` `#3E6C74` light / `#7FC4CE` dark (text/border/kort-vänsterkant) + `--jp-follow-bg` `#E2EEF0` / `#153338` (fyllning) — en FJÄRDE semantisk axel (relation: "du bevakar arbetsgivaren") vid sidan av grön=grad, blå=sparad/ansökt, neutral=tid. Bär `.jp-tag[data-tag="followed"]` (BEVAKAR-taggen, /jobb-kort + annonsmodal) + `.jp-job[data-followed]`-vänsterkanten (pseudo-element, överlever grön hover). Icke-grön (grad+interaktion låst, ADR 0068), icke-blå (sparad/ansökt), icke-danger. AA: light 5.83:1 mot vitt kort / 4.92:1 mot bg; dark 7.21:1 / 6.84:1 (design-reviewer re-verifierar mot rendering, §12).
- **Borders:** border `#C9D2E0` (dekorativa hairlines), border-soft `#E3E8F0`, border-strong `#7C8AA0` (informationsbärande dividers, mörkad från `#97A4B8` till 3.5:1-UI-golvet, issue #193 — delar nu värde med border-input, medveten tonalitet), border-input `#7C8AA0`; border-modal/-structural per ADR 0041 (re-homade på v3-border).
- **Skuggor:** bara shadow-card/pop/modal (popovers/dropdowns/modal) — djup skapas via border/hairline, aldrig på cards/knappar
- WCAG AA-kontrast obligatoriskt på alla färgpar. accent-700 på vit = 7.56:1; `#6EE7A8` på dark canvas = 11.9:1. Full tabell i tokens-skillens `contrast-table.md`.

### Dark-mode-stance

Dark mode **stöds** (designsystem v2, Klas-GO 2026-05-16 + ADR — ersätter Fas 0-borttagningen som skedde pga shadcn-presetens oklch indigo-violetter). v2 använder en **civic slate-skala utan dekorativ hue** (`data-theme="dark"` på `<html>`). Light är default; `prefers-color-scheme: dark` honoreras **automatiskt och utan flash** (inline pre-paint-script), manuell toggle överrider och persisteras i localStorage. Sunken-ytor är mörkare än canvas i båda lägen (samma papper-metafor). Light och dark valideras parallellt — aldrig dark som efterhandstillägg.

Exakta tokens och hex-värden (light+dark), kontrast-tabell och deploy-ready `@theme`/`--jp-*`-block → **jobbpilot-design-tokens**.

---

## 4. Typografi (sammanfattning)

- **Primär:** Source Sans 3 (`next/font/google`, variabel `--font-sans`) — weight 400–800 laddade, hela familjen 200–900 (LP-1 #254; 800 = display-klassen). Ersätter Hanken Grotesk — högre x-höjd/versalkvot (0,736 vs 0,707) ger tydligare text vid samma px samt civic-pedigree (USWDS-default, CSN) (#549 WS4, ADR 0091). Vikt-stegen är tokeniserad (`--jp-fw-*`, #549 WS2) — aldrig numeriska font-weight-litteraler i CSS/TSX.
- **Monospace:** JetBrains Mono (`next/font/google`, variabel `--font-mono`) — endast för bokstavs-/kod-identifierare och versala caps-labels (mono-kickers, kolumnrubriker, `SV`/`EN`, versioner, opaka stöd-koder) där rollen är *etikett/kod*, inte *läs talet*. Aldrig brödtext/rubriker/knapptext. **Aldrig för informationsbärande siffror** (#376 / ADR 0038-amendment).
- **Informationsbärande siffror** (antal, belopp, datum, tider, räknare, stats, ID-/SSYK-siffror användaren läser): Source Sans 3 (sans, `--font-sans`) med `font-variant-numeric: tabular-nums` — entydig läsbarhet vid synnedsättning (`0`≠`8`) med bibehållen kolumn-justering inom samma vikt (sifferbredd växer mellan vikter — verifierat riskfritt i appen idag, ADR 0091). Låg-syn-golvet (§1.1-målanvändaren, ADR 0038-linjen) väger tyngre än mono-kod-estetiken; `tabular-nums` är progressive enhancement (faller graciöst till proportionella figurer).
- **App-UI-roller (ADR 0038 — GOV.UK-läsbarhetsgolv; #549 WS1 / ADR 0068-notat 2026-07-03):** body **16px/400** (golv — aldrig informationsbärande text < 16px), body-sm/small **14px** (min), lede **17px/400**, h3/h4 **18/16px/700/ink-1**, h2 **20px/700/`--jp-heading-2`** (navy-800), h1 **32px/700/`--jp-heading-1`** (navy-700 — enad sidtitel-tier, re-align mot ADR 0052 Beslut 5). Rubriker = navy (information), grönt = interaktion (E2f-kärnan består). Dark: rubriker ink-1.
- **Display (banner-plattan, ADR 0068 G2):** 44px / 800 / line-height 1.1 / letter-spacing −0.025em (32px mobil) — följer F4-platta-komponenten var den används (/jobb-hero + pagehero på alla inre sidor). Landing-plattan: 56px-clamp / 700. Innehållsbredd-kanon app-wide = 1136px (header = platta = innehåll).
- **Inline-data (datum, ID-siffror, räknare):** sans 13px/500 + `tabular-nums`, färg `text-secondary`/`text-primary` (aldrig `text-tertiary`) — siffror läsbara, kolumner justerade (#376; mono-bärande caps-labels förblir mono)
- **Mono caps (labels):** 11px (`--text-overline`) / letter-spacing 0.08–0.16em / UPPERCASE, färg `text-secondary` — kickers, STATISKA kvitto-kolumnhuvuden (`UPPDATERAD · MAJ 2026`; statistik-tabellens icke-interaktiva `.jp-table thead th`); ALLA mono-caps-kickers är unifierade på denna kanoniska rung (#549-konsolideringen, CTO D2). Två sanktionerade avvikelser (CTO-bind 2026-07-10):
  - **(1) Interaktivt/sorterbart kolumnhuvud → kontrolltext, ej mono-caps** — Tabell-vyns `.jp-apptable__th` (/ansökningar) är sans (`--jp-font-sans`) / `--text-body-sm` 14px / `--jp-fw-semibold` / ink-1 / sentence-case (transform none, spacing normal). Ett sorterbart huvud *är* en kontroll (`--jp-control-fs`-golvet, §6), inte en kvitto-etikett; sortbtn-affordansen bärs av accent-700-hover + pil-glyf (WCAG 1.4.1), inte av ljushet. Diskriminator (thead-granulär, ej per-huvud): en **interaktiv thead** (minst ett sorterbart huvud) → hela huvudraden kontrolltext, icke-sorterbara huvuden i samma rad följer registret (blanda aldrig sans/mono i en rad); en **statisk/kvitto-thead** (statistik/admin, inga sorterbara huvuden) → mono-caps (#779).
  - **(2) Strukturell grupp-band-eyebrow → 13px/ink-1, ej 11px/text-secondary** — `.jp-allapps__restkicker` håller mono-caps-*registret* (700 caps + tracking 0.08em + hairline `--jp-border-soft`) men på legibility-grenen (krymp ej 13→11) och i ink-1 (strukturellt grupp-band, inte metadata — #549 WS1); superordinat via register, inte storlek. Register-syskon till 11px-rungen, ingen ny storleks-tier (design-reviewer MAJOR → CTO-bind 2026-07-10 / #768).
- **Läsbarhetsdoktrin (#549 WS1, CTO D4):** innehålls-/brödtext är **`text-primary` (ink-1) som default** — ljusa tiers är reserverade för äkta metadata. `text-secondary` (ink-2, 7.8:1) = timestamps, `<dt>`-etiketter, eyebrows, kvitton; `text-tertiary` (ink-3, 6.69:1 sedan #296-mörkningen — AA-säker, inte "dekorativ") = demoterad metadata (tider, ids); placeholder = `--jp-placeholder`. Aldrig grå-utils (`text-slate-*` m.fl.); shadcn `--muted-foreground` är remappad till ink-1.
- Civic-ledger-*formen* (flata tabeller, hairlines, mono-etiketter [utom interaktiva/sorterbara kolumnhuvuden, se avvikelse (1) ovan], inga cards) är oförändrad — ADR 0038 omkalibrerar skala/färg/fältstorlek; #376-amendmentet utsträcker samma läsbarhetslinje till siffer-glyfform (informationsbärande tal i sans, koder/labels i mono) (handoffen drev under §1.1-målanvändarens läsbarhetsbehov).
- Global text-tracking −0.005em (optisk täthet). Aldrig all caps i sans. Aldrig letter-spacing-justeringar i brödtext.
- Italic bara i citat och referenser, aldrig för emfas i body.

Komplett skala, line-heights och Tailwind-mappning → **jobbpilot-design-tokens**.

---

## 5. Spacing och layout (sammanfattning)

- **4px-baserad skala.** Vanliga värden: 8, 12, 16, 24, 28, 48, 64.
- **Border-radius:** sm 2px (inputs/badges), md 4px (default — knappar, panels, sökruta), lg 6px (större paneler/dropdowns), pill 9999px (endast statusprickar/pills). Inga andra radier — inga 8/10/12px.
- **App shell (Variant B):** vänster sidebar 240px med `border-right` hairline, topbar 56px, innehåll max-width 1080px.
- **Formulär:** max-width 640px, labels alltid ovanför inputs.
- Desktop-first — touch (≤768px) bumpar hit-targets till 44px, ledger-tabeller stackas (utvecklaransvar).

---

## 6. Komponenter (sammanfattning)

shadcn/ui är primitive-lagret. Komponenter kopieras in i `components/ui/` — de ägs av projektet, importeras inte från npm.

Använda i v1: Button, Card, Input, Textarea, Select, Badge, Dialog, Toast, Table, Breadcrumb, Form, Skeleton, Alert.

Aldrig byt ut mot: Material UI, Chakra, Mantine, Headless UI.

**Civic-utility patterns (v2, `.jp-*`-systemet i globals.css):**
- `.jp-table--flat` — ledger-tabell. Ingen zebra-stripe, inga inramade celler, hairlines mellan rader, fetare topp/botten-linje
- `.jp-attentionqueue` — prioriterad lyft-lista (Ansökningar). Återanvänder ledger-raden `.jp-app`; lede max 68ch, hairlines, ingen låda
- `.jp-pipeline` — kanban som ledger-rader, kolumner åtskilda av `border-strong`, INGA floating cards
- `.jp-statusDot` (förstaval i tabeller — prick + text, ingen bg) vs `.jp-pill` (accent vid entitet — färgad 50-bg + prick + text)
- `.jp-matchchip` — namngiven matchningsgrad (Toppmatch/Stark/Bra/Grundmatch),
  plus `--related` "Relaterat yrke", som är en egen grad men får den **neutrala**
  status-behandlingen — **inte ett femte grönt steg** (en femte fyllning hade krävt
  en ny leaf-hue; ADR 0084 F2).
  Enda tillåtna formen för **graden**: aldrig mätare, procenttal eller ring (§8,
  ADR 0076 Decision 4, ADR 0053 Amendment 2026-06-19). Matchar/saknas **per
  dimension** är en egen form (`.jp-modal__matchrow`, hålig prick för "Ej bedömt")
  — båda halvorna krävs. Föregångaren `.jp-match` (6px score bar) är borttagen.
  Frånvaro — vad som visas när annonsen inte når någon grad alls: §8.
- `.jp-filterBar` — flat rad mellan två hairlines, fält i naturlig bredd, ingen chrome-box
- `.jp-banner` — info-banner med 3px brand-vänsterkant, används sparsamt
- **Knapphöjd — TVÅ ratificerade system, båda korrekta. Namnge alltid vilket du menar.** `.jp-btn` = **44px** (`--sm` 36; `--lg` 52 är ratificerad men **oimplementerad** — klassen finns inte, så `jp-btn--lg` ger tyst 44px) i 46 filer (42 produktionsfiler) — ratificerad av HANDOVER-v3 §5.1 via ADR 0052 (Amendment 2026-07-27). shadcn `Button` = **40px** (`sm` 36, `lg` 44) — ratificerad av ADR 0038. **Ingen av dem är drift.** En blank mening som "knapphöjd = 40px" är falsk genom utelämnande oavsett siffra; skriv ut systemet. Radius `--jp-r-md` = **6px** (ADR 0052 Beslut 4: knappar 6px). Transition **90ms** (`.jp-btn`; shadcn `Button` `duration-75` = 75ms). Max EN `--primary` per skärm (ADR 0038). Inline-padding = `--jp-btn-px` (**18px**, `--sm` 14px via scoped re-pin) — namnet finns för att `.jp-btn--flush` ska kunna upphäva **exakt** den med `calc(-1 * var(--jp-btn-px))`, så en knapps TEXT hamnar på rälsen (#1090; alternativen är `padding-inline: 0`, som får ghost-hoverns fyllning att klistra sig mot glyferna, och ett `-18px` kopplat till regeln av ingenting). CSS-scopad till `:first-child` — "flush" betyder bara något först i sin rad, och efter en chipsrad skulle marginalen äta gapet. **Inte** samma sak som `--jp-control-px` (kontroll-storleks-punkten nedan), som är en DELAD SSOT över flera komponenter; denna läses av knapp-familjen plus sin egen upphävare. **Inte** heller det tokeniserings-punkten avvisar: ett `--jp-field-h` **skulle** dölja vilket av två system som gällde, medan `--jp-btn-px` namnger sitt (`.jp-btn`) och ändrar inget renderat värde. Klas-beslut 2026-07-28.
- **Samma betoningsnivå på `.jp-btn`-chassit: `.jp-btn--emphasis`** (#1373). Speglar `.jp-rowbtn--emphasis` exakt — `--jp-accent-50`-vilo-tint + `--jp-accent-700` text och kant + `--jp-fw-bold` — för ytor som bär `.jp-btn` i stället för `.jp-rowbtn`. Modifiern finns för att kort-griderna behövde samma nivå: `ResumeCard` renderas en gång per CV, så en `--primary` där hade gett N solida accent-800-fyllningar på `/cv` och brutit raden nedan. Komponerar befintliga tokens, inför inga nya. Kontrast mätt: text 6,62:1, kant mot vitt kort 7,56:1.
- **Rad-CTA-betoning (`.jp-rowbtn--emphasis`, ej en andra primär):** en föreslagen radhandling (t.ex. "Flytta till {status}" i ansöknings-raden) betonas via `--jp-accent-700`-kant + `--jp-fw-bold` på den vita `.jp-rowbtn`-basen — en UNDERORDNAD nivå *under* den solida en-per-skärm-primären (`.jp-btn--primary`, accent-800-fyllning). Aldrig solid fyllning per rad (N rader = N knappar bryter regeln ovan); betoningen håller sig innanför ADR 0038 just genom att vara icke-solid. Blir nivån för tyst eskaleras den *inom* den icke-solida nivån (t.ex. `--jp-accent-50`-vilo-tint + distinkt hover), aldrig till solid fyllning (CTO-bind 2026-07-12, #788).
- **Input/Select-höjd — samma två system, samma regel.** `.jp-input` = **48px** (sm 40 ratificerad men **oimplementerad** — ingen `.jp-input--sm` finns) och radie **6px** — HANDOVER-v3 §5.2 via ADR 0052 (Amendment 2026-07-27); bumpen gjordes för att ett v2-användartest föll för §1.1-målanvändaren (55-åriga jobbsökare), så den är avsiktlig, inte slarv. shadcn `Input` = **44px** (ingen size-prop) och `SelectTrigger` = **44px** (`sm` 36) — ADR 0038; de bär flest ytor (`ui/input` i 20 filer, `ui/select` i 2 produktionsfiler). `.jp-sortfield__select` läser `--jp-control-h`; `.jp-select`/`.jp-textarea` städades bort i #1073; textarea bärs av `[data-slot="textarea"]` (`min-h-16`). **Följd av deltat:** en `.jp-input` (48px) och en `.jp-btn` (44px) på samma rad linjerar inte — para dem med `.jp-btn--field` (48px). Label alltid ovanför, hint under. **Inga beskrivande placeholder-exempel i sök/filter-fält** (Nielsen/WCAG-anti-pattern). Format-placeholder i auth-formulär OK (`namn@exempel.se` = syntax, ej exempelinnehåll)
- **Varför två system, och varför det inte är drift (#1095, avgjort 2026-07-27).** En revision flaggade 48/44 som avvikelse från ADR 0038. Fel: båda värdena är protokollförda i `HANDOVER-v3.md` §5.1/§5.2, den Klas-beslutade designspec vars **header** (rad 3) bär vetot över befintliga ADR:er (filen är **gitignorerad** — `.gitignore:104`; substansen är transkriberad in i ADR 0052:s amendment så den går att läsa ur repot, och `.worktreeinclude` tar med den i worktrees), och som ADR 0052 säger sig vara transkriberad från. Radien och typografin fördes över till Beslut 4/5; **höjdraderna tappades i just den transkriptionen** — en ofullständig transkription, inte ett obeslutat värde. ADR 0038:s 44/40 står kvar och är korrekt **för de primitiver den styr**. Alltså: ADR 0038 → shadcn-primitiverna, ADR 0052/HANDOVER → `.jp-*`. **Tokenisera inte 48:an** — ett `--jp-field-h` skulle dölja vilket system som gäller bakom ett namn där ingen letar (samma form som density-systemet, retirerat 2026-07-26). Den verkliga SSOT-kandidaten ligger i /jobb-sökraden, som upprepar 52px i tre regler för en visuell rad.
- **`.jp-*` är OLAGRAT — en Tailwind-utility för en egenskap som `.jp-*`-regeln SJÄLV sätter är tyst verkningslös.** `@layer base`/`utilities` ägs av Tailwind; `.jp-*`-reglerna står utanför alla lager, och olagrad CSS vinner över varje lager. Det gäller varje egenskap, inte bara höjd: `.jp-btn` sätter både `height` och `padding`, så `className="jp-btn h-12"` OCH `className="jp-btn px-0"` är båda no-ops. Utan fel och utan varning — `guard:css` läser stilmallen, jsdom har ingen kaskad, eslint ser en giltig sträng. Mätt 2026-07-26: `{"input":48,"button":44}` med `h-12` på elementet. Ändra i regeln eller i en `.jp-*`-modifier (som `.jp-btn--field`) — aldrig med en utility. **Shorthand-fällan:** en regel som sätter `padding: 0 32px` blockerar även `py-4`, inte bara `px-*` — shorthanden sätter alla fyra sidorna. Omvänt gäller regeln BARA de egenskaper klassen faktiskt sätter: `.jp-skeleton` sätter bara `background` + `border-radius`, så `className="jp-skeleton h-10"` fungerar och görs på 59 ställen.
- **Kontroll-storlek (filterrader)** — `--jp-control-h` (40px) + `--jp-control-fs` (14px) + `--jp-control-px` (14px) i `globals.css` är SSOT för filterradernas kontroll-storlek (hero-pills + sort-select på /jobb). Ändra storlek/textstorlek HÄR, aldrig inline per komponent — så hela filterraden förblir EN form med EN textstorlek (Klas 2026-06-30). Textstorlek ≥ 14px-golvet (§4 — kontrolltext aldrig under body-sm); mobil bumpar hit-targets till 44px (WCAG 2.5.5).

Regler:
- En primary button per form — aldrig två primärknappar sida vid sida
- Destructive actions kräver alltid bekräftelse-dialog
- Icon-only buttons kräver `aria-label`
- Loading state: ersätt label med "Sparar…", behåll bredd, sätt `disabled`
- Inga stats-kort runt enstaka värden — visa siffran direkt i rad/tabell ovanför listan

Full spec, variant-states och JSX-kompositionsexempel → **jobbpilot-design-components**.

---

## 7. Ikoner

- Bibliotek: **Lucide React** — stroke/outline only, inga filled variants
- Default: `size-4` (16px) inline med text, `size-5` (20px) fristående
- Färg ärvs via `currentColor` — aldrig hårdkodad ikonfärg
- Inga emojis i UI-text, oavsett kontext

---

## 8. Copy-riktlinjer (sammanfattning)

- **Du-tilltal** alltid — "du" inte "Du" eller "ni"
- **Direkt:** 10 ord där möjligt, inte 25
- **Konkret:** siffror, datum, namn — "Intervjun är 14 apr kl 10:00" slår "Du har en kommande intervju"
- **Opretentiös:** inga ordspråk, inga liknelser, ingen peppning
- Inga utropstecken i info/success. OK i error om det förstärker brådska — sparsamt.
- Inga emojis, inga engelska fraser i svensk copy
- Svenska locale-format: "14 apr 2026", "14:32", "33 456 kr"
- Matchning och CV-omdömen presenteras som kategori, aldrig som procenttal,
  mätare eller ring (ADR 0076 Decision 4, ADR 0053 Amendment 2026-06-19). För
  matchning är formen en namngiven grad plus matchar/saknas per dimension
- **Saknad matchningsgrad visas inte** — har en annons ingen grad alls renderas
  varken `.jp-matchchip` eller en mening om att graden saknas, varken på
  /jobb-kortet eller i matchningssektionen (Klas 2026-09-01, räckvidden
  2026-09-02, #1613). I sektionen vilar tystnaden på att Yrke-raden står kvar
  och bär sitt verdikt och sitt skäl eller sitt bevis; utan den raden är
  sektionen inte tyst utan tom. Skyltar som är **åtgärdbara** — angivet yrke,
  uppladdat CV — står kvar; den om angivet yrke ersätter dessutom hela sektionen
- **Ort-axeln heter "Ort"** — dess granulariteter heter "Län" och "Kommun". En
  resultat-etikett namnger AXELN (senior-cto-advisor 2026-09-01, #1623). Regeln
  binder varje yta som rapporterar utfallet, resultatrad såväl som förklaringstabell
- **Bevisramen förutsätter sitt led** — listan över vad annonsen efterfrågar ramas
  med "även" bara när raden faktiskt visar en föregående träff. Utan träff faller
  adverbet bort (`jobads.ui.match.requested` i stället för
  `jobads.ui.match.alsoRequested`).
  Gäller varje dimension som når den generiska bevisformen (#1627)
- Varje CV-omdöme pekar ut sitt underlag i CV:t, som citat eller som observation;
  "Ej bedömt" redovisas som ej bedömt, aldrig som en gissad grad (CLAUDE.md §5)
- Ingen AI/LLM i produkten (ADR 0071) — det finns ingen AI-samtyckescopy att skriva

Microcopy-library, felkoder (40+ med svenska translations) och locale-formatting-funktioner → **jobbpilot-design-copy**.

---

## 9. Tillgänglighet (sammanfattning)

WCAG 2.1 AA är golvet, inte målet. Ingen dispens för MVP eller tidspress.

- Lighthouse a11y-score **≥ 95** innan merge
- axe DevTools: **0 violations** per ny sida/komponent
- Synlig fokusring obligatorisk — aldrig `outline: none` utan ersättning
- Tangentbordsnavigation måste fungera för alla interaktiva element
- Formulär: `<label>` alltid kopplad, `aria-invalid` på felfält, feltext (inte bara röd border)
- Design-reviewer klassificerar alla a11y-brister som **Blocker** utan undantag

Komplett WCAG-checklist (20 punkter), screen reader-testplaybook (NVDA + VoiceOver) och verktygsguide (axe, Lighthouse, eslint-plugin-jsx-a11y) → **jobbpilot-design-a11y**.

---

## 10. Motion

- Minimalt. Civic-produkter rör sig inte för att "kännas levande".
- Tillåtna animationer: Fade 150ms (toast, dropdown), Slide 200ms (side panel), Opacity 150ms (hover)
- Ingen bounce, spring, scale-on-hover, parallax, wiggle
- `prefers-reduced-motion: reduce` respekteras — stänger av alla animationer

---

## 11. Logotyp

Logo-marken är **Sigillet** ([ADR 0070](./docs/decisions/0070-sigillet-brand-mark-och-spinner.md), 2026-06-13): ett fyllt civilt registersigill — slät grön skiva (`--jp-accent-800` `#15603F`) + tunn vit inre ring + tre liggar-rader, mittenraden guld (`--jp-gold` `#E8C77B`) med en bock = en loggad post. Semantiskt knutet till namnet Jobbliggaren (liggare = register) och `.jp-table--flat`-formspråket. Ersätter den tidigare 4-uddiga kompassen; ADR 0068:s "kompassen förblir navy + guldprick"-not är därmed superseded.

SSOT: `web/jobbliggaren-web/src/components/brand/brand-mark-svg.tsx` (`BrandMarkSvg`, 3-färgskontrakt primär/accent/papper via `--jp-mark-*`) + `brand-logo.tsx` (`BrandLogo`). Wordmark "Jobbliggaren" i ink. Ytor: `icon.svg`, `apple-icon`, `opengraph-image`, `twitter-image`, `manifest.ts` (theme_color grön `#15603F`).

**Header-lockup** (ADR 0070 Fas 3): `BrandLogo` `full`-varianten är mark + en staplad [wordmark / tagline]. Taglinen "Den svenska jobbansökningshanteraren" (= OG-/social-taglinen) sätts som under-rad i sentence-case (12px, vikt 500, `--jp-ink-2`), `aria-hidden` (`.jp-brand`-länken bär accessible name). Mark 40px, wordmark 24px, alla fyra brand-header-barer 88px (`.jp-header__inner` = app + gäst, `.jp-head__inner` = landing + site). `mark`-varianten (minimala kontexter) är oförändrad — bara sigillet, ingen tagline.

**Laddningsindikator — spinner-vs-skeleton-doktrin** (ADR 0070 Fas 2): `BrandSpinner` ("Sigillet i rörelse" — pulserande register + roterande guldbåge; ren CSS, `prefers-reduced-motion` → statisk seal, `role="status"` + sr-only-label). **Skeleton är default (~90 %)** för innehållsladdning med känd form (listor, kort, detaljvyer) — den visar formen som fylls i. `BrandSpinner` används **endast** för känt-långsamma, formlösa väntor (> ~1–2 s): öppna ytan direkt, visa sedan spinnern + en svensk statusrad inuti ("Jobbannonsen läses in…"). Första konsumenter: jobbannons-modalen + sparad-ansökan-modalen (`ModalLoadingShell`). **Aldrig** spinner på snabba sid-/flik-byten — läser som jank och eroderar det seriösa intrycket.

**Guld utanför sigillet** (Klas-direktiv 2026-08-23, #1480): `--jp-gold` får bära en **typografisk roll** på landningsplattans kicker (`.jp-land-hero__kicker`) och footerns kolumnrubriker (`.jp-foot__colhead`). Båda är mono-versaler i överlinje-storlek och båda ligger på `#0B2A1E`: footern på solid `--jp-accent-900`, kickern på hero-gradientens första stopp (`--jp-hero-from`, samma värde). Paret mäter **9,45:1**, räknat ur tokenvärdena. **Fristående dekor är fortsatt förbjudet**: ingen guldregel, ingen guldram, ingen guldprick som inte är sigillets egen. Regeln är den här filens — ADR 0070 *inför* guldet och förbjuder ingenting. `design-reviewer` har veto på varje ny guldyta.

Krav (uppfyllda): SVG; fungerar på ljus och mörk bakgrund; monokrom fallback (sätt accent = papper); civic-ton (geometrisk, stabil, inte lekfull).

---

## 11.5 E-post

Transaktionell e-post är ett eget medium med egna begränsningar, och **tre av den här filens regler
kan inte följas där**. Avstegen är ratificerade här i stället för att argumenteras i en kodkommentar,
så att den som redigerar en token vet att kopior finns (#183, 2026-08-12, `design-reviewer`).

**Enda implementation:** `src/Jobbliggaren.Infrastructure/Email/EmailHtml.cs` (skal + primitiver);
copyn ligger i `EmailTemplates.cs` bredvid textdelen den speglar. Ingen annan yta får rendera e-post.

1. **Färger är hex-literaler, inte `--jp-*`.** En e-postklient läser inga custom properties, så
   literalen är den enda möjliga formen. Varje literal namnger sin källtoken på egen rad, och
   `EmailPaletteMirrorsDesignTokensTests` assertar dem mot `globals.css` — kopian är gjord
   kontrollerbar, inte bara deklarerad (samma disciplin som `CvPalette`/`CvPaletteTests`).
   **Inga nya tokens definieras i e-postlagret**, och en färgändring är fortfarande en DESIGN.md-
   ändring först.
2. **Systemfonter, inte Source Sans 3.** En webbfont är en fjärresurs, och HTML-delen får referera
   **noll** fjärresurser — det är en GDPR-kontroll (Art. 30-postens retentionsgrund, pinnad med test),
   inte en preferens. Avsteget är verkligt och inte en teknikalitet: skill-regeln lyder "aldrig
   Inter/Roboto/Arial/**system-ui** som primär", och `-apple-system` *är* system-ui. Stacken är
   `-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif`; Outlooks Word-motor
   hoppar över de två första och landar på Segoe UI.
3. **Egen typskala, inte app-UI-rollerna i §4.** Ett mejl är inte app-chrome och renderas i en
   främmande klient. Skalan är: rubrik **22px/700** i `--jp-navy-800`-värdet, brödtext **16px/1.55**,
   sidfot **14px** (golvet, aldrig under). Ordmärket i sidfoten är **16px/700 utan negativ tracking** —
   det får aldrig väga tyngre än brödtexten, eftersom "ingen grå text" tar bort färg som hierarki-axel
   och då måste storlek och vikt bära den ensamma.

**Dessutom, och utan avsteg:** tabellayout och inline-CSS (inget `<style>`-block alls), max 600px,
ingen flexbox/grid, `color-scheme: light` — mejlet är avsiktligt ljust i båda teman, vilket är rätt
e-postpraxis och det enda undantaget från "light-only är blockerat". **Guld (`--jp-gold`) hör till
sigillet och får inte användas som fristående dekor i mejl** (§11 ovan; ADR 0070
inför guldet och bär ingen sådan regel). Brand-signalen är den gröna
4px-regeln överst, och den ska vara den enda.

---

## 12. Granskning

Design-compliance verifieras av `design-reviewer`-agenten vid varje frontend-diff. Hennes auktoritet är denna fil + skills-detaljerna. Hon har veto-makt på design-frågor — ingen MVP-dispens, inget konsensus-override.

Ett designverdikt över en yta vars icke-vilotillstånd (fel, vägran, kvittens/utfall, tomläge, laddning) ingår i deltat fälls mot rendering av de tillstånden, inte mot diffen ensam. Saknas renderingen är verdiktet inte godkänt — det är återremiss tills tillståndet renderats. Ingen MVP-dispens. (AGENTS.md §8 punkt 4; trigger, kostnadsgräns och mekanik i `docs/runbooks/frontend-visual-verification.md`.)

---

**Slut på DESIGN.md.** Fullständiga specer i `.claude/skills/jobbpilot-design-*`. Huvudspec i [`BUILD.md`](./BUILD.md). Coding conventions i [`CLAUDE.md`](./CLAUDE.md).
