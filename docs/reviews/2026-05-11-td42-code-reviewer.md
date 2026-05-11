# Code-review: TD-42 touch-target-uppgradering (commit f2b179a)

**Status:** APPROVE
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §4 (TypeScript/Next.js), §5.2 (FE anti-patterns),
jobbpilot-design-components SKILL.md, jobbpilot-design-a11y SKILL.md §9
**Scope:** Frontend — 5 filer, Tailwind class-byte (h-8→h-9, h-9→h-11,
size-8→size-9, size-9→size-11). Ingen ny logik, ingen ny dependency.

---

## Verifieringspunkter

### 1. Komponent-tester (Vitest 150/150)
Verifierat: inga tester i `web/jobbpilot-web/` refererar till `h-8`, `h-9`,
`h-11`, `size-8`, `size-9` eller `size-11`. Class-byte triggar inga
test-assertions. 150/150-pass är konsistent.

Inga unit-tests existerar för button/input/select-primitives i `src/components/ui/`.
Det är inte en TD-42-fråga (primitives har varit otestade sedan shadcn-init),
men noteras under "Praise/observations" nedan.

### 2. Skill-doc-konformitet
Före commit (h-8 = 32px default) drev kod-baseline från docs. Efter commit:

| Element | Skill-doc | Kod efter commit | Match |
|---|---|---|---|
| Input/Textarea/Select default | h-9 (36px) | h-9 | ✓ |
| Input dense context | h-8 (32px) | bibehållen via data-[size=sm]:h-7 i select | ✓ (sm-variant ersätter h-8) |
| Button default | h-9 (36px) | h-9 | ✓ |
| Button lg (critical CTA) | h-11 (44px) | h-11 | ✓ |
| Touch target (WCAG 2.5.5) | 44×44 px | h-11 för critical CTAs, h-9 default + civic-utility-kompromiss | ✓ (medveten skill-doc-policy) |

### 3. Proportionell sub-komponent-justering
`input.tsx` ändrar `file:h-6` → `file:h-7`. Verifierat: input-fältets nya
höjd är 36px (h-9), file-input-knappen är 28px (h-7) — kvarstår
~8px clearance ovanför/under, matchar tidigare proportion (28/4 av 32 → 28/4
av 36). Acceptabel proportion.

### 4. Inkonsekvenser mellan UI-primitives och native form-element
| Fil | Element | Höjd | Radius | vs. Input-primitive |
|---|---|---|---|---|
| `components/ui/input.tsx` | `<input>` | h-9 ✓ | rounded-sm | (baseline) |
| `components/ui/select.tsx` | trigger | data-[size=default]:h-9 ✓ | rounded-lg | (skill-policy: select ≠ input) |
| `components/me/me-profile-form.tsx:111` native select | `<select>` | h-9 ✓ | rounded-sm | matchar Input ✓ |
| `components/applications/add-follow-up-form.tsx:54` native datetime-local | `<input>` | h-9 ✓ | rounded-md ⚠ | matchar inte Input (rounded-sm) |

`add-follow-up-form.tsx` använder `rounded-md` på native datetime-local,
medan `Input`-primitiven använder `rounded-sm`. Detta är **pre-existing
drift** (commit ändrade bara h-8 → h-9), men commit-meddelandet säger
"alignerar kod med skill-doc-defaults" — radius-drift är då i scope och
borde fixats samtidigt. Se Minor 1.

### 5. CLAUDE.md §4 + §5.2-konformitet
Inget i diff:en bryter mot konventionerna:
- Inga `any`-typer (diff är ren Tailwind-class-text)
- Inga `useEffect` för datahämtning
- Inga `console.log`
- Inga gradients, drop shadows > shadow-sm, glow-effekter, glas-morphism
- Inga radius > 6px utom rounded-pill (datetime-local har rounded-md = 4px)
- Inga `localStorage` för känslig data
- Inga hårdkodade UI-strängar (diff rör inte copy)
- Inga emoji, inga utropstecken
- Inga DOM-manipulationer introducerade (pre-existing
  `document.getElementById(...)?.focus()` i me-profile-form rör inte denna PR)

### 6. Tailwind h-11 = 44px verifiering
`postcss.config.mjs` använder default `@tailwindcss/postcss`. `globals.css`
overrride:ar inte `--spacing`-skalan. Tailwind v4 default: `--spacing: 0.25rem`
(4px). Därför:
- `h-9` = 0.25rem × 9 = 2.25rem = **36px** ✓
- `h-11` = 0.25rem × 11 = 2.75rem = **44px** ✓
- `size-9` = 36×36px ✓
- `size-11` = 44×44px ✓

WCAG 2.5.5 (AAA) touch-target-minimum 44×44px uppnås exakt för h-11/size-11.
Civic-utility-kompromissen h-9 (36px) som default är medveten skill-doc-policy
(jobbpilot-design-a11y SKILL.md §9 + civic-utility-densitet) och accepteras.

---

## Blockers
Inga.

## Major
Inga.

## Minor

1. **Native datetime-local i add-follow-up-form använder rounded-md, inte rounded-sm**
   Fil: `web/jobbpilot-web/src/components/applications/add-follow-up-form.tsx:60`
   Nuvarande: `className="flex h-9 w-full rounded-md border border-input ..."`
   Föreslås: `rounded-md` → `rounded-sm` för att matcha `Input`-primitiven
   (skill-doc: "Radius: rounded-sm (2px) — inputs are tighter than buttons").
   Motivering: pre-existing drift, men commit-meddelandet säger "alignerar
   kod med skill-doc-defaults". Höjden är nu konsistent (h-9), radius är
   inte. Bör fixas in-block (TD-42 är "touch-target-uppgradering till
   skill-doc-defaults" — radius är en närliggande skill-doc-default).
   Alternativ: ersätt native `<input type="datetime-local">` med
   `<Input type="datetime-local">` för att få alla primitive-defaults
   gratis. Det skulle också eliminera duplicering av focus-ring + disabled-
   states som idag dubblas mellan input.tsx och denna inline-className.
   Delegera till: nextjs-ui-engineer (4h-regeln §9.6: in-block-fix, scope
   <30 minuter).

## Nit

1. **Button lg-variant ändrade samtidigt px-2.5 → px-3**
   Fil: `web/jobbpilot-web/src/components/ui/button.tsx:28`
   Commit-meddelandet nämner inte padding-ändringen, bara höjd. Padding
   gick från `px-2.5` (10px) till `px-3` (12px) för lg-varianten. Ingen
   site i kodbasen använder `size="lg"` idag (verifierat via grep),
   så ingen regression — men commit-message-precision lider. Förslag:
   nästa gång, lista alla class-ändringar i commit-body för audit-trail.
   Inte blockerande.

## Praise / observations

- **Rätt scope-disciplin:** commit-message dokumenterar bevarade dense-
  context-varianter (xs/sm/icon-xs/icon-sm/select sm) som medvetet
  beslut — inte "missade", utan policy. Detta är exakt den typ av
  documentation-of-intent som JobbPilot-civic-utility-tonen kräver.
- **Korrekt checkbox-resonemang:** `size-4` checkboxes bibehållna med
  motivering "items-start + gap-3 + cursor-pointer på label ger
  funktionell hit-area genom hela rad". Verifierat i me-profile-form
  rad 127-144: row-pattern är korrekt implementerat. Detta är giltigt
  WCAG 2.5.5-mönster (target inkluderar label-text-area).
- **WCAG-policy-transparens:** commit-message förklarar varför h-9
  default + h-11 critical CTAs är civic-utility-kompromiss mot AAA
  44×44 — refererar till Stripe Dashboard / GOV.UK desktop-utility-
  pattern. Detta är rätt nivå av designdokumentation för en class-byte.
- **Inga regressions-risker:** ingen användning av `size="lg"`,
  `size="icon-lg"` eller `size="icon"` finns i kodbasen idag —
  uppgraderingen är förebyggande för framtida bruk.
- **Proportionell file:-höjd-justering:** att tänka på sub-elementets
  proportion (file:h-6 → file:h-7) när container ändras (h-8 → h-9)
  är detaljnivå som ofta missas i Tailwind-byten.

### Observation utanför scope (inte denna PR)
- Inga unit-tester för button/input/select-primitives i `src/components/ui/`.
  Dessa är shadcn-baserade och har varit otestade sedan init. Inte en
  TD-42-fråga. Möjligt nytt TD om vi vill snapshot-testa varianter eller
  rendering-stabilitet. Inte blockerande, inte delegerat — bara noterat.

---

## Sammanfattning

0 blockers, 0 major, 1 minor, 1 nit.

**Status: APPROVE-WITH-FIXES** rekommenderas formellt, men eftersom Minor 1
är pre-existing drift (radius-inkonsekvens på native datetime-local) och inte
introducerad av TD-42, klassas commit:en som **APPROVE** med rekommendation
att lyfta radius-inkonsekvensen som följd-arbete i samma "skill-doc-defaults"-
tema. Klas avgör om Minor 1 ska bli en in-block-fix på denna commit eller en
separat TD.

**Bevisning:**
- Skill-doc-konformitet uppnådd för höjder (verifierat med grep)
- Tailwind h-11 = 44px verifierat mot postcss + globals.css (ingen
  spacing-override)
- Inga regressions-risker (size="lg" oanvänt i kodbasen)
- CLAUDE.md §4 + §5.2 intakta
- Vitest 150/150 oförändrade (verifierat att inga tester gör class-baserade
  assertions på de ändrade klasserna)

**Delegationer:**
- Minor 1 (radius på datetime-local) → nextjs-ui-engineer in-block, eller
  separat TD om Klas väljer scope-bevarande

Re-review behövs inte om Minor 1 fixas i en följd-commit med tydlig
"refactor(web): align native datetime-local radius with Input primitive"-
beskrivning, eller om det lyfts som TD.
