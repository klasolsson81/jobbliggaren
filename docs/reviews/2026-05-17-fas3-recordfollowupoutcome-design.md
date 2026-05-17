# Design-reviewer — FAS 3 RecordFollowUpOutcome frontend (in-block)

**Datum:** 2026-05-17
**Agent:** design-reviewer (initial aa38616d8bccdd7cd → re-review aece8eb869c885df1)
**Scope:** `record-follow-up-outcome-form.tsx` (ny), `add-follow-up-form.tsx` (in-block-fix), `[id]/page.tsx`, `status.ts`.

## Status: APPROVED (kod-nivå) — 0 Block / 0 Major / 1 Minor

### Initial review: CHANGES-REQUESTED (2 Major, 3 Minor)
- M1: felmeddelande ej aria-kopplat (a11y).
- M2: `text-danger-600` ska vara `text-danger-700` (dark-mode-kontrast).
- m1: label "Registrera utfall" → "Utfall". m2: `variant="outline"` → civic-utility-variant. m3: date-fns/tid (utanför scope).

### Åtgärdat in-block (båda formulären, §9.6-direktiv) → re-review APPROVED
- **M1 löst:** `record-follow-up-outcome-form.tsx` — `errorId` stabilt per followUpId, `aria-invalid`/`aria-describedby` conditional på SelectTrigger, `role="alert"` + `id` på felmeddelande. `add-follow-up-form.tsx` — form-level-fel: `role="alert"` bekräftat tillräckligt (per-fält-koppling vore vilseledande för server-action-form-level-fel).
- **M2 löst:** `text-danger-700` båda filer (light #B91C1C, dark #FECACA — kontrast OK båda lägen).
- **m1/m2 löst:** label "Utfall"; `variant="secondary"` (verifierat i button.tsx:15-16).
- **m3 medvetet kvar:** cross-cutting lokaliserings-touch, utanför PR-scope.

## VETO-villkor (kvarstår — Fas 3-stängnings-gate, EJ push-blocker)
Rendered-screenshot-granskning i light + dark krävs **innan Fas 3-stängning** (deploy, `pnpm visual-verify`, design-reviewer granskar, Klas godkänner — identiskt med Fas 2-precedensen). Kod-nivå-approval ≠ fas-stängnings-approval. Push av batchen ej blockerad.
