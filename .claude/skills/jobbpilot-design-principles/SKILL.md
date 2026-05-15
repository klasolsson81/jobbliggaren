---
name: jobbpilot-design-principles
description: >
  Loads JobbPilot's civic-utility design philosophy and do/don't rules. Use
  this skill whenever design direction, UI tone, visual treatment, aesthetic
  fit, or comparison to other products is discussed. Triggers on: design, tone,
  civic, utility, aesthetic, look, feel, brand, philosophy, direction, visual,
  GOV.UK, 1177, Digg, Stripe Dashboard, Linear, Vercel, Notion, glasmorfism,
  gradient, glow, AI-aesthetics.
---

# JobbPilot Design Principles

> **Canonical reference** for JobbPilot's civic-utility design philosophy.
> Deeper specs live in companion skills:
> - Design tokens (colors, typography, spacing) → `jobbpilot-design-tokens`
> - Component behavior → `jobbpilot-design-components`
> - Swedish copy patterns → `jobbpilot-design-copy`
> - Accessibility (WCAG, keyboard, focus) → `jobbpilot-design-a11y`
>
> This skill is the **why**. The others are the **how**.

---

## Core principle

JobbPilot is a tool for stressed job-seekers. The UI signals **trust and
reliability** — it does not impress or entertain. The target user is a
55-year-old process operator in Alingsås looking for her next job. She should
feel the app is built to function, not to sell.

Every design decision answers one question: **does this help a stressed user,
or does it add cognitive load?**

---

## Reference aesthetics — what JobbPilot should feel like

| Product | What to borrow |
|---|---|
| **GOV.UK Design System** | Typographic hierarchy, content-first, minimal decoration |
| **Digg / Sveriges designsystem** | Swedish civic precedent, institutional credibility |
| **1177 Vårdguiden** | Safe, legible, accessible — never intimidating |
| **Stripe Dashboard** | Information density without visual chaos |
| **Mercury Bank** | Utility over branding — the product IS the interface |

---

## Anti-references — what JobbPilot must NOT feel like

| Product | Why it is wrong for JobbPilot |
|---|---|
| **Vercel / Linear / Arc** | Too trendy, too "vibe" — signals startup coolness, not public utility |
| **Notion** | Too playful — wrong emotional register for job stress |
| **Default shadcn/ui out-of-box** | Standard AI-app look — civic-utility requires deliberate override |

---

## Do / don't quick reference

| ✅ Ja | ❌ Nej |
|-------|--------|
| Ljus default + dark mode stöds (auto via `prefers-color-scheme` + manuell toggle) | Forcerad dark utan användarval |
| Myndighetsblå primary color | Neon, purple, cyan accents |
| Direct Swedish copy | Emojis, exclamation marks, "Let's go!" |
| Tables and lists | Card layouts everywhere |
| `border-radius: 4px` | 16px+ rounded corners |
| Muted status colors | Glow, drop shadow, glassmorphism |
| Breadcrumbs + hierarchy | Flat pages without context |
| Hanken Grotesk / JetBrains Mono | Display fonts, scripts, Inter/Roboto/Arial |
| Content-first pages | Hero sections, vibey microcopy |
| Quantified information | Vague "positive" feedback |
| Solid backgrounds via tokens | Gradient backgrounds |
| `shadow-sm`/`shadow-md` on popovers only | Drop-shadow on cards/buttons |

---

## De sju reglerna

JobbPilots gränssnitt är byggt i en design-tradition vi kallar
**civic-utility**. Tonen är inspirerad av offentliga myndighetsportaler
(Skatteverket, Försäkringskassan), datavisualiseringsverktyg (Bloomberg
Terminal, Linear i tabellvyer), tidiga webb-CRM (Basecamp v1, Highrise) och
tryckta tidtabeller. Den är **inte** modern AI-app-design, konsument-SaaS,
"glass"-UI eller marknadsförings-orienterad.

### 1. Papper, inte glas

Vit canvas. Hairlines mellan rader och sektioner. **Inga floating cards. Inga
drop shadows utan funktion** (skuggor finns bara på popovers/dropdowns för att
signalera lager). **Inga gradients någonstans.** Tänk på UI:t som ett dokument,
inte en glasplatta med widgets ovanpå.

### 2. Information är design

Siffror, statusar, datum visas **direkt på canvas**, separerade med vertikala
hairlines — inte inramade i lådor.
- **Förbjudet:** stat-kort med en siffra inramad; cards runt en metric.
- **Korrekt:** en rad `3 391 träffar · uppdaterad 14:32` i mono ovanför en flat
  tabell; en kolumn-toolbar med 4 värden separerade av hairlines.

### 3. Inga fyllnadselement

Varje pixel ska bära information.
- **Förbjudet:** ikoner som "smyckar" varje rad; tooltips på allt;
  illustrationer på tom-states; achievement-badges; auto-genererade avatarer
  (initialer är okej).
- **Korrekt:** tom-state är en mening centrerad text i tertiary färg; ikoner
  finns där de signalerar handling (sök, kalender, dismiss); stats-kort tas
  bort när siffran redan står i tabellen nedanför.

### 4. Mono som signal

JetBrains Mono används för ID:n/referenser (`S-1042`, `A-2841`), datum
(`2026-05-21 14:00`), tid (`14:32`), versioner (`v2.3.1`), tangentbordsgenvägar
(`⌘K`), caps-labels (`UPPDATERAD · MAJ 2026`), och räknare i pills
(`12`, `3 391`). **Aldrig** för brödtext, rubriker eller knapptext.

### 5. En accentfärg

Myndighetsblå (`--jp-brand-600`, `#0B5CAD` light) är produktens enda dekorativa
färg, reserverad för primär åtgärd, aktiv selektion (rader/flikar/navigation),
länkar och "idag"-markering. Status-färgerna används **endast** för status:
`success` → erbjudande/drift/klar; `warning` → deadlines/uppmärksamhet;
`danger` → avslag/fel/destruktivt; `info` → neutral info (skickad/bekräftad).
**Aldrig** för temamarkering, dekoration eller "brand expression".

### 6. Tydlig, inte cute

Ingen emoji. Ingen "AI-typografi" ("Hej Klas! 👋 Här är din dagliga
sammanfattning ✨"). Inga marknadsföringsfraser ("Boostra din karriär!"). Inga
utropstecken (utom i felmeddelanden där de signalerar verklig urgency). Tonen
är **saklig** — som en myndighetsapp som råkar vara välgjord.

### 7. Densitet med respekt

Tätt nog att skanna, luftigt nog att läsa. Använd
`--jp-density`-multiplikatorn (`compact`/`standard`/`luftig`) hellre än att
hårdkoda padding. Riktlinjer: hit-targets minst 32px (28px för sm-knappar i
toolbars); tabellrader minst 36px höga; stats-värden 28+px; text-tracking
-0.005em globalt för optisk täthet.

---

## Decision framework

When a design decision is unclear, apply in order:

1. **Trust vs trend?** Choose trust.
2. **Will this look dated in 5 years, or will it feel like a public utility?**
   Choose utility.
3. **Does this help a stressed user, or does it add cognitive load?**
   Choose helping.
4. **If Digg or 1177 wouldn't do it, we don't either** — unless there is a
   documented exception via ADR.

---

## Tone identity (one sentence)

JobbPilot is Sweden's 1177 for job applications: authoritative, calm,
accessible, and built to be trusted — not admired.

---

## Förbjudna mönster (anti-pattern catalog)

### Layout & styling
- ✗ Gradienter på bakgrunder, knappar, badges
- ✗ Drop-shadows på cards (skuggor endast på popovers/dropdowns)
- ✗ Avrundade hörn över 6px (utom pill-prickar)
- ✗ Floating Action Buttons (FAB)
- ✗ Floating cards
- ✗ Cards runt enstaka värden
- ✗ Stats-kort när siffran redan visas i tabell
- ✗ Färgade chip-bakgrunder i kalenderceller
- ✗ Zebra-stripes i tabeller
- ✗ Inramade tabellceller (celled borders)

### Typografi
- ✗ Inter, Roboto, Arial som primär font
- ✗ system-ui som primär font
- ✗ Mono för brödtext
- ✗ Sans för identifierare/datum
- ✗ Versaler för rubriker (utom mono caps-labels)

### Innehåll & ton
- ✗ Emoji som ikon eller dekoration
- ✗ Marknadsföringscopy
- ✗ "AI hjälper dig!"-fraser
- ✗ Animerade hand-emojis, raketer, glitter
- ✗ Hype-språk ("Lås upp din potential")
- ✗ Konstgjord brådska ("3 personer tittar på detta jobb just nu!")

### Komponenter
- ✗ Onödiga tooltips på allt
- ✗ Auto-genererade ikoner per rad
- ✗ Achievement-badges
- ✗ Färgade brand-glyphs på OAuth-knappar (använd monokrom monogram)
- ✗ Floating labels på inputs

### Interaktion
- ✗ Auto-spelade animationer
- ✗ "Bouncy" easing-curves
- ✗ Inertia-baserade transitions över 300ms
- ✗ Confetti, partikel-effekter

---

## Checklista för Claude (eller utvecklare)

Innan en PR lämnas, gå igenom:

1. ✓ Använder du befintliga CSS-variabler (`--jp-*`) för ALLA färger? (sök efter hårdkodade hex)
2. ✓ Är komponenten en variant av en befintlig pattern eller en ny art? Om ny — motivera först.
3. ✓ Finns drop-shadow eller gradient i designen? Ta bort.
4. ✓ Är ikoner faktiskt nyttiga, eller dekorativa? Ta bort dekorativa.
5. ✓ Är tonen saklig — inga AI-fraser, inga uppmaningar?
6. ✓ Är tabeller flat (`.jp-table--flat`) och rader hairline-separerade?
7. ✓ Renderar layouten korrekt i både light och dark mode? Toggla och kolla.
8. ✓ Är språket konsekvent svenska, med engelsk översättning för landingpage?
9. ✓ Använder du `minmax(0, 1fr)` på grids där innehåll kan tryckas iväg?
10. ✓ Föreslår du ändringar utanför det användaren bad om? Fråga först.
11. ✓ Text-kontrast WCAG AA (4.5:1, 3:1 för 18+ px)?
12. ✓ Informationsbärande dividers (kolumngränser) använder `--jp-border-strong`, inte `--jp-border`?
13. ✓ Status-information har både färg OCH textetikett (aldrig endast färg)?
14. ✓ Brödtext har `max-width` runt 68ch så rader inte sträcks ut på breda skärmar?
15. ✓ Hit-targets minst 32×32px (28px endast i toolbars)?

---

## Färdiga jämförelser (bra/dåligt)

### Översikt-sammanfattning

**Dåligt** (AI-SaaS):
```
┌─────────────────┐  ┌─────────────────┐
│  Active jobs    │  │  Interviews     │
│      12 🎯      │  │       2 🎤      │
│   +3 this week  │  │  next: Friday   │
└─────────────────┘  └─────────────────┘
```

**Bra** (civic-utility):
```
Aktuellt
─────────────────────────────────────
● Erbjudande från Bonnier News väntar
  på svar — sista dag 18 maj.
─────────────────────────────────────
● Du har 2 intervjuer kommande vecka.
  Nästa: Folksam IT, 21 maj 14:00.
─────────────────────────────────────
```

### Status-indikator

**Dåligt:**
```
┌──────────────┐
│ 🟢 ACCEPTED  │
└──────────────┘
```

**Bra:**
```
• Erbjudande
```
(grön prick + saklig svensk text, ingen ram)

### Filterbar

**Dåligt:** stor inramad sektion med shadow + rounded corners 16px

**Bra:** flat rad mellan två hairlines, fält i sin naturliga bredd

---

## When this skill is not enough

Load the companion skill for the specific question:

- Exact token values (colors, type scale, spacing grid) → `jobbpilot-design-tokens`
- How a specific component (Button, Card, Table, Input) should behave →
  `jobbpilot-design-components`
- Swedish copy patterns, empty states, error messages → `jobbpilot-design-copy`
- WCAG requirements, keyboard navigation, focus management → `jobbpilot-design-a11y`
