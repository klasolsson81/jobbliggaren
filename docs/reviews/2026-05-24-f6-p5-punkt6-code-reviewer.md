# Code-review: F6 P5 Punkt 6 — page-hero + brand-empty-states

**Status:** APPROVED
**Granskad:** 2026-05-24
**Auktoritet:** CLAUDE.md §4 (TS/Next.js) + §5.2 (FE anti-patterns)
**Scope:** Frontend — 5 filer (CSS + 3 sidor + app-shell)
**Spec:** `docs/jobbpilot-v3-bundle/HANDOVER-v4.md` (Klas-godkänd 2026-05-24)

## Findings

Inga Blockers, inga Major.

### Minor (FYI, ej blockerande)

1. **CSS-duplikering: `:focus-visible`-override mellan `.jp-pagehero` och `.jp-empty--brand`**
   Filer: `app/globals.css:1053-1056` + `:2345-2348`
   Båda blocken har identisk regel: `outline: 2px solid #fff; outline-offset: 2px;`. Specen (HANDOVER-v4 §4.2) flaggade detta som "egen judgement" för pagehero — CC har valt att applicera det på båda navy-zoner, vilket är konsekvent men dupliceras. Ej Major (regeln är trivial och kontextuellt scopad), men kan konsolideras senare via gemensam selektor (`.jp-pagehero :focus-visible, .jp-empty--brand :focus-visible { ... }`).

## Bra gjort

- **Server Components genomgående:** alla tre sidor (`/oversikt`, `/ansokningar`, `/cv`) är RSC. Inga `"use client"`-direktiv tillagda. CLAUDE.md §4.3 respekterad.
- **Inga anti-patterns ur §5.2:** ingen `any`, ingen `useEffect` för data, ingen emoji, inget utropstecken, inga hårdkodade strängar utöver design-token-värden i CSS.
- **A11y i kod:** `aria-hidden="true"` på alla dekorativa Lucide-ikoner (`Plus`, `Search`). H1 är enda H1 per sida (`.jp-pagehero__title`). Kicker är `<div>` (inte semantisk heading) — korrekt, då det är typografisk dekoration, inte hierarkisk landmark.
- **Fragment-användning:** `<>...</>` används där en wrappande `<div>` skulle vara meningslös semantik (`/ansokningar`, `/cv`). Oversikt använder explicit `<>` också. Korrekt.
- **app-shell `V3_NATIVE_ROUTES`-tillägg motiverat:** `/cv` har nu egen `.jp-container jp-page` och kvalificerar för opt-out från `.jp-shell-transitional-container`. Kommentaren i `app-shell.tsx:56-67` förklarar varför listan finns + borttagnings-trigger (när F3/F5/F6 är klara) — perfekt branch-by-abstraction-disciplin.
- **Kommentarer motiverade (WHY, ej WHAT):** "F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.x)" pekar på spec-källan. CSS-kommentarerna förklarar varför `:focus-visible`-override behövs (WCAG 2.4.7 mot mörk navy). Inga gratulationskommentarer.
- **CSS-tokens:** alla nya regler refererar `--jp-hero-bg`, `--jp-hero-ink`, `--jp-hero-ink-soft`, `--jp-r-md`, `--jp-font-mono`. Inga nya färg-literals utanför `#fff` + `#EAF1FA` + `#08213F` som specen explicit kräver för hover/contrast på vit knapp mot navy.
- **TypeScript strict:** inga `as`-casts, inga implicit returns, inga `!`-suppressioner utan motivering.
- **Spec-trohet:** CSS-blocken är verbatim mot HANDOVER-v4 §2.1, komponent-snippets matchar §2.2–§2.4 nästan ord-för-ord (Oversikt har extra `kickerName`-fallback från befintlig logik — korrekt bevarad).

## Sammanfattning

Ren FE-uppdatering. Spec följd, inga anti-patterns, a11y intakt. **Mergeklar.**
