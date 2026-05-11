---
review: design-reviewer
datum: 2026-05-11
commit: f2b179a
td: TD-42
status: APPROVE-WITH-FIXES
blockers: 0
major: 3
minor: 2
nits: 0
---

# Design-review: TD-42 touch-target-uppgradering (commit f2b179a)

**Status:** APPROVE-WITH-FIXES
**Granskad:** 2026-05-11
**Reviewer:** design-reviewer
**Auktoritet:** `jobbpilot-design-components` SKILL.md (Input/Select 36px default),
`jobbpilot-design-a11y` SKILL.md §9 (44×44 hit-area, lg=h-11),
`jobbpilot-design-principles` SKILL.md (civic-utility-densitet,
GOV.UK/Stripe Dashboard-referens)

---

## Sammanfattning

Commit stänger en genuin doc/kod-drift. Skill-docs har specat `h-9` som
default i månader; koden låg på `h-8`. Inte estetisk justering — kod
kommer ikapp authoritative specen. Civic-utility-känslan består:
GOV.UK, Stripe Dashboard och 1177 använder alla 36–40px primary controls
på desktop. h-9 (36px) är slankare än Material (48px) men funktionellt
över h-8 för mus, motorisk funktionsnedsättning och touch — netto
a11y-vinst utan att tappa täthet.

**0 Blockers. 3 Major-fynd** i call-sites (inga i primitives) — 2
fixade in-block per 4h-regel, 1 lyft som TD-57.

---

## Blockers

Inga.

---

## Major

### M1. Felmatchad button-höjd i `/ansokningar/ny`-formulär-actions

- **Plats:** `app/(app)/ansokningar/ny/page.tsx:46-48`
- **Före commit:** submit h-9 + Avbryt `size="sm"` h-7 (8px gap, var 4px innan TD-42)
- **Motsvarande sida** `/cv/ny:60` har redan `<Button asChild variant="ghost">` utan size (h-9 + h-9) — korrekt pattern.
- **Bedömning:** Paired primary + secondary actions i samma rad ska visuellt matcha höjd.
- **In-block-fix tillämpad:** ta bort `size="sm"` på Avbryt-knappen. Båda nu h-9.

### M2. Page-header CTA-höjd inkonsekvent med form-submits

- **Platser:**
  - `app/(app)/ansokningar/page.tsx:40` — "Ny ansökan" `<Button asChild size="sm">`
  - `app/(app)/cv/page.tsx:24` — "Nytt CV" `<Button asChild size="sm">`
- **Pre-existing inkonsekvens** som blev mer synlig efter h-9-default:
  - Page-header CTA: h-7 (28px)
  - Form submit: h-9 (36px)
  - Critical CTA (size="lg"): h-11 (44px) — oanvänd
- **Bedömning:** h-7 = 28px är under WCAG 2.5.5 AA (24×24 undantag för inline-text).
  Som primary CTA i page-header bryter h-7 mot principen "size-variation signalerar
  hierarki, inte slumpas".
- **In-block-fix tillämpad:** ta bort `size="sm"` på båda page-header CTAs.
  Nu default (h-9), matchar form submits.

### M3. Native datetime-local + native select divergerar från Input-primitive

- **Plats:** `app/components/applications/add-follow-up-form.tsx:60` (datetime-local)
  + `app/components/me/me-profile-form.tsx:116` (native language-select)
- **Divergence från Input-primitive:**

| Attribut | Input-primitive | Native datetime-local |
|---|---|---|
| `rounded-*` | `rounded-sm` | `rounded-md` |
| `py-*` | `py-1` | `py-2` |
| `text-*` | `text-base md:text-sm` | `text-sm` |
| aria-invalid-styling | ✓ | saknas |
| dark-mode-styling | ✓ | saknas |
| disabled bg-färg | `disabled:bg-input/50` | saknas |

- **Inte regression från TD-42** — divergence pre-existed. Commit synliggör
  problemet eftersom skill-doc:en nu är auktoritativ för field-height.
- **Lyft som TD-57** (separat, kräver design-beslut om wrapper-strategi —
  >4h scope per 4h-regel).

---

## Minor

### m1. Visuell hierarki default → lg (8px gap, var 4px)

Bedömning: **inte ett problem**. Skill-doc:en spec:ar uttryckligen att
lg = `h-11 = 44px for critical CTAs` — gapet ska vara distinkt. Det
gamla h-8/h-9 (4px) var i praktiken visuellt identiskt.

Ny skala är symmetrisk runt default + monoton:

| Variant | Höjd | Avstånd från default |
|---|---|---|
| xs | h-6 (24px) | -12px |
| sm | h-7 (28px) | -8px |
| default | h-9 (36px) | — |
| lg | h-11 (44px) | +8px |

Ingen åtgärd.

### m2. button.tsx lg-padding ändrade px-2.5 → px-3

Inte nämnt i scope eller commit-message, men konsekvent med skill-doc:s
pattern att lg ska kännas mer "vägd". 2px på h-11-knapp är subtilt och
förbättrar proportionen. OK som tyst förbättring. Ingen åtgärd.

---

## Bra gjort

- Skill-doc:en var auktoritativ; kod alignar nu med doc — rätt riktning
- Bevarade xs/sm/icon-xs/icon-sm för dense-context — disciplinerad
  förståelse av civic-utility-densitet (inte "alla knappar måste vara 44px")
- Commit-message motiverar trade-off mot WCAG 2.5.5 AAA explicit med
  Stripe/GOV.UK-referens
- Tester 150/150 oförändrade — bekräftar att ändringen är ren CSS

---

## Sammanfattning av åtgärder

| Fynd | Severity | Status |
|---|---|---|
| M1 — `/ansokningar/ny` Avbryt-knapp h-7 | Major | ✓ In-block-fix tillämpad |
| M2 — page-header CTAs h-7 (× 2) | Major | ✓ In-block-fix tillämpad |
| M3 — native form-controls divergerar | Major | Lyft som TD-57 |
| m1, m2 | Minor | Ingen åtgärd (acceptabla) |

TD-42 är **stängd**. M1+M2 fixade in-block per 4h-regel. M3 deferrad
via TD-57 (kräver design-beslut + flera filer = >4h).
