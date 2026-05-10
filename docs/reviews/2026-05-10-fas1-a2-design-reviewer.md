# Design-review: Fas 1 Block A (sub-block A2) — Profil-edit-yta

**Granskat:** 2026-05-10
**Auktoritet:** DESIGN.md, skills `jobbpilot-design-{principles,tokens,a11y,components,copy}`
**Skills konsulterade:** principles, tokens, a11y, components, copy
**Filer:**
- `web/jobbpilot-web/src/components/me/me-profile-form.tsx`
- `web/jobbpilot-web/src/app/(app)/mig/page.tsx`
- `web/jobbpilot-web/src/lib/actions/me-schemas.ts`
- `web/jobbpilot-web/src/lib/actions/me.ts`
- `web/jobbpilot-web/src/lib/api/me.ts`
- `web/jobbpilot-web/src/lib/types/me.ts`

---

## Sammanfattning

Implementationen är civic-utility-paritetisk. Inga AI-aesthetics, inga
hardcodade hex, inga emojis, inga utropstecken. Native `<select>` + native
checkbox är ett legitimt val för Fas 1, men ett faktafel i implementation-
noteringen behöver rättas: **shadcn `Select` finns redan i
`components/ui/select.tsx`**. Det är inte en blocker i sig — native är
acceptabelt och fungerar — men avgörandet att inte använda befintlig
komponent bör vara medvetet, inte byggt på fel premiss.

A11y-mönster är solid (fieldset/legend, htmlFor-koppling, aria-invalid,
aria-describedby, role="alert" + role="status", focus-flytt). Copy är
inom civic-tonen. Token-användningen är genomgående korrekt —
`accent-primary` mappar via `@theme inline` mot `--primary` (#0B5CAD
brand-600) och är ett giltigt token.

---

## Fynd

### Major

**M1. Implementation-notering felaktig — shadcn Select finns**

Fil: `web/jobbpilot-web/src/components/ui/select.tsx` (existerar, 193 rader)

Implementation-noteringen säger "ingen Select-komponent finns i `ui/`".
Det är fel — en fullskalig Radix-baserad Select är installerad. Native
`<select>` är fortfarande ett legitimt civic-utility-val (GOV.UK gör samma),
men beslutet ska dokumenteras eller revideras innan A3. Rekommendation:
lägg en kommentar i komponenten ("Native select medvetet — 2 opt, Radix
Select overkill") eller byt till shadcn Select för konsekvens med övriga
formulär.

**M2. Custom select dubblerar Input.tsx-klasser inline**

Fil: `me-profile-form.tsx:130`

~110 tecken Tailwind copierat från `Input.tsx`. När `Input.tsx` uppdateras
driftar selecten. Antingen extrahera till `ui/native-select.tsx`-primitiv
eller använd shadcn `Select`. Inte blocker, men teknisk skuld från dag ett.

### Minor

**Mi1. Touch target under WCAG 2.5.5 (44×44)**
- Select: `h-8` (32px)
- Checkbox: `size-4` (16px) — `gap-3` + `mt-1` runt ger inte 44px hit-area
- Save-knapp: default size `h-8` (32px)

Detta är **projektgemensamt mönster** (Input/Button är `h-8`), inte
A2-introducerad regression. Höj till skill-nivå: överväg att höja default
button/input till `h-9`/`h-10` projektbrett. Inte blocker för A2, men note
för Klas att hela komponentbiblioteket sitter under 44px-tröskeln.

**Mi2. Loading-microcopy använder `...` istället för `…`**

Fil: `me-profile-form.tsx:181`  → `"Sparar..."`

Per `jobbpilot-design-copy` §4: ellipsis ska vara Unicode `…`. Ändra
till `"Sparar…"`. Trivialt fix.

**Mi3. Tidsstämpel-format inkonsekvent med locale-spec**

Fil: `me-profile-form.tsx:185` → `Sparat {savedAt.toLocaleTimeString("sv-SE")}.`

`.toLocaleTimeString("sv-SE")` ger `14:32:08` (inkl sekunder). Per copy-spec
används `14:32` (utan sekunder). Fix:

```ts
savedAt.toLocaleTimeString("sv-SE", { hour: "2-digit", minute: "2-digit" })
```

**Mi4. Felmeddelande använder generisk "Försök igen."**

Fil: `me.ts:56` → `"Kunde inte nå servern. Försök igen."`

Per copy §3: "Försök igen" utan kontext är vag. Föreslagen text:
`"Kunde inte nå servern. Kontrollera din nätverksanslutning."`

**Mi5. Empty/error-state för misslyckad profilhämtning är passivt**

Fil: `mig/page.tsx:58-60`

`"Kunde inte hämta din profil. Försök ladda om sidan."` är OK men saknar
konkret action-knapp. Ej blocker — re-load är en explicit instruktion —
men en `<Button onClick={...}>Ladda om</Button>` skulle matcha
Alert-mönstret från `jobbpilot-design-components`.

**Mi6. `<legend>` styled som hjälptext, inte rubrik**

Fil: `me-profile-form.tsx:138` → `text-body-sm text-text-secondary`

Legend "Notifieringar" är en sektionsrubrik. `text-label` (13px/500) eller
`text-body` (14px/400) i `text-text-primary` är mer korrekt än secondary-grå.
Tonar ner gruppens semantik visuellt. Funktionellt OK för screen reader.

### Nit

**N1.** `disabled:opacity-50` på checkbox — `accent-primary` + opacity
stackar olika i Safari vs Chromium. Acceptabelt för Fas 1.

**N2.** `cursor-pointer` på Label — bra UX-touch, men `<label htmlFor>`
ger redan native cursor-svar. Klassen är redundant men inte fel.

**N3.** Type-cast `parsed.data as UpdateMyProfileInput` —
`me-profile-form.tsx:96` — Zod `.infer<>` ger redan exakt typen, casten
är onödig.

---

## Bedömning per fråga

1. **Civic-utility-paritet:** Ja. Native select + native checkbox passar
   civic-tonen — GOV.UK och 1177 använder native primitives där de räcker.
   `accent-primary` är rätt token (brand-600). Inga tokens bryts.

2. **WCAG 2.1 AA:** `fieldset/legend` + `htmlFor`-koppling räcker för
   SR-gruppering. Focus-flytt fungerar för booleans
   (`document.getElementById(...)?.focus()` på native
   `<input type="checkbox">` är fullt supportat). Touch-target-frågan är
   projektbred (Mi1), inte A2-specifik.

3. **Komponent-mönster:** Native acceptabelt för Fas 1, men
   **kontradikterar implementation-note (M1)** — shadcn Select finns redan.
   Lyft beslutet till medvetet val eller byt.

4. **Copy:** Inom tonen. Tre minor fixar (Mi2/Mi3/Mi4) — ellipsis Unicode,
   tidsformat utan sekunder, mer specifik nätverksfel.

5. **Layout (2 kort, max-w-lg):** Korrekt civic-utility. Single-column,
   ingen hero, ingen grid-gymnastik. `max-w-lg` (32rem ≈ 512px) ger optimal
   lästextbredd.

---

## Approve-status

**Approved med villkor:** Mi2 + Mi3 + Mi4 fixas in-block (5-minuters-jobb).
M1/M2 öppnas som tech-debt-noteringar och beslutas innan A3 — antingen
dokumentera native-valet eller migrera till shadcn Select. Inga Blockers,
inga regressioner mot DESIGN.md eller WCAG AA.

---

## Föreslagna in-block-fixar

```tsx
// me-profile-form.tsx:181
{isPending ? "Sparar…" : "Spara profil"}

// me-profile-form.tsx:185
Sparat {savedAt.toLocaleTimeString("sv-SE", { hour: "2-digit", minute: "2-digit" })}.

// me.ts:56
return { success: false, error: "Kunde inte nå servern. Kontrollera din nätverksanslutning." };
```

Mi6 (legend-styling): byt `text-body-sm text-text-secondary` →
`text-label text-text-primary`.

M1/M2 → öppnas som TD-rader (Select-konvention + drift-risk).
Mi1 → öppnas som TD-rad (touch-target projektbrett).
Mi5 → defererad eller TD om tydligt scope.
