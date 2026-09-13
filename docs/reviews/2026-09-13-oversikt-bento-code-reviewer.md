# Code-review: `/oversikt` bento-grid, frontend + tracked docs (PR #1724)

**Agent:** code-reviewer · **Datum:** 2026-09-13 · **Head:** `bbac14b2` · **Issue:** #1723
**Status:** ⚠ Changes requested
**Auktoritet:** AGENTS.md §4, §5 (`Comments:`, `Tests:`), §7 · CLAUDE.md §9.6 · DESIGN.md §5
**Scope:** Next/React/TS + CSS + i18n + ADR/skill. Områdena 1–3 (Clean Architecture, DDD, CQRS) aktiveras inte — inget backend-delta. Områdena 4–6 körda. Design-reviewers fynd omgraderas inte (§9.6: severity tillhör den som rapporterade).

### Major

1. **`CriterionAdLines` doktrin-docblock säger fortfarande "three props" efter att deltat lagt till en fjärde** — Fil: `web/jobbliggaren-web/src/components/company-criteria/criterion-ad-lines.tsx:31`
   Nuvarande: `* actionOfferedByCaller. Three independent facts, three props — see each prop.` — medan den nya propen på rad 89 i samma fil öppnar med `<b>A FOURTH independent fact.</b>`. Filen motsäger sig själv på två skärmhöjders avstånd, och rad 31 är typens docblock, alltså den mening en läsare möter först och den enda som bär doktrinen samlat.
   Krävs: rätt antal, eller stryk räkneordet. Det är ett tal i en kommentar och deltat är det som gjorde det falskt — `senior-cto-advisor` D1 valde formen "N självständiga fakta, N props" just för att antalet ska kunna läsas av.
   Motivering: AGENTS.md §5 `Comments:` — "A factually wrong comment — wrong number … is a defect and is fixed." Charterns gradering: Major.
   Delegera till: `nextjs-ui-engineer`

2. **Sju levande pekare till filer och komponenter som deltat raderar** — Filer:
   `src/components/oversikt/company-summary.tsx:155` ("där **SetupCallout** redan står med samma mål" — raderad; och `CompanySummary` renderas nu bara av gäst-ytan, som aldrig bar den)
   `src/app/globals.css:5290` (sektionsrubriken räknar upp "åtgärds-kort (**setup-callout**)" medan samma diff raderar hela `.jp-callout*`-blocket ur filen)
   `src/components/company-criteria/criterion-breadth.tsx:3` ("Parity `criterion-row.tsx` / **`criteria-summary.tsx`**")
   `src/components/company-criteria/criterion-row.tsx:57` ("the criterion detail page and **`CriteriaSummary`** are the other two")
   `src/components/company-criteria/criteria-section.tsx:41` ("Parity **`criteria-summary.tsx`**, deliberately not extracted")
   `src/app/(app)/foretag/branschbevakningar/[id]/page.tsx:179` ("the same reason **`CriteriaSummary`** passes `false` at N=1")
   `src/app/(app)/foretag/branschbevakningar/[id]/page.test.tsx:131` ("**`criteria-summary.test.tsx`** proves /oversikt renders it" — den meningen är hela skälet till att call-site-pinnen finns, och filen finns inte)
   Nuvarande: alla sju är *levande* påståenden (paritet, uppräkning, bevisföring), inte proveniens — en läsare som följer dem landar i ingenting.
   Krävs: peka om till `CriteriaCard` / `criteria-card.test.tsx` / `MatchingCard`s setup-arm, eller stryk meningen. De två första ligger i filer deltat redan rör.
   Motivering: AGENTS.md §5 `Comments:` (stale referens = defekt) + §1.6 (en pekare framåt in i ingenting konverteras när någon rör filen).
   Delegera till: `nextjs-ui-engineer`

### Minor

3. **`.jp-notice-prefs__grouptitle:first-of-type` matchar aldrig** — Fil: `src/app/globals.css:5411`
   Nuvarande: `:first-of-type` väljer på *elementtyp*, inte klass. Första `div`-syskonet i `.jp-notice-prefs` är `.jp-notice-prefs__heading` (`notice-prefs-popover.tsx:125`), så regeln är inert och första grupprubriken behåller sin `border-top` + `margin-top: 4px` tvärtemot vad regeln säger sig göra.
   Krävs: `.jp-notice-prefs__heading + .jp-notice-prefs__grouptitle` eller motsvarande — annars stryk regeln. Renderingen är redan godkänd av design-reviewer, så det är död kod som läser som levande.
   Delegera till: `nextjs-ui-engineer`

4. **Inert Tailwind-utility ovanpå en olagrad `.jp-*`-regel som sätter samma egenskap** — Fil: `src/components/oversikt/criteria-card.tsx:163`
   Nuvarande: `className="jp-ov-card__count tabular-nums"`, men `app.css:2152-2156` sätter redan `font-variant-numeric: tabular-nums` på klassen — olagrad, alltså vinner den. `notice-list-card.tsx:78` använder samma klass utan utilityn.
   Krävs: ta bort `tabular-nums`. Det är precis den fälla filens grannkommentar `globals.css:5157` skriver ut ("sätts HÄR och aldrig som Tailwind-utility i JSX").
   Delegera till: `nextjs-ui-engineer`

5. **`"use client"` utan motivering där koden inte visar skälet** — Filer: `src/components/oversikt/requires-you-card.tsx:1`, `src/components/oversikt/recent-events-card.tsx:1`
   Nuvarande: båda saknar state, handler och hook utöver `useTranslations` — enda skälet är att `renderRow` är en funktionsprop till en klientkomponent och inte kan passera RSC-gränsen. En läsare som försöker göra dem till RSC får ett ogenomskinligt serialiseringsfel. (`use-notice-list.ts`, `notice-prefs-popover.tsx`, `notice-list-card.tsx` motiverar sina direktiv i docblocken; `notice-dismiss-button.tsx` visar skälet i koden — inget fynd där.)
   Krävs: en rad i docblocken. Detta är den enda kommentar chartern kräver (AGENTS.md §4 "Server Components by default").
   Delegera till: `nextjs-ui-engineer`

### Bra gjort
- Ohederliga nollor finns ingenstans: `OversiktNumber(null)` ger en-dash utan enhet och utan CTA i alla fyra korten, och `criteria-card.tsx:113-115` *läser* zod-refinementet i stället för att upprepa det.
- `summariseWatches` / `applicationBars` är rena och testade mot handler-formade fixtures som namnger sin producerande aktör (`watch-summary.test.ts:6-8`) — AGENTS.md §5 `Tests:` klarnar, även det blandade null/tal-läget som är deklarerat oproducerbart och bara påstår säker degradering.
- Tre fokuskontrakt (två `useNoticeList` + popovern) delar `dismissed`-nyckeln utan kollision: varje effekt är grindad på sin egen ref-flagga, och barn-före-förälder-ordningen kan inte trigga två samtidigt.

### Sammanfattning
0 blockers, 2 major, 3 minor. Majors är merge-blockerande (CLAUDE.md §6/§12) och fixas in-block — båda stängs genom att stryka eller rätta en mening, ingen ny prosa behövs. Kontrollerat rent: RSC↔client-gränsen (inget nytt icke-serialiserbart; `SectionNoticeData.text` korsar samma gräns som förut), `matchingStatedByCaller` (docblockets "three of the four callers" stämmer — fyra anropsställen, ett sant), `MaxPerUser = 20`, `OrderByDescending(CreatedAt)`, `ux_company_watches_user_orgnr_active`, terminal = 10 − 6 = 4, `ref`-mergen i popovern (React 19-prop, lint-ren), `loading.tsx` sätter inga egenskaper som `.jp-skeleton` äger, samt noll `any`/`console.log`/`useEffect`-fetch. Inget i deltat är en genuint tvetydig routing-fråga för `senior-cto-advisor` — utom dispositionen av de tre Minors mot §9.6:s filnings-cap, som är sessionens att avgöra (issue eller namngiven skip i PR-kroppen; en rad utan namngivning är en utelämning, inte ett undantag). Re-review efter fix: samma agent, report-only, skopad till fix-deltat (CLAUDE.md §9.6).

---

## Omkontroll (rapport-only, skopad till fix-deltat `bbac14b2..8a8edfce`)

**Status:** ✓ Approved — mina fem fynd stängda
**Auktoritet:** AGENTS.md §4, §5 (`Comments:`, `Tests:`) · CLAUDE.md §9.6
**Scope:** enbart fix-deltat för mina egna fynd. Mätt på **committarna** (`git show <sha>:<fil>`), inte arbetsträdet.

### Fynd-status

1. **Major 1 — antalet i doktrin-docblocken** — STÄNGT · `criterion-ad-lines.tsx:31-32`. Mätning på `8a8edfce`: "…whether the caller renders the matching number itself is `omitMatchingCount`. Four independent facts, four props — see each prop." Talet stämmer mot faktisk arity (`variant` · `adviceStatedByCaller` · `actionOfferedByCaller` · `omitMatchingCount`). Omdöpningen nådde anropsstället (`criteria-card.tsx:133`), samma polaritet och grenposition. "three of the four callers" på rad 97 är fortsatt sant.

2. **Major 2 — sju levande pekare till raderade filer** — STÄNGT (7/7). Mätning per rad i `b0e98b75`: `company-summary.tsx:155` satsen raderad · `globals.css:5289` posten raderad ur rubriken · `criterion-breadth.tsx:3` → `criteria-card.tsx` · `criteria-section.tsx:41` → `criteria-card.tsx` · `criterion-row.tsx:57` → `CriteriaCard` · `[id]/page.tsx:179` → `CriteriaCard` · `[id]/page.test.tsx:131` → `criteria-card.test.tsx`. Kontrollgrep över det committade trädet ger tre kvarvarande träffar, ingen av dem ett av de sju ställena: `guest-oversikt-page.tsx:45` (utanför deltat, namngiven skip per AGENTS.md §1.6 / #1585), samt `oversikt-page.tsx:156` och `oversikt-page.test.tsx:299`, som använder "setup-callouten" som begrepp, inte som filpekare.

3. **Minor 3 — inert `:first-of-type`** — STÄNGT · `globals.css:5411` `.jp-notice-prefs__heading + .jp-notice-prefs__grouptitle`. Rubriken är alltid panelens första barn, så angränsande syskon = första grupprubriken. I en-grupps-popovern renderas ingen grupprubrik, så regeln är inert per konstruktion där — rätt.

4. **Minor 4 — inert Tailwind-utility** — STÄNGT · `criteria-card.tsx:163` `className="jp-ov-card__count"`. `app.css:2155` sätter fortfarande `tabular-nums` på klassen; oförändrad rendering, paritet med `notice-list-card.tsx:78`.

5. **Minor 5 — omotiverat `"use client"`** — STÄNGT · `requires-you-card.tsx:24-26`, `recent-events-card.tsx:24-26`. En mening i var docblock namnger skälet koden inte kan visa själv.

### Sammanfattning
5 av 5 stängda (2 major, 3 minor), 0 kvarstående, 0 nya fynd. Kontrollerat `8a8edfce` där den rör fynden, plus de två ställen där ett nytt fynd hade fått passera spärren: partitionstesten i `application-bars.test.ts` vilar på en premiss produktionen producerar (AGENTS.md §5 `Tests:` klarnar); den raderade `notices.calloutLabel` har noll referenser på commiten. Inget är Blocker eller repo-brett. Klar för `agents-done` när övriga agenter stängt sina fynd; verifiera HEAD oförändrat omedelbart innan etiketten sätts (CLAUDE.md §9.6).
