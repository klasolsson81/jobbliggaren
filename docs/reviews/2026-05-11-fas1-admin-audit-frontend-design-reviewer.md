# Design-review: Fas 1 admin-granskning (audit-log frontend)

**Status:** Approved with Minor changes
**Granskat:** 2026-05-11
**Auktoritet:** DESIGN.md §1, §3, §4, §5, §6, §9 + skills `jobbpilot-design-{principles,tokens,components,copy,a11y}`

## Verdict-sammanfattning

| Severity | Antal |
|---|---|
| Blocker | 0 |
| Major | 0 |
| Minor | 3 |
| Nit | 2 |

## Svar per scope-fråga

1. **Civic-utility-ton:** ✓ 1177/Digg/GOV.UK-style. Inga gradients, glows, hover-shimmer, zebra rows.
2. **Token-discipline:** ✗ Ett brott — `border-danger-200` saknas i `@theme` (Mi2).
3. **Svensk copy:** ✓ Rak, konkret, inga emoji/utropstecken. Datum-format YYYY-MM-DD HH:mm:ss matchar §10.2.
4. **Table-design:** ✓ Semantiskt korrekt. Monospace för UUID/IP/datum är pragmatic-civic, inte Linear.
5. **Filter-form (native HTML GET):** ✓ GOV.UK-pattern. Zero JS för core-flöde.
6. **Tom-state:** ✓ Konstatering + konkret nästa steg.
7. **Error-state:** ✓ Distinkta meddelanden per kind. Inga "Något gick fel".
8. **Pagination prev/next:** ✓ Räcker för Fas 1. Inte regression mot DESIGN.md §6.
9. **A11y:** ✓ aria-labels, scope="col", caption, role="alert"/"status", aria-disabled på disabled spans.
10. **Layout-bredd `max-w-6xl` admin vs `max-w-4xl` app:** ✓ Motiverat (admin = datatäta tabeller, Stripe Dashboard-pattern).

## Minors

### Mi1: `text-text-tertiary` på empty-state sekundärtext bryter WCAG AA
**Fil:** `audit-log-table.tsx:20`

`text-text-tertiary` (#8A8A85) på `bg-surface-secondary` ≈ 2.9:1 → AA kräver 4.5:1.

**Fix:** Byt till `text-text-secondary` (#5A5A5A = ~6.0:1).

**OBS:** Samma fel finns i `(app)/ansokningar/page.tsx:48` — replikerat pattern. Skapa följd-TD eller bunta med TD-42.

### Mi2: `border-danger-200` ej i `@theme` — tailwind-fallback
**Fil:** `page.tsx:98`

`globals.css` har danger-50/600/700 men inte danger-200. Tailwind v4 fallbackar till default-palette → token-brott.

**Fix-alternativ:**
- A: `border-danger-600/30` (minst invasiv, konsekvent palette-svit)
- B: `border-border` (neutral border)
- C: Lägg till `--color-danger-200: #F4C4C4;` i @theme (kräver DESIGN.md update)

**Rekommenderat:** A.

### Mi3: "Använd filter" → "Filtrera" (verb-objekt-disciplin)
**Fil:** `audit-log-filter.tsx:85`

GOV.UK/1177-precedent. Acceptabelt som-är, men "Filtrera" är kortare och starkare. Klas-val.

## Nits

### Nit1: `<table aria-label>` + `<caption>` är duplicerat
**Fil:** `audit-log-table.tsx:29-32`

Behåll `<caption>` (mer informativ), ta bort `aria-label`. Testet `audit-log-table.test.tsx:60-62` måste uppdateras till regex `/^Granskningsposter sorterade/`.

### Nit2: Admin-layout max-width-val saknar inline-kommentar
**Fil:** `(admin)/layout.tsx:24, 49`

Lägg till kommentar om medveten design (admin = datatäta tabeller).

## Praise

- Server Components default genomgående — zero `"use client"` i scope.
- URL-driven state via searchParams — bookmarkbar, browser-back fungerar.
- Empty-state-pattern konsekvent med `(app)/ansokningar`.
- Layout-spegling mellan `(admin)/layout.tsx` och `(app)/layout.tsx`.
- Distinkta error-meddelanden per kind (forbidden/unauthorized/error).
- Pure functions för konvertering (toIsoOrUndefined, toLocalInput, shortId, formatDateTime).
- Native `datetime-local` istället för custom date-picker.
- Semantisk tabell-struktur (scope="col", caption, th).
- Inga AI-clichér.

## Verdict

**Approved.** Mergeklar. Mi1+Mi2 rekommenderas tas in i nästa touch-up — Mi1 är konsistens med befintlig app-yta (samma fel finns i `(app)/ansokningar`), Mi2 är ensamt token-brott i scope.
