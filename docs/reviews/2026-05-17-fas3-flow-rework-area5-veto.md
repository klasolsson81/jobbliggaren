# Design-review (VETO): FAS 3 flow-rework — `/ansokningar/[id]` Area 5

**Status:** ✓ APPROVED — med 2 Minor (icke-blockerande)
**Granskat:** 2026-05-17
**Auktoritet:** ADR 0047 (Area 5 task-completion/flödesbegriplighet, Accepted 2026-05-17) + design-reviewer Area 1–4 (estetik / tokens / a11y / svensk copy); CLAUDE.md §1 civic-utility
**Mandat:** rendered-screenshot-VETO + interaktionssökväg-korsläsning (ADR 0047 Beslut 2)
**Granskat material:**
- 12 rendered screenshots, `C:/tmp/jobbpilot-visual/20260517-2216/` (outcome-form + lista, light/dark, 1280/1920/3440)
- `status-card.tsx`, `record-follow-up-outcome-form.tsx`, `(app)/ansokningar/[id]/page.tsx` (interaktionssökväg — screenshots visar ej disclosure-expanderat/confirm-stadium)

> Skärmbilderna visar grundstadiet (disclosure stängd, confirm ej utlöst).
> Det är precis varför ADR 0047 Beslut 2 kräver kod-korsläsning — verdiktet
> vilar på kod + screenshot tillsammans, inte den statiska bilden ensam.

---

## VETO-verdikt

**APPROVED.** Inga Blocker, inga Major. De 5 defekterna Klas fann i v0.2.13-dev
är åtgärdade i flödet — inte bara kosmetiskt flyttade. ADR 0047 Area 5-checklistan
(a)–(e) går igenom. Area 1–4 rena. Två Minor noteras nedan, ingen är blockerande.

---

## Per-defekt: åtgärdad ja/nej

### Defekt 1 — Status/action-blandning utan nuläges-förankring → ÅTGÄRDAD

`status-card.tsx` är ett GOV.UK summary-card. `Nuvarande status: <StatusPill>`
är **alltid synlig** (rad 113–119) i ett semantiskt `<dl>` — inte ersatt av
nästa-state. "Ändra status" är progressiv disclosure (`aria-expanded`,
`aria-controls="status-change-region"`, rad 100–101). När disclosure öppnas
**upprepas nuläget** ("Nuvarande status är **Utkast**.", rad 137–143) ovanför
övergångsknapparna. Boeke status/action-anti-pattern är strukturellt löst:
kontrollen visar aldrig nästa-state som om det vore nuvarande. Verifierat i
screenshot light/dark/1280–3440 (StatusPill "Utkast" förankrad i kortets topp).

### Defekt 2 — Irreversibelt utfall okommunicerat, ingen ångra/bekräfta → ÅTGÄRDAD

Två oberoende skydd:
- **Status-övergångar:** destruktiva (`isDestructiveTransition`) går via
  bekräftelse-`Dialog` (rad 169–214) med explicit konsekvens FÖRE handling:
  "Ansökan ändras från **X** till **Y**. Det går inte att ångra utan manuell
  åtgärd." + Avbryt/destructive-knapp.
- **Utfall:** `record-follow-up-outcome-form.tsx` har persistent konsekvenstext
  (rad 63–69, `aria-describedby`-kopplad) FÖRE val + tvåstegs bekräftelse-stadium
  (rad 102–141): "Spara utfallet **X**? Detta går inte att ändra efteråt." med
  Avbryt. Wroblewski check-before-submit uppfylld. Backend förblir irreversibel
  by-design (dotnet-architect Beslut 4) — UI:t kommunicerar, vilket är rätt
  ansvarsfördelning per ADR 0047 ("flow stays on rendered surface").

### Defekt 3 — Etikettlös konkatenering → ÅTGÄRDAD

`page.tsx` rad 161–183: utfall/anteckning renderas i semantiskt `<dl>` med
explicita `<dt>` "Utfall:" / "Anteckning:" och `<dd>`-värden. Ingen
`{outcome}—{note}`-konkatenering kvar. Verifierat i screenshot: "Utfall:
Inväntar svar" / "Anteckning: Visuell verifiering — väntar svar" som
etiketterade fält-rader. (Bindestreck-texten är medveten testfixtur — ej
copy-granskad per uppdragsinstruktion.)

### Defekt 4 — AddFollowUp sammanflätat med outcome-form → ÅTGÄRDAD

Strukturellt separerade i `page.tsx`:
- Outcome-form renderas **inuti varje `<li>`** för Pending-uppföljning
  (rad 185–190), avgränsad med `border-t` (`record-follow-up-outcome-form.tsx`
  rad 61).
- "Lägg till uppföljning" är ett **eget block** med egen `border-t`-avdelare
  och egen `<h3>` (rad 198–203), utanför `<ul>`-listan.
Screenshot bekräftar tydlig visuell separation: outcome-form sitter i
uppföljnings-kortet medan "Lägg till uppföljning" är ett distinkt
underavsnitt med egen rubrik. GOV.UK "one thing per page"-andan uppfylld.

### Defekt 5 — Ingen sektionering → ÅTGÄRDAD

Fyra distinkta `<section aria-labelledby>`-kort (Status, Personligt brev,
Uppföljningar, Noteringar) med `border border-border`, header-rad med
`<h2 text-h3>` och `border-b`-avdelad kropp. `gap-6` mellan korten.
"Lägg till"-formulär är `border-t`-avdelade underavsnitt med `<h3>`.
Screenshot light/dark/alla viewports: tydlig kort-rytm, inget "rakt
upp-och-ner". Krug "don't make me think" uppfylld — strukturen är skannbar.

---

## ADR 0047 Area 5-checklista

| Check | Verdikt | Grund |
|---|---|---|
| (a) Förstagångsanvändare slutför kärnuppgift utan att gissa | ✓ | Sektionerade kort, explicit knapp-copy ("Ändra status", "Spara utfall", "Lägg till uppföljning"), disclosure med upprepat nuläge |
| (b) Systemstatus synlig + förankrad | ✓ | `Nuvarande status:`-pill alltid synlig i kort-topp; ej ersatt av nästa-state; upprepad i disclosure |
| (c) Irreversibel handling märkt FÖRE handling | ✓ | Persistent konsekvenstext + tvåstegs confirm (utfall); bekräftelse-Dialog med "går inte att ångra"-text (destruktiv status) |
| (d) Separata uppgifter visuellt blandade | ✓ ej blandade | Outcome-form i `<li>`; "Lägg till uppföljning" eget `<h3>`-block utanför `<ul>` |
| (e) Sektions-/områdesavgränsning | ✓ | 4 `<section aria-labelledby>`-kort + border + gap-6 + h3-avdelade underavsnitt |

Alla fem uppfyllda. Ingen Area 5-Blocker/Major.

---

## Area 1–4

**Area 1 (civic-utility-estetik):** ren. Inga gradienter, glow, glasmorfism,
AI-accentfärger, emoji. `rounded-md` (≤6px). Solida token-bakgrunder. Typografi-
hierarki via `text-h3`/`text-body-sm`. Pipeline-listan har civic
left-border-aktiv-nav (verifierad i screenshot — 4px brand-blå på "Ansökningar",
ej pill).

**Area 2 (tokens):** ren. Genomgående `bg-surface-primary`,
`bg-surface-secondary`, `border-border`, `text-text-primary/secondary/tertiary`,
`text-danger-700`. Inga Tailwind-defaults eller hex hittade i de tre filerna.

**Area 3 (a11y):** stark. `<section aria-labelledby>` ×4, `<dl>/<dt>/<dd>`
semantik, `aria-expanded`+`aria-controls` på disclosure, `aria-invalid`+
`aria-describedby` på Select, `role="alert"` på felmeddelanden, `<label htmlFor>`
kopplad via unika id, breadcrumb `<nav aria-label>` + `aria-hidden` separator.
Kontrast i dark verifierad i screenshot (1280/1920/3440) — text-secondary mot
mörk yta läsbar, ingen dark-only-regression.

**Area 4 (svensk copy):** ren. "du"-tilltal, inga utropstecken, ingen emoji,
datum `2026-05-17` (sv-SE locale). Konsekvenstext rak och informativ:
"Det går inte att ångra utan manuell åtgärd." Inga AI-klyschor.

---

## Minor (icke-blockerande — FYI till nextjs-ui-engineer)

1. **Disabled "Spara utfall"-knapp visuellt subtil**
   Fil: `record-follow-up-outcome-form.tsx:104–113`
   I screenshot (light/dark) är "Spara utfall" i disabled-läge (outcome ej vald)
   svag — `variant="secondary"` disabled syns knappt mot kortet. Funktionellt
   korrekt (`disabled` + `aria` ok), men en förstagångsanvändare kan missa att
   knappen finns innan utfall valts. Förslag: explicit `disabled:opacity-50`
   eller hjälptext "Välj utfall först". Inte Blocker — flödet är genomförbart
   (välj utfall → knapp aktiveras), men polish höjer (a)-tydligheten.

2. **Statusövergång-fel rensas inte vid disclosure-toggle bakåt**
   Fil: `status-card.tsx:102–105`
   `setError(null)` körs vid toggle, bra. Men om ett serverfel visats
   (`role="alert"`, rad 160–164) och användaren stänger disclosure utan att
   åtgärda, försvinner felkontexten tyst vid återöppning. Mindre
   begriplighets-kant — överväg att behålla senaste fel tills lyckad övergång.
   Inte Blocker — ingen felaktig handling möjliggörs.

---

## Sammanfattning

**VETO: APPROVED.** 0 Blocker, 0 Major, 2 Minor. Alla 5 defekter från
v0.2.13-dev är strukturellt åtgärdade i flödet (verifierat mot kod +
interaktionssökväg per ADR 0047 Beslut 2, ej enbart screenshot). Area 1–4
rena. Area 5-checklistan (a)–(e) uppfylld. Minor är polish, ej fas-stängnings-
blockerare — delegeras till nextjs-ui-engineer som opportunistisk touch, ej
re-review-krav.

Klas slutgodkänner + live-verifierar.
