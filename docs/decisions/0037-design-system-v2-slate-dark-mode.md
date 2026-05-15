# ADR 0037 — Designsystem v2: civic slate-skala + dark mode (ersätter Fas 0-borttagning)

**Datum:** 2026-05-16
**Status:** Accepted 2026-05-16 (Klas-godkänd — "Det är jag som tagit fram denna refactor")
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0016 (Civic design language — §Konsekvenser flaggade dark mode som Fas 1–2-skuld), ADR 0015 (Frontend-stack, CSS-first tokens), ADR 0003 (Design as skills), CLAUDE.md §5.2 + §8.9, DESIGN.md, `web/jobbpilot-web/src/app/globals.css`

---

## Kontext

Vid Fas 0-bootstrap (se ADR 0016) togs dark mode bort. Skälet var konkret: shadcn nova-presetens `.dark`-block genererade oklch indigo-violetta värden (`oklch(0.488 0.243 264.376)`) som bröt mot CLAUDE.md §5.2:s förbjudna palett och ADR 0016:s civic-restriktion. Den borttagna koden i `globals.css` lämnade en kommentar som krävde att dark mode inte återinförs "utan Klas approval + ADR", och CLAUDE.md §8.9 kodifierar samma spärr. Denna ADR är den arkitekturpost som spärren kräver.

Klas har tagit fram en designsystem-v2-refactor och godkänt den explicit 2026-05-16. Behovet:

1. **v1-paletten var warm-gray.** Den civic-tonen ville Klas byta mot en slate-baserad skala som bättre matchar 1177/Digg/GOV.UK-referensen.
2. **Dark mode efterfrågades** men fick inte återinföra §5.2-brottet. Lösningen är en civic slate-skala utan dekorativ kulör — inte shadcn-presetens indigo-violetta default.
3. **Mekanismvalet** (hur dark mode aktiveras tekniskt) hade flera giltiga alternativ; FOUC/FOWT-hantering utan extern dependency krävde ett medvetet beslut. CTO-beslut 2026-05-16 = Variant A.

## Beslut

JobbPilots designsystem migreras till **v2**: en slate-baserad civic-utility-palett med fullt dark mode-stöd.

**Palett (v2):** slate-skala ersätter v1:s warm-gray-värden. Kanonisk `--jp-*`-palett definieras **en gång** i `:root` (light) + `[data-theme="dark"]`. Tailwind v4 `@theme inline` + shadcn `:root`-bryggan refererar `--jp-*`, så alla utilities + shadcn-komponenter blir theme-aware automatiskt utan per-komponent-ändringar.

**Dark mode-mekanism:** `data-theme="dark"`-attribut på `<html>` (**inte** `.dark`-klass). Tailwind v4 `@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *))`.

**No-flash:** inline blockerande IIFE-script (`ThemeScript`) i `<body>` sätter `data-theme` före första paint från localStorage-nyckeln `jp-theme`, med fallback till `prefers-color-scheme`. `ThemeProvider` (client) använder `useSyncExternalStore` för runtime-läsning; `suppressHydrationWarning` på `<html>`. Ingen extern dependency (`next-themes` förbjudet av batch-scope) — detta är webbplattform-standardlösningen för FOUC/FOWT, samma teknik som `pacocoursey/next-themes` internt. CTO-beslut 2026-05-16 = **Variant A** (inline blockerande script + client provider).

**Varför detta supersederar Fas 0-borttagningen:** Fas 0 tog bort dark mode för att shadcn nova-presetens oklch indigo-violett bröt CLAUDE.md §5.2. v2:s dark-palett är en civic slate-skala utan dekorativ kulör — surfaces `#020617`/`#0F172A`/`#1E293B`, slate-text, `blue-400` som brand. Den återinför **inte** brottet.

**Density-system:** `[data-density]` (compact 0.85 / standard 1.0 / luftig 1.18) multiplicerar `--jp-row-h` / `--jp-section-y` / `--jp-pad-x`.

**App shell Variant B:** sektionerad sidebar, 4 px brand left-border på aktiv nav. JetBrains Mono + Hanken via `next/font`. Nytt `.jp-*` civic component utility-system.

## Alternativ som övervägdes

### Alt A — `data-theme`-attribut + inline blockerande script + client provider (valt)

**För:** Webbplattform-standard för FOUC/FOWT (samma teknik som `next-themes` internt). Ingen extern dependency — respekterar batch-scope. `data-theme` är ett rent attribut-API som låter `@custom-variant` kapsla in dark-selektorn på ett ställe. `useSyncExternalStore` ger korrekt SSR/runtime-konsistens.
**Emot:** Inline-script i `<body>` är en explicit avvikelse från "ingen inline JS"-instinkt. Kräver `suppressHydrationWarning` på `<html>`. Egen liten ytarea att underhålla i stället för ett bibliotek.

### Alt B — `next-themes`-paketet

**För:** Beprövat, hanterar FOUC, system-preference och persistence ur kartongen.
**Emot:** Ny top-level dependency utanför batch-scope (CLAUDE.md §9.2). Tillför ingen kapabilitet som inline-script + `useSyncExternalStore` inte redan ger för JobbPilots behov. Avvisat per CTO-beslut 2026-05-16 (Variant A).

### Alt C — `.dark`-klass (shadcn-default-mekanism)

**För:** shadcn-konvention; minst friktion mot preset-defaults.
**Emot:** Klass-toggling på `<html>` kräver att theme-läsning sker innan paint ändå (samma no-flash-problem). `data-theme` ger renare `@custom-variant`-kapsling och separerar theme-state från Tailwinds klassrymd. Avvisat.

### Alt D — Behåll v1 warm-gray utan dark mode

**För:** Ingen migreringskostnad; Fas 0-spärren orörd.
**Emot:** Löser inte det uttryckta behovet (slate-civic + dark mode). Fas 0-borttagningen var en tidsbunden skuld (ADR 0016 §Konsekvenser) som Klas nu valt att lösa, inte en permanent designhållning. Avvisat.

## Konsekvenser

### Positiva

- **Dark mode levereras** utan att återinföra CLAUDE.md §5.2-brottet — civic slate-skala, ingen dekorativ kulör.
- **En kanonisk palettkälla.** `--jp-*` definieras en gång; Tailwind `@theme inline` + shadcn `:root`-brygga refererar den, så hela utility- + komponent-ytan blir theme-aware automatiskt.
- **Ingen FOUC/FOWT** och ingen extern dependency — webbplattform-standardlösningen, batch-scope respekterad.
- **Fas 0-spärren formellt upplöst.** ADR 0016 §Konsekvenser-skulden och CLAUDE.md §8.9-spärren har nu sin krävda arkitekturpost.
- **Density-system** ger civic-anpassningsbar informationstäthet utan token-fork.

### Negativa

- **Dark mode är nu hårt krav, inte valfritt.** Varje UI-ändring måste valideras i både light och dark — dubblerad verifieringsyta per ändring.
- **Migreringskostnad:** DESIGN.md + alla 5 design-skills + `design-reviewer`- och `nextjs-ui-engineer`-agenterna uppdaterade till v2.
- **Inline-script + `suppressHydrationWarning`** är en avvikelse från "ingen inline JS / inga hydration-undantag"-instinkten — motiverad men kräver att framtida läsare förstår varför.
- **Egen ytarea i stället för bibliotek** — om webbplattformens theme-API ändras är det JobbPilots kod som måste följa med, inte ett uppströms-paket.
- **Känd doc-drift att lösa:** `jobbpilot.css`/`.jp-h1` använder `font-weight: 500` / `display 36px` medan `tokens-full.md` / DESIGN.md §4 säger `600` / `56px`. Flaggat för Klas auktoritetsbeslut. Blockerar **inte** denna ADR — noteras som öppen punkt.

## Implementation

- `web/jobbpilot-web/src/app/globals.css` — `--jp-*` i `:root` + `[data-theme="dark"]`, `@custom-variant dark`, `@theme inline` + shadcn `:root`-brygga, `[data-density]`-multiplikatorer.
- `ThemeScript` (inline blockerande IIFE i `<body>`) + `ThemeProvider` (client, `useSyncExternalStore`); `suppressHydrationWarning` på `<html>`; localStorage-nyckel `jp-theme`.
- App shell Variant B (sektionerad sidebar, 4 px brand left-border aktiv nav); JetBrains Mono + Hanken via `next/font`; `.jp-*` civic component utility-system.
- DESIGN.md + 5 design-skills + `design-reviewer` + `nextjs-ui-engineer` uppdaterade till v2.

**Öppen punkt (ej blockerande):** `.jp-h1` font-weight/display-drift mellan `jobbpilot.css` och `tokens-full.md`/DESIGN.md §4 — väntar Klas auktoritetsbeslut.

## Relation till andra ADR:er

- **ADR 0016** — flaggade dark mode som Fas 1–2-skuld i §Konsekvenser och definierade civic-restriktionen som v2:s slate-palett uppfyller. Denna ADR löser den skulden; ADR 0016:s förbjudna mönster + governance-krav gäller fortsatt oförändrade.
- **ADR 0015** — CSS-first OKLCH-tokensystem är den tekniska grunden v2:s `--jp-*`-palett bygger vidare på.
- **ADR 0003** — design-systemets skill-struktur; v2 uppdaterar skill-innehållet, inte strukturen.
- **`globals.css` Fas 0-borttagningsnot** — den krävda "Klas approval + ADR"-spärren uppfylls av denna ADR.

## Referenser

- CLAUDE.md §5.2 (förbjuden palett), §8.9 (dark mode-spärr), §9.2 (dependency-disciplin)
- ADR 0016 §Konsekvenser (dark mode som Fas 1–2-skuld), §Förbjudna mönster
- ADR 0015 (CSS-first OKLCH-tokens), ADR 0003 (design as skills)
- DESIGN.md + `.claude/skills/jobbpilot-design-*/`
- `pacocoursey/next-themes` — referensteknik för FOUC/FOWT (mekanism, inte dependency)
- senior-cto-advisor 2026-05-16 — mekanismval Variant A (inline blockerande script + client provider)
