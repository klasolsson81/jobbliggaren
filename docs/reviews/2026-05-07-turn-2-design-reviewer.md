# Design Review — Turn 2 (layout.tsx + mig/page.tsx)
Datum: 2026-05-07
Granskare: design-reviewer agent
Auktoritet: DESIGN.md, ADR 0016, CLAUDE.md §5.2

## Verdict

Clean — inga blockers, inga majors. Minor-fynd dokumenterade nedan.

---

## Fynd per fil

### src/app/(app)/layout.tsx

**Övergripande:** Filen är välstrukturerad och håller civic-utility-disciplin konsekvent.
Inga AI-design-creep-mönster identifierades.

#### Fynd 1 — Saknad skip link (Minor)

Severity: Minor

`<header>` innehåller navigation (länk + logga-ut-knapp). Det finns inget skip-to-main-content-element
före headern i DOM-ordning. DESIGN.md §9 / WCAG 2.1 AA kräver skip link på sidor med navigation.

Konkret: lägg till `<a href="#main-content" className="sr-only focus:not-sr-only ...">Hoppa till innehåll</a>`
som allra första barn i `<div className="min-h-full ...">`, och sätt `id="main-content"` på `<main>`-elementet.

Notera: detta är Minor och inte Blocker eftersom det är ett intra-session-beslut om huruvida layout.tsx
räknas som "sida med navigation" — headern är minimal (en länk + ett formulär-knappar). Men WCAG 2.4.1
gäller per definition. Flaggas för nextjs-ui-engineer att åtgärda.

#### Fynd 2 — Logout-knapp utan aria-label (Info)

Severity: Info

Knappen "Logga ut" har synlig textetikett, så `aria-label` är inte strikt nödvändigt. Men knappen
är inbäddad i ett `<form action={logoutAction}>`-element utan `aria-describedby` eller
`name`-attribut på formuläret. Skärmläsare läser "Logga ut" korrekt via knapptexten — inga
åtgärder krävs, men observationen dokumenteras.

#### Fynd 3 — max-w-4xl konsekvent med resten? (Info)

Severity: Info

`max-w-4xl` (896px) används på header-innehållet och main. Marketing-sidan använder `max-w-2xl` (672px),
auth-layout `max-w-sm` (384px). Dessa skiljer sig av designmässiga skäl (marknadsföringssida vs app-shell).
Ingen inkonsistens — app-shell har rimligen en bredare content-container än auth-formulären.
Dokumenteras som Info eftersom ingen specifikation i DESIGN.md §5 anger exakt bredd för app-shell
(spec säger "max-width 1280px på innehåll" och "sidebar 240px" men gäller sidebar-layout, inte
top-nav-layout). Inget att åtgärda.

#### Token-genomgång (godkänt)

- `bg-background` — korrekt (maps till surface-primary via :root)
- `border-border` — korrekt (maps till border-default)
- `bg-surface-secondary` — korrekt, definierad i @theme
- `text-body`, `font-medium`, `text-text-primary` — alla definierade i @theme
- `hover:text-brand-600` — korrekt token
- `text-body-sm`, `text-text-secondary` — korrekt
- Inga hardkodade hex-värden
- Inga Tailwind-defaults som `bg-slate-*`, `text-zinc-*`, `border-gray-*`

#### Civic-utility-disciplin (godkänt)

- Ingen gradient
- Ingen glassmorfism (backdrop-blur etc.)
- Ingen glow
- Ingen `rounded-xl` eller större — `rounded-md` används av Button via buttonVariants
- Ingen emoji
- Ingen AI-accent (violet, purple, indigo, cyan, neon)
- `shadow-sm` och `shadow-md` är de enda tillåtna skuggnivåerna — inga skuggor används alls här, korrekt

---

### src/app/(app)/mig/page.tsx

**Övergripande:** Välskriven Server Component. Korrekt civic-tone, god semantisk struktur med `<dl>/<dt>/<dd>`.
Inga blockers eller majors.

#### Fynd 1 — Dubbel session-kontroll (Info)

Severity: Info

`getServerSession()` + `if (!user) redirect(...)` görs i layout.tsx och upprepas i mig/page.tsx.
Layout.tsx skyddar redan hela (app)-gruppen. Den dubbla kontrollen är defensiv och inte ett designfel,
men det är en observation. Inga åtgärder ur design-perspektiv.

#### Fynd 2 — CardTitle saknar semantisk h-tagg (Minor)

Severity: Minor

`<CardTitle>Kontoinformation</CardTitle>` renderas som en `<div>` (se card.tsx rad 37-47).
På sidan finns `<h1 className="text-h1 ...">Min profil</h1>` korrekt, men card-rubriken
"Kontoinformation" har ingen heading-semantik trots att den visuellt fungerar som en `<h2>`.

Konsekvens: skärmläsare kan inte navigera till card-rubriken via heading-shortcuts.
På en sida med en enda card är detta Minor, inte Blocker — men när sidan växer med fler cards
(framtida "Notifieringar", "Inloggningssessions" etc.) saknar skärmläsar-användare en navigerbar
strukturkarta.

Förslag: lägg till `asChild`-prop eller explicit `<h2>`-wrapper i CardTitle-användningen:
```tsx
<CardTitle><h2 className="text-h3 font-medium">Kontoinformation</h2></CardTitle>
```
Alternativt: CardTitle-primitiven borde renderas som `<h3>` med rätt semantisk nivå. Det är en
komponent-fråga som ligger utanför scope för denna granskning men bör flaggas till nextjs-ui-engineer.

#### Fynd 3 — font-mono saknar token-klass (Minor)

Severity: Minor

Rad 25: `<dd className="text-body text-text-primary font-mono">` — `font-mono` är en Tailwind
utility-klass som refererar till `--font-family-mono`. I globals.css @theme är `--font-mono`
definierat som `'JetBrains Mono', 'SF Mono', Menlo, Consolas, monospace`. Tailwind v4 mappar
`font-mono` till det värdet korrekt, så detta är tekniskt korrekt.

Dock: det finns en token `--text-mono: 13px` i @theme (rad 75 i globals.css) som tycks avsedd
för monospace-text men inte används. `text-body` (14px) används istället. Frågan är om `text-mono`
(13px) borde ha applicerats för Användar-id-fältet.

Bedömning: eftersom `text-mono` inte är definierad som Tailwind utility-klass (det är bara en
storlek-token, inte en kombinerad font+size-token), och `font-mono` + `text-body` ger rätt
visuellt resultat, klassas detta som Info snarare än Minor. Flaggas för eventuell framtida
specifikation av `text-mono`-utility.

Severity nedjusterat: Info.

#### Fynd 4 — "Inga roller tilldelade" — copy-ton (Minor)

Severity: Minor

Rad 36-39: `"Inga roller tilldelade"` är korrekt och neutral svenska, inga utropstecken, ingen emoji.
Dock: texten är en empty state utan nästa steg. För en produktions-UI borde copy vara mer informativ,
t.ex. "Kontakta support om du saknar roller" — men för en profil-debug-vy i Fas 0 är det acceptabelt.
Dokumenteras som Minor med rekommendation att adressera i Fas 1 när rollhantering är implementerad.

#### Token-genomgång (godkänt)

- `text-h1`, `font-medium`, `text-text-primary` — korrekt
- `max-w-lg` — Tailwind-default för bredd, inte en färgtoken. Korrekt användning (max-width är inte
  ett token-system i detta projekt, spacing-tokens gäller padding/gap)
- `text-body-sm`, `text-text-secondary` — korrekt
- `text-body`, `text-text-primary` — korrekt
- `gap-6`, `gap-4`, `gap-1` — korrekt 4px-grid (24px, 16px, 4px)
- Inga hardkodade hex-värden
- Inga Tailwind-färg-defaults

#### Civic-utility-disciplin (godkänt)

- Ingen gradient
- Ingen glassmorfism
- Ingen glow
- Ingen radius-violation — Card använder `rounded-lg` (6px, på gränsen men inom DESIGN.md §5 "lg 6px panels")
- Ingen emoji
- Ingen AI-accent
- Copy: "Min profil", "Kontoinformation", "Användar-id", "E-postadress", "Roller" — korrekt
  civic-ton, inga utropstecken, inga engelska fraser, korrekt svenska

#### Semantisk HTML (godkänt med Minor-observation)

- `<dl>/<dt>/<dd>`-mönster korrekt — bästa semantiken för key-value-par
- `gap-4` på `<dl>`, `gap-1` på varje `<div>` — tydlig typografisk hierarki
- `<h1>` på sidan — korrekt
- `<Card>` utan heading-semantik — se Fynd 2 ovan (Minor)

---

## Konsistens-jämförelse med Turn 1

Turn 1-referensfiler: auth/layout.tsx, logga-in/page.tsx, registrera/page.tsx, marketing/page.tsx

### Spacing-konsistens

| Mönster | Turn 1 (auth) | Turn 2 (app) | Konsekvens |
|---|---|---|---|
| Gap på page-wrapper | gap-8 (auth pages) | gap-6 (mig/page.tsx) | Acceptabel skillnad — app-UI är tätare än auth-UI |
| Padding px | px-6 (auth layout) | px-6 (app layout) | Konsekvent |
| Heading-weight | font-medium | font-medium | Konsekvent |
| Text-tokens | text-h2 (auth), text-h1 (marketing) | text-h1 (mig) | Korrekt hierarki per sida |

### Token-konsistens

Alla referensfiler och de granskade filerna använder samma token-uppsättning:
`text-text-primary`, `text-text-secondary`, `text-body`, `text-body-sm`,
`bg-surface-secondary`, `border-border`, `text-brand-600`. Ingen avvikelse.

### Civic-tone-konsistens

Turn 1-filerna har inga emoji, inga utropstecken, rak svensk copy. Turn 2-filerna följer samma mönster.
Konsekvent genomförd civic-utility-ton i hela frontend-basen hittills.

### Komponent-konsistens

marketing/page.tsx och mig/page.tsx använder båda `Card` + `CardHeader` + `CardTitle` + `CardContent`
med identiskt mönster. Konsekvent.

logga-in/page.tsx använder `text-sm text-text-secondary` (Tailwind-default `text-sm` snarare
än token `text-body-sm` / `text-caption`). Denna Minor-avvikelse från Turn 1 återkommer inte i Turn 2:
mig/page.tsx använder korrekt `text-body-sm`. Turn 2 är faktiskt striktare mot token-systemet än
Turn 1 på denna punkt.

---

## Sammanfattning

Inga blockers. Inga majors.

**Minor-fynd (3 st):**
1. Saknad skip link i layout.tsx — WCAG 2.4.1 (Minor, åtgärda i Fas 1 senast)
2. CardTitle renderas som div utan heading-semantik i mig/page.tsx (Minor, åtgärda när sidan växer)
3. "Inga roller tilldelade" — empty state utan nästa steg (Minor, adressera i Fas 1 med rollhantering)

**Info-fynd (3 st):**
1. Logout-knapp — semantik korrekt, ingen åtgärd
2. max-w-4xl — konsekvent för app-shell, ingen åtgärd
3. font-mono / text-mono-token — tekniskt korrekt, eventuell framtida specning

Filerna följer civic-utility-principen konsekvent. Token-systemet används korrekt genomgående.
Ingen AI-design-creep identifierad. Inga hardkodade färger. Semantisk HTML är i god ordning med
Minor-förbehåll kring CardTitle.

**Mergeklar med reservation:** Minor-fynden bör adresseras av nextjs-ui-engineer före Fas 1-lansering,
inte nödvändigtvis inom denna session.
