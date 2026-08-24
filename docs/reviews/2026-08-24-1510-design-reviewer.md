# design-reviewer — PR #1510 (#1505), `/jobb` load-cycle live region

- **Date:** 2026-08-24
- **PR:** [#1510](https://github.com/klasolsson81/jobbliggaren/pull/1510)
- **Head reviewed:** `7adb1a0f` · **Base:** `f3846dbf`
- **Verdict:** ⚠ Changes requested — **0 Blocker, 3 Major, 2 Minor**
- **Authority:** DESIGN.md §5 (Frontend anti-patterns), `jobbpilot-design-a11y` §6 (live regions) + §10, `jobbpilot-design-copy` (empty states), ADR 0047, WCAG 4.1.3 / ARIA22
- **Transcribed by the driving session** — the charter grants `Read`/`Grep`/`Glob` and no `Write`. Findings and escalations verbatim.

## Major

### 1. Noll-träff-grenen annonserar utfallet utan nästa steg

`web/jobbliggaren-web/src/components/job-ads/jobb-results.tsx:407-413`

Nuvarande: `totalCount === 0 ? t("toolbar.noHits") : …` → skärmläsaren hör `"Inga träffar"`.
På skärmen renderar samma gren `.jp-empty` med `"Inga jobb hittades"` + `"Justera filtren eller töm sökrutan för att se fler annonser."` (`job-ad-list.tsx:70-73`, `messages/sv/jobads.json:157-158`). SR-användaren hör alltså återvändsgränden men inte vägen ut — medan samma `switch`s två felgrenar annonserar titel OCH brödtext, just för att jag på `/foretag/sok` fällde att felmeningen behöver orsak + åtgärd. Ett tomt resultat är samma form av misslyckande.

Krävs: `` `${t("list.emptyTitle")} ${t("list.emptyBody")}` `` — båda redan på skärmen, samma namespace (`jobads.ui`), fortfarande noll ny copy.

Motivering: empty states ger ett konkret nästa steg (`jobbpilot-design-copy`); ADR 0047 — uppgiften ska kunna slutföras utan gissning.

### 2. E2E-kontrollen räknar två regioner där den renderade sidan har fyra — assertionen faller som skriven

`web/jobbliggaren-web/tests/e2e/jobb-live-region.spec.ts:27, 38-47`

`FILTER_REGION = "p[aria-live='polite'].sr-only:not([aria-atomic])"` matchar TVÅ element på ett hydrerat `/jobb`: `job-ad-typeahead.tsx:287` (förslagsräknaren) och `jobb-hero-search.tsx:668` (tagg/sparad-annonsen). `toHaveCount(1)` faller. `page.test.tsx` kan inte se det — den mockar `jobb-hero-search` (rad 73) — och e2e-lanen är `continue-on-error`, så ingenting rapporterar det. Dessutom finns en fjärde region i skalet: `shell/header-stats.tsx:219-224` (en `<span>`, matchar därför ingen av lokatorerna, men gör testnamnet "exactly two regions" mätbart falskt).

`LOAD_REGION` är däremot unik — `aria-atomic` på ett `<p>` bärs bara av `announcer.tsx` — så node-identitets-testet, det som dödar M9, är korrekt.

Krävs: gör kontrollen strukturell i stället för räknande — assertera att load-regionen är descendant till `section[aria-labelledby="jobb-results-title"]`, vilket ingen hero-region kan uppfylla, och rätta namn + kommentar till den mätta siffran.

### 3. Rate limit-grenen har nu två annonseringskanaler, varav en assertive

`web/jobbliggaren-web/src/components/job-ads/jobb-results.tsx:472-483`

Kortet behåller `role="alert"` (implicit assertive) och får ett `<Announce>` som skriver samma mening i den polite regionen. Vid en client-side-navigering sätts alert-noden in i en levande DOM med sin text — det fall AT faktiskt annonserar — så meningen läses sannolikt två gånger, en av dem avbrytande. Går den inte igång är attributet i stället dött. **Båda utfallen argumenterar för strykning**, så fyndet hänger inte på vilken AT-beteende som råder.

`jobbpilot-design-a11y` §6: assertive endast för kritiska fel som ska avbryta, aldrig för rutinuppdateringar. En rate limit med retry-timer är rutin — syskongrenen `error` är rollös och visar ytans eget svar.

Krävs: stryk `role="alert"` från kortet (ren radering, noll visuell effekt — inget CSS-selektor nycklar på det, mätt). `/foretag/sok`s ErrorShell (`foretag-sok-results.tsx:246-249`) bär identisk komposition; åtgärda båda här, annars divergerar mekanismen PR:en enar redan dag två.

## Minor

### 4. `job-ad-list.tsx:64-67` — kommentarens premiss är nu falsk

Den säger att `page.tsx` "har redan en live-region på resultat-räknaren". Räknaren bär ingen region längre och låg aldrig i `page.tsx` (den bor i `jobb-results-toolbar.tsx`). Slutsatsen (ingen region här) står kvar, men av ett annat skäl. Överlappar `code-reviewer`s §5 `Comments:`-domän — men diffen skapade falskheten, så den hör hemma i denna PR.

### 5. Regionens programmatiska kontext skiljer sig mellan de två vägarna

`page.tsx:371` monterar `<Announcer>` inuti `<section aria-labelledby="jobb-results-title">`; `loading.tsx:75` monterar den direkt under `.jp-container.jp-page`. Ingen mätt effekt, men vägarna är inte symmetriska.

## Bra gjort

- Regionen är **föregående syskon** till `aria-busy`-subtree:t i båda hostarna — hade den legat inuti hade `aria-busy="true"` undertryckt just den annonsering den finns för. Rätt av konstruktion, i båda filerna.
- Den annonserade räknaren byggs av samma `formatNumber(format, …)` + `t("toolbar.hits", { count })` som toolbaren renderar — hört och sett kan inte divergera, singularis `"1 träff"` inkluderad.
- Noll CSS-, token- och message-ändringar (`git diff --stat -- '*.css' '*.json'` tomt) — "ingen ny copy" är mätt, inte påstått.

## Svar på de fem ställda punkterna

1. **Grenlistan.** Fyra är rätt, och uteslutningen av `unauthorized` håller — `redirect()` kastar innan någon nod renderas, så det finns inget status message för 4.1.3 att bita i. `default: assertNever(result)` kastar in i error boundary:n, som sedan `d3642710` flyttar fokus till sin rubrik: annan mekanism, korrekt inte denna. Alerten döljer däremot något — Major 3.
2. **Ordagrant.** `"1 234 träffar"` är rätt mening. Den följer `"Söker bland annonser…"` i samma region, och regionen ligger i `<section aria-labelledby="jobb-results-title">` = "Lediga jobb", så kontexten är programmatisk och inte beroende av synligt sammanhang; Understanding 4.1.3:s eget exempel är "18 results returned". Den mening som inte är rätt är noll-grenens — Major 1.
3. **Två regioner.** Det är **fyra**, inte två (Major 2). Doktrinen håller ändå: fyra jobb, fyra regioner, alla polite — de köar i stället för att krocka. Vid en filtercommit blir sekvensen "…tillagt" → "Söker bland annonser…" → "N träffar", vilket är en sammanhängande berättelse, inte en kollision. Det som är fel är siffran i specen och resonemanget som byggdes på den.
4. **`aria-busy`.** Mätningen står sig — `aria-busy` är en global ARIA-state applicerad på `roletype` och förutsätter ingen live-region; CTO:ns premiss var falsk och återtagandet rätt. Vad mätningen fastställer är **giltighet, inte verkan**: på en `generic`-div utanför varje live-region är attributet inert. Inert + giltigt + skrivet skäl + paritet med `/foretag/sok` ⇒ behåll. Inget fynd. Det som faktiskt betyder något här är placeringen, och den är rätt (se Bra gjort).
5. **Visuellt — mätt, ingen delta.** `.jp-results-count` stilas enbart via klass (`globals.css:2660` `.jp-results-count`, `:2664` `.jp-results-count b`). Repot har exakt två CSS-filer (`src/app/globals.css`, `src/app/(app)/app.css`) och ingen av dem innehåller en attributselektor på `role`/`aria-live` — de två `role=`-träffarna i `globals.css` (rad 1731, 6679) ligger båda inuti kommentarer. Diffen rör ingen `.css` och ingen `.json`. Att tappa de två attributen kan alltså inte ändra rendering, i något tema.

## Icke-tagen mätning (inte godkännande)

**Rendered verify ej utförd.** API:t på 5049 kör inte och `/jobb` är session-gated, så jag nådde ytan varken i light eller dark, och e2e-specen som bär placeringsgarantin kunde inte köras heller. Det som *bound:ar* risken, och som inte ersätter mätningen: noll CSS/JSON i diffen, ingen ny klass, och de enda DOM-deltana är två borttagna ARIA-attribut plus ett tomt `sr-only <p>` — identisk konstruktion som redan levererat inuti samma `.jp-container.jp-page`-wrapper på `/foretag/sok`. **Dark mode:** ingen färg-, token- eller CSS-ändring finns i diffen, så det finns inget tema-beroende att validera; det är resonemang, inte en renderad mätning.

## Sammanfattning

0 Blocker, 3 Major, 2 Minor. Majors → in-block (alla tre är små: ett uttryck, en lokator, en attributradering). Minor 4 → fix in-block eller namngiven skip i PR-body; Minor 5 → namngiven skip. Re-review efter fix: samma agent, report-only, skopad till fix-deltat (CLAUDE.md §9.6).
