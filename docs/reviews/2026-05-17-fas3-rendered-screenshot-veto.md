# Design-reviewer — FAS 3 rendered-screenshot-VETO (Grind 2)

**Datum:** 2026-05-17
**Agent:** design-reviewer (rendered-screenshot-veto)
**Batch:** RecordFollowUpOutcome-vertikal (commit 78d3b14)
**Grind:** FAS 3-stängning Grind 2 — rendered-screenshot-VETO mot bilder (ADR 0046 rad 104, Fas 2 Batch 6-mönster)
**Auktoritet:** DESIGN.md §1 (filosofi), §3 (färg/dark-mode), §5 (spacing/radius), §6 (komponenter), §8 (copy), §9 (a11y) + jobbpilot-design-tokens/a11y/copy-skills
**Skärmbilder:** `C:/tmp/jobbpilot-visual/20260517-2057/` mot v0.2.13-dev, auth-läge dev-test-konto

## VETO-verdikt: APPROVED — 0 Block / 0 Major / 1 Minor

Kod-nivå-approval (rapport `2026-05-17-fas3-recordfollowupoutcome-design.md`) bekräftas
nu på rendered-nivå. FAS 3 Grind 2 passerad — Klas slutgodkänner bilderna.

---

## Granskade ytor (12 FAS 3-relevanta shots)

| Yta | light | dark | 1280 | 1920 | 3440 |
|---|---|---|---|---|---|
| `ansokningar-detalj-outcome-form` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ansokningar-lista` | ✓ | ✓ | ✓ | ✓ | ✓ |

Alla 12 granskade. Layout-integritet verifierad i alla 3 viewports inkl.
3440 bred-skärm (Fas 2-precedensens broad-screen-gate).

---

## Områdesbedömning

### Civic-utility-ton (DESIGN.md §1) — PASS
- Inga gradients, glow, glasmorfism, drop-shadow > sm i någon viewport/tema.
- Inga AI-accentfärger (lila/cyan/neon) — strikt slate + myndighetsblå.
- "Utfall"-panelen är inramad med hairline-border, djup via border ej skugga
  (`bg-surface-primary` + `border-border-default`, civic-ledger-form bevarad).
- Radius ≤ 6px genomgående; "Utkast"-status använder pill-radius korrekt.
- Typografisk hierarki bär visuell vikt (H1 "Ansökningar", H2 "Uppföljningar",
  mono-ID `69cc8a45`, mono caps-labels "MINA ANSÖKNINGAR"). Content-first.

### Token-disciplin + dark-mode-kontrast (DESIGN.md §3, WCAG 1.4.3/1.4.11) — PASS
- Light + dark validerade parallellt, inte dark som efterhandstillägg.
- Dark canvas `surface-primary` (#020617), panel-border synlig och korrekt.
- "Utfall"-select-border läsbar mot dark panel i alla viewports — klarar
  3:1 UI-komponentgräns.
- "Spara utfall" `variant="secondary"` (m2-fix verifierad rendered): låg-emfas
  sekundärknapp bredvid select, läsbar text båda teman, ingen konkurrens med
  den enda `--primary` ("Lägg till uppföljning" / "Spara notering").
- Civic-nav-check PASS: aktiv "Ansökningar" = 4px brand-blå vänsterkant +
  transparent bakgrund, INTE bakgrunds-pill. Korrekt i light + dark.
- Brand-primary-knappar: dark = ljusblå (#60A5FA) med mörk text (~7:1, AA),
  light = #0B5CAD med vit text (6.1:1, AA). Korrekt per contrast-table.

### a11y rendered (DESIGN.md §9) — PASS (med en residual notering)
- Meningsfull struktur: breadcrumb, H1/H2-hierarki, label-ovanför-input
  genomgående ("Utfall", "Kanal", "Datum", "Anteckning (valfritt)", "Notering").
- Inga beskrivande placeholder-exempel i fält (Platsbanken-regeln) — select
  visar `SelectValue`-platshållare ("Välj utfall"/"Välj kanal"), tillåtet undantag.
- Status aldrig enbart färg: "Utkast"-pill = text + bakgrund, statusrad
  "Inväntar svar — Visuell verifiering — väntar svar" är text.
- M2 danger-700 dark-mode-kontrast (#FECACA på #2E1014 = ~7.6:1) kan ej
  renderverifieras — inget valideringsfel triggat i fixtur. Kod-nivå-approval
  + contrast-table-värdet står; ingen rendered-motsägelse. Inte ett fynd.
- Fokus-ringar ej fångbara i statiska shots (förväntat) — globalt CSS-styrt
  `:focus-visible`, verifierat på kod-nivå.

### Svensk copy (DESIGN.md §8) — PASS
- "du"-ton, ingen emoji, inga utropstecken.
- "Pipeline över alla ansökningar. Klicka på en rad för detaljer." — konkret,
  civic, opretentiös.
- Labels: "Utfall" (m1-fix verifierad rendered, ej "Registrera utfall").
- Datum ISO-format `2026-05-17` korrekt svensk locale.
- Testfixtur-text ("FAS 3 visuell verifiering (temp 20260517-2057)",
  "Visuell verifiering — väntar svar") medvetet undantagen per uppdrag — ej
  granskad som produkt-copy.

---

## Minor (nice-to-fix, ej blocker, ej FAS 3-stängningsblockerare)

1. **Native `datetime-local`-fält svag kontrast i dark mode**
   Fil-yta: `ansokningar-detalj-outcome-form__dark__{1280,1920,3440}`
   AddFollowUp-formulärets "Datum"-fält använder browser-native
   `<input type="datetime-local">`. I dark mode renderas placeholder
   `yyyy-mm-dd --:--` och den native kalender-picker-ikonen i låg kontrast
   mot mörk fältbakgrund — picker-ikonen är nästan osynlig vid 3440 dark.
   Detta är **pre-existerande** AddFollowUp-yta och sammanfaller med m3
   (date-fns/tid-lokalisering) som medvetet sköts upp utanför PR-scope i
   kod-nivå-granskningen. **Ingen ny regression introducerad av denna batch.**
   Föreslagen åtgärd: hantera tillsammans med m3 lokaliserings-touch
   (custom date-input eller `color-scheme: dark` på fältet) — inte i denna grind.

---

## Övriga ytor (utanför FAS 3-scope) — inga regressions flaggade

Stickprov på `landing`, `logga-in`, `registrera`, `sokningar-lista`,
`vantelista`, `jobb-*`, `sokningar-radera-dialog` i katalogen visar inga
uppenbara civic-utility-brott eller dark-mode-regressions som sticker ut.
Inte uttömmande granskade (utanför uppdragsscope).

---

## Sammanfattning

0 Block, 0 Major, 1 Minor. Den enda Minor är en pre-existerande native-date-
kontrast som följer med den redan uppskjutna m3-lokaliseringstouchen — inte en
regression från RecordFollowUpOutcome-batchen och inte en FAS 3-stängnings-
blockerare. Civic-utility-tonen, token-disciplinen, dark-mode-kontrasten,
a11y-strukturen och svensk copy håller i alla tre viewports i båda teman.

**VETO: APPROVED.** FAS 3-stängning Grind 2 passerad. Klas slutgodkänner bilderna.
