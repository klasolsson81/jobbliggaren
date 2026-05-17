# Code-review: FAS 3 flödes-omarbetning /ansokningar/[id] (pre-commit)

**Status:** GO — APPROVED
**Granskad:** 2026-05-17
**Auktoritet:** CLAUDE.md §4 (TS/Next.js), §5.2 (frontend anti-patterns), §7 (testkrav), §10 (svensk copy), Clean Arch frontend-gränser
**Scope:** Frontend — presentation/interaktion. 7 filer (2 M, 3 NY, 1 D, 1 M e2e). Backend/domän/server-actions/DTO oförändrade (verifierat via git status — endast page.tsx, record-follow-up-outcome-form.tsx, applications.spec.ts modifierade; status-card.* + dess test nya; transition-form.tsx raderad).
**Avgränsning:** Design-estetik och flödesbegriplighet (ADR 0047) granskas INTE här — det är design-reviewer Area 1–5.

## Räkning

| Severity | Antal |
|---|---|
| Blocker | 0 |
| Major | 0 |
| Minor | 2 |

Inga merge-blockerande fynd. Minor är FYI, inte villkor för GO.

## Clean Arch frontend

- `page.tsx` förblir Server Component. Auth/datahämtning/error-cases (rad 17–78) intakta — `getServerSession`, `getApplicationById`, `switch(result.kind)` med `assertNever`-uttömning, redirect/notFound oförändrad logik. Ingen klient-fetch läckt in.
- `status-card.tsx` har `"use client"` korrekt motiverat: `useTransition`, `useState`, Dialog-interaktivitet, onClick-handlers. Disclosure-state kan inte vara server-side. JSDoc rad 39–44 förklarar mönstervalet (GOV.UK summary-card) — uppfyller §4.3-andan om motiverad klient-gräns.
- `record-follow-up-outcome-form.tsx` `"use client"` motiverat: `useActionState` + tvåstegs-confirm-state.
- Ingen `useEffect` för datahämtning (§5.2) — ingen `useEffect` alls i diffen. Server-action-mönster via `useActionState`/`useTransition` konsekvent med `add-follow-up-form.tsx`/`add-note-form.tsx`.
- StatusCard återanvänder samma `useTransition`+Dialog-mönster som raderade TransitionForm (verifierat mot `git show HEAD:...transition-form.tsx`) — evolution, inte parallell-uppfinning. DRY/SoC bevarad.

## §4 TS/Next.js-standarder

- Ingen `any`. `PILL_TONE: Record<string, PillTone>` är typad; `ApplicationStatus`/`ActionResult` importeras från single-source. Inga `as`-cast utan kommentar (inga cast alls).
- Fil-org §4.2: `PascalCase.tsx`, en export per fil, co-lokaliserade `.test.tsx`. Korrekt.
- Namngivning: `StatusCard`, `RecordFollowUpOutcomeForm` engelska PascalCase, rutt-svenska bevarad. Korrekt.
- Inga magic strings utanför tokens: status-värden, transitions och destruktiv-klassning kommer från `lib/applications/status.ts` (`getAllowedTransitions`, `isDestructiveTransition`, `STATUS_BADGE_VARIANT`, `getStatusLabel`). Inga hårdkodade `"Rejected"` e.d. i komponenterna. `PILL_TONE`-map mappar BadgeVariant→PillTone — acceptabel adapter, värdena speglar `BadgeVariant`-unionen i status.ts.
- Class-namn använder design-tokens (`text-text-primary`, `border-border`, `text-danger-700`) — ingen rå hex.

## §5.2 Frontend anti-patterns

Ingen `any`, ingen `console.log`, ingen `useEffect`-fetch, ingen `localStorage`, ingen `document.getElementById` i produktionskod (förekommer endast i test för region-assertion, vilket är legitimt test-bruk), inga hårdkodade strängar för domänlogik. Inga emoji/utropstecken i UI-copy. Rena.

## §7 Testkrav

`status-card.test.tsx` (9 tester) och `record-follow-up-outcome-form.test.tsx` (8 tester):

- Happy + failure täckta: non-destruktiv direkt-call, destruktiv dialog-gate, action-fel `role=alert`, confirm-avbryt, disclosure-toggle (StatusCard); confirm-stage, submit, avbryt, outcome-change-reset, fel-aria (RecordFollowUpOutcomeForm).
- Deterministiskt: `transitionStatusActionMock`/`recordFollowUpOutcomeActionMock` resettas i `beforeEach`, `mockResolvedValue`. `waitFor` för async. Inget `DateTime.Now`-ekvivalent (datum är props, inte `new Date()` i komponent).
- Mock-mönster konsekvent med repo: `vi.mock("@/lib/actions/applications")` följer samma server-action-mock-stil som övriga formulärtester. Select-mocken (record-follow-up-test rad 20–72) är välmotiverad med kommentar (Radix pointer-capture-polyfill saknas i delad setup) — pragmatiskt och dokumenterat, inte fragilt.
- Assertion på `getByRole`/`findByRole`/`getByLabelText`/aria-attribut snarare än fragil text-substring där det räknas. `toHaveTextContent` används för copy-verifiering vilket är acceptabelt här.
- e2e (`applications.spec.ts`) uppdaterad till nya interaktionssökvägar: `region name: "Status"`, `Ändra status`-disclosure, destruktiv dialog-bekräftelse. Konsekvent med ny DOM.
- E2E uppdaterad när kritiskt flöde ändrades (§7-krav) — uppfyllt.

## Dead-code-radering

`transition-form.tsx` raderad (git status: `D`). Enda kvarvarande textträff på `transition-form` är `docs/sessions/2026-05-08-0753-steg7b-frontend-cv.md` — en historisk session-log, inte kod/import. Inga kvarvarande importörer. Radering komplett — överensstämmer med CTO-beslut a870292905edc4943 (in-block dead-code-hygien, §9.6/CCP/Fowler).

## §10 Svensk copy (kod-nivå, ej ton-domen)

"du"-tilltal ("Kontrollera att det stämmer innan du sparar"), inga emoji, inga utropstecken (frågetecken i "Markera som Nekad?" är legitim fråga, ej utrop). `toLocaleDateString("sv-SE")` för datum. Inga hårdkodade engelska UI-strängar. Notera: copy-ton/microcopy-konsistens (t.ex. "Inget svar" återanvänds för både `Ghosted` och `NoResponse`) är design-reviewer/copy-skill-scope, inte kod-review — flaggas ej som fynd här men noteras för parallell granskare (jfr MEMORY badge-text cross-reference).

## Minor (FYI — ej GO-villkor)

1. **`PILL_TONE`-map dubblerar `BadgeVariant`-domänen**
   Fil: `web/jobbpilot-web/src/components/applications/status-card.tsx:30-37`
   `PILL_TONE: Record<string, PillTone>` mappar de sex `BadgeVariant`-strängarna till `PillTone`. Eftersom `BadgeVariant` är en sluten union i `status.ts:16` kunde nyckeltypen vara `Record<BadgeVariant, PillTone>` istället för `Record<string, PillTone>` — då fångar tsc en framtida `BadgeVariant`-utökning som missas i mappen vid compile-time istället för att tyst falla till `?? "neutral"`. Inte fel idag (fallback finns), men typad nyckel ger starkare invariant. Defensiv polish, ingen funktionsdefekt.

2. **`outcomeLabel`-härledning inline-ternär dubblerar `FOLLOW_UP_OUTCOME_LABELS`**
   Fil: `web/jobbpilot-web/src/components/applications/record-follow-up-outcome-form.tsx:52-57`
   `outcomeLabel` byggs via inline-ternär (`"Responded" → "Svar mottaget"` etc.) trots att `FOLLOW_UP_OUTCOME_LABELS` i `status.ts:65-69` redan mappar exakt dessa. Mild DRY-avvikelse mot single-source-label-mönstret som resten av filen (page.tsx rad 165) följer. Lokalt isolerat, inte magic-string i §5.1-mening (värdena är UI-copy, inte domännycklar), men `FOLLOW_UP_OUTCOME_LABELS[outcome as FollowUpOutcome]` vore mer konsekvent. Minor — ej blockerande.

## Bra gjort

- Server/client-gräns korrekt och motiverad i JSDoc — exemplariskt §4.3-mönster.
- Domän-irreversibilitet (Outcome) respekteras: UI kommunicerar konsekvens, server-action/domän orörd (verifierat — `applications.ts` action oförändrad i diffen). Dotnet-architect Beslut 4 hedrad i frontend.
- Status/transition-logik delegerad helt till `lib/applications/status.ts` — noll magic strings i komponenterna.
- Testtäckning stark: 17 nya tester, happy+failure+edge (avbryt, toggle, outcome-byte-reset), deterministisk mock-disciplin konsekvent med repo.
- Select-mock dokumenterad med skäl — inget tyst test-hack.
- Dead-code-radering komplett och verifierad — ingen orphan.
- e2e uppdaterad i samma batch som DOM-ändringen — ingen drift mellan lager.

## Sammanfattning

0 Blocker, 0 Major, 2 Minor (defensiv typ-styrkning + lokal DRY). Koden uppfyller CLAUDE.md §4/§5.2/§7/§10 och Clean Arch frontend-gränser. **GO för commit.**

Minor 1–2 är opportunistisk polish — ingen TD-lyftning motiverad (§9.6: hör till nuvarande fas, men är ej fas-stängningskritiska och kan fixas in-block om Klas/CTO vill, annars droppas). Rekommendation: fixa Minor 1–2 in-block i samma batch (≤6 rader vardera, ingen testpåverkan) eller medvetet avstå — inte TD-material.

Re-review ej nödvändig om Minor adresseras trivialt. Design-reviewer kör parallellt på rendered-UI/flödesbegriplighet (ADR 0047) — utanför denna rapports scope.
