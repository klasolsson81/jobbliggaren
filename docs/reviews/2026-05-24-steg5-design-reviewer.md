# Design-review: Steg 5 — Closed beta-disciplin (/vantelista)

**Status:** ⚠ Changes requested → Re-review after fixes
**Granskat:** 2026-05-24
**Agent:** design-reviewer
**Auktoritet:** DESIGN.md §3 (färgsystem), §6 (komponenter), §8 (copy), §9 (a11y) + jobbpilot-design-tokens/-components/-a11y/-copy
**Manifest:** FAS-DEFERRAL — pre-screenshot. Rendered-veto endast där DOM/CSS-disciplin är glasklart bruten. Visual-verify kvarstår som STOPP C hos Klas.

## 0 Blockers

Inga. Civic-utility-disciplinen intakt på de stora axlarna: `.jp-pagehero`-återanvändning, inga AI-aesthetics, korrekt form-a11y-grund, `<fieldset>` + `<legend>` runt consent-gruppen, 308-redirect utan UI.

## 3 Major

### M1. `bg-surface-2` är inte registrerad som Tailwind-token

`--color-surface-2` finns inte i `@theme inline`. Tailwind genererar ingen utility, klassen resolveras till intet. Success-state-blocket renderas utan bakgrund (transparent).

**FIXAD in-block 2026-05-24** — bytt till `bg-surface-secondary` (samma värde via registrerad alias).

### M2. Native `<input type="checkbox">` med Tailwind-leakage

Tre problem: `rounded` utan suffix, ingen `accent-color`, inget synligt focus-ring.

**FIXAD in-block 2026-05-24** (Alt A) — `rounded-sm border-border accent-brand-600 focus-visible:outline-2 focus-visible:outline-brand-600 focus-visible:outline-offset-2`.

### M3. Email-hint "Formatet är namn@domän.se" — placeholder-anti-pattern

Redundant med `type="email"` + label. Civic-tight = ta bort.

**FIXAD in-block 2026-05-24** — `<p id="email-hint">` borttagen.

## 5 Minor

### m1. `text-sm` på error-text bryter token-konsekvens

**FIXAD in-block 2026-05-24** — bytt till `text-body-sm`.

### m2. Footer-länk kontrast — acceptabel som-är

Markeras inte som fix.

### m3. `<h2 id="form-heading" className="sr-only">` duplicerar `<h1>`-text

**FIXAD in-block 2026-05-24** — `<h2 sr-only>` borttagen. `<section aria-labelledby="vantelista-heading">` pekar nu på `<h1>` direkt.

### m4. Submit-button saknar `aria-busy` under pending-state

**FIXAD in-block 2026-05-24** — `aria-busy={isPending}` tillagd.

### m5. `defaultValues: { policiesAccepted: true, cookiesAccepted: true }` bryter GDPR Art. 7(1) — eskaleras till CTO

**Pre-ifyllt samtycke är inte fritt och informerat samtycke** (Art. 7(1) + Recital 32). Två alternativ:

- **A.** Ersätt med disclaimer-text under submit-knappen ("Genom att skicka in godkänner du..."). Marketing förblir checkbox.
- **B.** Behåll tre checkboxar men ändra defaults till `false`.

**Eskalerat till senior-cto-advisor för triage 2026-05-24.**

## 6 Praise

1. `.jp-pagehero`-återanvändning verbatim
2. Navy hero + vit form-yta — papper-metafor
3. 308 redirect utan UI — exemplarisk disciplin
4. Microcopy "inga datum lovas" — civic-rak
5. Success-state-copy 1177-paritet
6. `<fieldset>` + `<legend>` runt consent-gruppen

## Sammanfattning

**3 Major + 5 Minor.** M1-M3 + m1/m3/m4 alla fixade in-block 2026-05-24. m5 (consent-defaults) eskalerad till CTO-triage. m2 acceptabel.

**Visual-verify-blocker (Klas STOPP C):**
- Light + dark mode
- Lighthouse a11y ≥ 95 på `/vantelista`
- axe DevTools 0 violations
- Tabb-ordning visuellt = DOM-ordning
