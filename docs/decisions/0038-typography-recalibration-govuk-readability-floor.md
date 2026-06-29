# ADR 0038 — Typografi-omkalibrering: GOV.UK-läsbarhetsgolv (delvis supersession av ADR 0037)

**Datum:** 2026-05-16
**Status:** Accepted 2026-05-16 (Klas-GO 2026-05-16; senior-cto-advisor-beslut samma datum)
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0037 (Designsystem v2 — **delvis supersederad**, endast typografi/density-aspekten), ADR 0016 (Civic design language), DESIGN.md §1.1/§4/§6, PRINCIPLES regel 7, `contrast-table.md`, `tokens-full.md`, `web/jobbpilot-web/src/app/globals.css`

---

## Kontext

ADR 0037 levererade designsystem v2 och implementerade JobbPilotNEWDESIGN-handoffen (JobbPilotNEWDESIGN) pixel-perfekt. Den handoffen kalibrerade typografin till Bloomberg-terminal-täthet: brödtext 13–14px, metadata 11.5px, mono caps-labels 10.5px, utbredd användning av text-tertiary, inputs 32px höga, och beskrivande exempel-innehåll i placeholder-fält.

Klas live-underkände tydligheten 2026-05-16 i en side-by-side-jämförelse mot Platsbanken. Rotorsaken verifierades: handoffen DREV BORT från JobbPilots EGEN spec, inte mot den. Konflikterna:

- **DESIGN.md §1.1** — målanvändaren är en 55-årig processoperatör; terminal-täthet motverkar den användaren direkt.
- **DESIGN.md §1 + PRINCIPLES.md regel 7** — referens-aestetiken rankar GOV.UK Design System först. Regel 7 mening 2 säger explicit "luftigt nog att läsa".
- **`contrast-table.md`** — dömer redan text-tertiary (`#94A3B8` på vit ≈ 2.6:1) som non-konform för brödtext. Handoffens utbredda text-tertiary-användning bröt mot en regel som projektet redan hade beslutat.

Web-research 2026-05-16 bekräftade golvet: GOV.UK Frontend kör 16px brödtextgolv (aldrig under), höjt för accessibility med stöd i British Dyslexia Association 16–19px-rekommendation. Nielsen Norman / WCAG: beskrivande placeholder-exempel i inmatningsfält är ett känt skadligt anti-pattern (försvinner vid fokus, läses som ifyllt värde, sänker SR-tydlighet).

ADR 0037:s §Negativa-not flaggade dessutom en öppen drift-punkt: `.jp-h1` `font-weight: 500` vs `tokens-full.md`/DESIGN.md §4 som säger `600`. Denna ADR stänger den punkten.

## Beslut

Typografin och fältstorlekarna i designsystem v2 omkalibreras till ett GOV.UK-förankrat läsbarhetsgolv. Civic-ledger-FORMEN (flata tabeller, hairlines, mono-IDs, inga cards) står oförändrad — endast skala, färg och fältstorlek ändras. Beslutade golv-värden (senior-cto-advisor 2026-05-16):

**Brödtext & rubriker**
- Brödtext 14 → **16px/400**. Body-sm/small → **14px min**. Lede → **17px**. H3 18px/500 → **18px/600**. H1/H2/Display oförändrade (28/20/56, alla 600).
- `.jp-h1` font-weight LÖSES: **500 → 600** (titlar bär information). `.jp-h1--display` → **600**. `.jp-h2` → **600**. Detta stänger ADR 0037 §Negativa öppna drift-punkt.

**Mono**
- Mono inline-data som användaren läser (datum, ID, räknare) → **13px/500**, färg text-secondary/primary.
- Mono caps-LABELS → **11.5px**, färg **text-secondary** (ALDRIG text-tertiary).

**Färg / kontrast**
- Informationsbärande text: **text-secondary** (`#475569`, 7.4:1) eller **text-primary**. **text-tertiary ENDAST dekorativt** (befintlig `contrast-table.md`-regel — efterlevs, inte ny).

**Fält-ergonomi**
- Input-höjd 32 → **44px** (sm 40). Knapp-höjd 32 → **40px** (sm 36).
- Beskrivande placeholder-exempel borttagna i sök/filter-fält. Auth-formulärs format-placeholder (`din.email@exempel.se`) behålls — syntax-mönster, ej exempelinnehåll, med stark label-kontext.

**Scope:** global token-ändring i `globals.css` (`--jp-*` + `@theme inline` + `.jp-*`-komponentstorlekar), **EJ per-sida** (DRY/SPOT — Martin, *Clean Architecture* kap. 13 CCP).

## Alternativ som övervägdes

### Alt A — Global token-omkalibrering till GOV.UK-golv (valt)

**För:** Återställer JobbPilots egen spec (DESIGN.md §1.1 målanvändare, PRINCIPLES regel 7 GOV.UK-referens). En kanonisk källa i `globals.css` — DRY/SPOT, ingen per-sida-fork. Stänger ADR 0037:s öppna `.jp-h1`-drift i samma drag. Forskningsförankrat golv (GOV.UK Frontend, BDA, Nielsen/WCAG).
**Emot:** Hela v2-ytan kräver visuell om-verifiering i både light och dark. Handoffens pixel-perfekta auktoritet bryts — DESIGN.md/skills/ADR blir auktoritet i stället.

### Alt B — Per-sida-override där tydlighet brister

**För:** Mindre yta att om-verifiera; rör inte global token.
**Emot:** Bryter DRY/SPOT (Martin CCP) — samma läsbarhetsgolv divergerar per sida och driver tillbaka. Rotorsaken (global kalibrering bort från egen spec) adresseras inte. Avvisat.

### Alt C — Behåll handoffens täthet (acceptera Bloomberg-kalibrering)

**För:** Ingen om-verifieringskostnad; handoffen orörd.
**Emot:** Klas live-underkände tydligheten mot Platsbanken. Direkt konflikt med DESIGN.md §1.1, PRINCIPLES regel 7 och projektets egen `contrast-table.md`. Avvisat.

## Konsekvenser

### Positiva

- **Läsbarhet återställd för målanvändaren** (DESIGN.md §1.1, 55-årig processoperatör) — forskningsförankrat 16px-golv.
- **Egen spec efterlevs igen** — PRINCIPLES regel 7 GOV.UK-referens, `contrast-table.md`-regeln om text-tertiary.
- **ADR 0037:s öppna `.jp-h1`-drift stängd** i samma beslut (500 → 600).
- **DRY/SPOT bevarad** — en kanonisk token-källa, ingen per-sida-fork.

### Negativa

- **Hela v2-ytan kräver visuell om-verifiering** i light + dark (ADR 0037 §Negativa-kravet om dubblerad verifieringsyta gäller fullt här).
- **JobbPilotNEWDESIGN-handoffen är inte längre pixel-perfekt auktoritet** — DESIGN.md/skills/ADR är auktoritet. Framtida läsare måste förstå att handoffen är historisk, inte normativ.
- **Följdrevidering krävs:** DESIGN.md §4/§6 + `tokens-full.md` + PRINCIPLES regel 7-förtydligande revideras separat (Klas-GO givet).

## Implementation

- `web/jobbpilot-web/src/app/globals.css` — `--jp-*`-typografi/färg-tokens, `@theme inline` och `.jp-*`-komponentstorlekar (input/knapp-höjd) enligt golv-värdena ovan.
- Beskrivande placeholder-exempel borttagna i sök/filter-fält; auth-format-placeholder behållen.
- Visuell om-verifiering av hela v2-ytan i light + dark.
- Separat (Klas-GO givet): DESIGN.md §4/§6 + `tokens-full.md` + PRINCIPLES regel 7-förtydligande revideras.

## Relation till andra ADR:er

- **ADR 0037 — DELVIS supersederad.** Endast typografi/density-aspekten ersätts. ADR 0037:s beslut om dark-mode-mekanik (`data-theme`, inline blockerande script, `useSyncExternalStore`), slate-palett och `[data-density]`-systemet står ORÖRDA. Detta är en dokumenterad partiell supersession — **ingen statusändring på ADR 0037** (den är inte ersatt i sin helhet). Den partiella ersättningen dokumenteras här i ADR 0038.
- **ADR 0016** — civic-restriktionen gäller fortsatt; civic-ledger-FORMEN ändras inte.

## Referenser

- DESIGN.md §1.1 (målanvändare 55-årig processoperatör), §4 (typografi-tokens), §6
- PRINCIPLES.md regel 7 (referens-aestetik, "luftigt nog att läsa")
- `contrast-table.md` (text-tertiary `#94A3B8` ≈ 2.6:1 non-konform för brödtext)
- `tokens-full.md` (`.jp-h1` font-weight-spec)
- GOV.UK Frontend — 16px brödtextgolv; British Dyslexia Association 16–19px (web-research 2026-05-16)
- Nielsen Norman Group / WCAG — placeholder-exempel som anti-pattern (web-research 2026-05-16)
- Robert C. Martin, *Clean Architecture*, kap. 13 (Common Closure Principle — DRY/SPOT-grund för global token-scope)
- senior-cto-advisor 2026-05-16 — golv-värden + global-scope-beslut

---

## Amendment 2026-05-17 — input-placeholder-regel hårdnad (Klas-direktiv)

**Datum:** 2026-05-17
**Källa:** Klas hård design-direktiv 2026-05-17 (Fas 2-stängningssession)
**Trigger:** ADR 0038:s ursprungliga delbeslut formulerade placeholder-frågan smalt — "beskrivande placeholder-exempel borttagna **i sök/filter-fält**" med ett behållet auth-format-placeholder-undantag (`din.email@exempel.se`). Klas-direktivet generaliserar och hårdnar formuleringen till en absolut regel över **alla** input-fält.
**Beslutsfattare:** Klas Olsson (direktiv 2026-05-17 = beslutskälla; CLAUDE.md §9.4 Klas-direktiv-undantag — amendment-text CC-strukturerad från Klas-underlaget, samma mönster som ADR 0032-amendments och ADR 0042 implementerings-notat denna session)
**Status:** Accepted (Klas-direktiv = beslutskälla). Additivt — Beslut-brödtexten (§41) ändras **inte**; denna sektion generaliserar och ersätter funktionellt det smala fält-scope:t med en absolut regel.

### Kontext för amendment

ADR 0038 §41 löste placeholder-frågan smalt (borttagning i sök/filter-fält, behållet auth-syntaxmönster). Klas live-granskning 2026-05-17 underkände kvarvarande exempel-placeholders som princip-inkonsekvens: ett behållet undantag öppnar för glidning tillbaka mot exempelinnehåll i fält. Civic-utility-referensen (Platsbanken/1177, jobbpilot-design-principles regel 3/7) har **rena inputs utan exempel-placeholders**; hjälp ges som persistent hjälptext, inte som text som försvinner vid fokus.

### Beslut

**Absolut regel:** inga exempel-placeholders i input-fält — varken beskrivande exempel eller syntax-mönster (det tidigare auth-format-undantaget `din.email@exempel.se` upphävs). Inmatningshjälp ges som **persistent hjälptext ovanför eller under fältet**, knuten via `aria-describedby`. Rena inputs (Platsbanken-stil).

**Rationale:**

- **Civic-utility / Platsbanken-stil** (jobbpilot-design-principles regel 3/7): rena fält utan exempelinnehåll signalerar myndighetsverktyg, inte konsumentprodukt.
- **A11y:** hjälptext via `aria-describedby` är robust (persistent, läses av skärmläsare som komplement till label, försvinner inte vid fokus). Placeholder som hjälpbärare är ett känt anti-pattern (Nielsen Norman / WCAG, redan citerat i ADR 0038 §Kontext) — försvinner vid fokus, läses som ifyllt värde, sänker SR-tydlighet.

**Kodifiering:** regeln är kodifierad i `.claude/skills/jobbpilot-design-components/SKILL.md` och `.claude/skills/jobbpilot-design-copy/SKILL.md` (kanonisk design-spec-yta — DESIGN.md indexerar dessa).

**Dokumenterat undantag:** shadcn-`SelectValue`-komponentens `placeholder`-prop är inte ett input-exempel utan en *tom-tillstånds-etikett* för en select (motsvarar en label för ej-valt tillstånd, inte exempelinnehåll i ett textfält). Detta är ett dokumenterat, avgränsat undantag — inte en lucka i regeln.

### Relation till ADR 0038 Beslut-brödtext

§41:s mening "Beskrivande placeholder-exempel borttagna i sök/filter-fält. Auth-formulärs format-placeholder (`din.email@exempel.se`) behålls — syntax-mönster, ej exempelinnehåll, med stark label-kontext." är **funktionellt upphävd** av detta amendment: det behållna auth-undantaget gäller inte längre, och regeln gäller alla input-fält, inte enbart sök/filter. Brödtexten lämnas orörd per ADR-immutabilitet (Nygard 2011) — läsare måste läsa §41 tillsammans med detta amendment, där amendmentet har företräde.

### Korsreferens

- jobbpilot-design-skills (`.claude/skills/jobbpilot-design-components/SKILL.md`, `.claude/skills/jobbpilot-design-copy/SKILL.md`) — regeln kodifierad där.
- ADR 0042 implementerings-notat 2026-05-17 (samma session — relaterad design-stängning).

---

## Amendment 2026-06-29 — informationsbärande siffror i läsbar sans (låg-syn-glyf-golv)

**Datum:** 2026-06-29
**Källa:** senior-cto-advisor binding `CTO-376-A-numerals-to-sans` (issue #376); Klas co-binder estetiken vid rendered-verify (§12 design-token-grind).
**Trigger:** Issue #376 (P1, hårt a11y-krav): §1.1-målanvändaren kan inte entydigt skilja siffror satta i appens monospace (JetBrains Mono) — `0` förväxlas med `8`, `6`/`8` tvetydiga. Rapporterat live på landningens header-stats, men gäller alla ~26 globals.css-klasser som använder `--jp-font-mono` för informationsbärande tal (header-/landning-stats, resultaträkning, sektion-/list-/notis-räknare, hero/pagehero-chip-tal, datum/tider, criterion-siffror).
**Beslutsfattare:** senior-cto-advisor (binding token `CTO-376-A-numerals-to-sans`); Klas Olsson (estetisk co-bind vid rendered-verify per §12).
**Status:** Accepted. Additivt — ADR 0038:s ursprungliga mono-doktrin i §Beslut (Mono-avsnittet) är funktionellt upphävd för SIFFER-innehåll; brödtexten lämnas orörd per ADR-immutabilitet.

### Kontext för amendment

ADR 0038 §Beslut (Mono-avsnittet) definierade `--jp-font-mono` 13px/500 som typsnitt för "mono inline-data som användaren läser (datum, ID, räknare)". JetBrains Mono default-nolla är prickad (löser `0`/`O`-förväxling) men löser inte `0`/`8`- eller `6`/`8`-förväxling — glyf-formerna är otillräckligt isärskilja för en synnedsatt användare (DESIGN.md §1.1-målanvändare).

Tre alternativ utreddes (web-research 2026-06-29):

- **Approach A — byt till Hanken Grotesk (sans) + `font-variant-numeric: tabular-nums`** för informationsbärande siffror. Löser glyf-problemet i sin helhet; tabular-nums bevarar kolumn-justering.
- **Approach B — behåll JetBrains Mono + aktivera `font-feature-settings: "zero" 1` (slashed-zero).** Avvisad på teknisk grund: web-research 2026-06-29 bekräftar att slashed-zero inte fungerar på Google-Fonts-subsetet av JetBrains Mono (upstream-issues google/fonts #7881, JetBrains/JetBrainsMono #551) — aktivering kräver separat font-binär — och löser ändå bara `0`, inte `8`/`6` (glyf-form opåverkad).
- **Approach C — hybrid: mono kvar på vissa ytor, sans på andra (differentierat per yt-typ).** Avvisad: splittrar regeln på yt-typ i stället för styrande princip → framtida drift (CCP-brott, Martin *Clean Architecture* kap. 13).

CTO valde Approach A.

### Beslut

Informationsbärande siffror — antal, belopp, datum, tider, räknare, stats och ID-/SSYK-siffror som användaren läser och agerar på — sätts i Hanken Grotesk (sans, `--font-sans`/`--jp-font-sans`) med `font-variant-numeric: tabular-nums`. `tabular-nums` är progressive enhancement: bevarar kolumn-justering och gör tabellliknande listor metriskt stabila.

Monospace (`--jp-font-mono`) behålls ENDAST för bokstavs-/kod-identifierare och versala caps-labels (mono-kickers, kolumnrubriker, `SV`/`EN`, versionssträngar, opaka stöd-koder som `.jp-criterion__id`) där rollen är *etikett/kod*, inte *läs talet*.

**Styrande princip (framtida klasser följer denna regel):** bär siffran innebörd användaren agerar på → sans + `tabular-nums`; är det dekorativ struktur eller opak kod → mono OK.

Detta utsträcker ADR 0038:s GOV.UK-läsbarhetsgolv från skala/färg/fältstorlek till **siffer-glyfform**.

### Implementation

~26 globals.css-klasser med `--jp-font-mono` för informationsbärande tal flippas till `--jp-font-sans` + `font-variant-numeric: tabular-nums` (PR för issue #376). DESIGN.md §4 omkalibreras parallellt (mono-doktrinen uppdateras). Inga nya tokens eller klasser tillkommer — ändringen sker på existerande klasser (AC#5-konfirm).

### Relation till ADR 0038 Beslut-brödtext

§Beslut Mono-avsnittet ("Mono inline-data som användaren läser (datum, ID, räknare) → 13px/500, färg text-secondary/primary.") är **funktionellt upphävd** för siffer-innehåll av detta amendment: dessa klasser sätts nu i sans. Caps-labels och kod-identifierare i mono är oförändrade. Civic-ledger-formen (flata tabeller, hairlines, inga cards) är oförändrad. Brödtexten lämnas orörd per ADR-immutabilitet (Nygard 2011) — läsare läser §Beslut tillsammans med detta amendment, där amendmentet har företräde.

### Korsreferens

- Issue #376 (P1 a11y — trigger).
- DESIGN.md §4 — mono-doktrinen omkalibreras i samma PR.
- ADR 0052 §Beslut 5 (typografi: "mono **endast** för IDs, datum, antal") — siffer-klausulen (datum/antal) **funktionellt inskränkt** av detta amendment (mono→sans, samma sätt som 0038:s egen mono-doktrin); ADR 0052:s mono-för-koder/labels och övrig typografi-/färgskala består (komplementär, ej supersederad). Forward-not i 0052:s README-rad.
- `.claude/skills/jobbpilot-design-tokens` (`text-mono`-raden) — synkad i samma PR till siffer-doktrinen (skill speglar DESIGN.md §4, drift-skydd per DESIGN.md §2).
- web-research 2026-06-29: google/fonts #7881, JetBrains/JetBrainsMono #551 (slashed-zero ej tillgänglig i Google-Fonts-subset).
