# test-writer — PR #1510 (#1505), SCOPAD OMKONTROLL

- **Date:** 2026-08-24
- **PR:** [#1510](https://github.com/klasolsson81/jobbliggaren/pull/1510)
- **Scope:** `git diff 7adb1a0f 1ba31db7` (10 filer, +133/−46). Report-only.
- **Egna mätningar:** 6 berörda testfiler körda — **63 tester, 63 passed**.
- **Verdikt:** **3/3 Major stängda** (Major 1 med anmärkning). Inga nya Blocker.

## Major 1 (e2e `FILTER_REGION` matchar två) — **STÄNGT MED ANMÄRKNING**

Fyndet stängt med rätt mekanism. `section[aria-labelledby="jobb-results-title"]` finns på `page.tsx:365`, `<Announcer>` på `:378` — inuti. Båda hero-regionerna ligger strukturellt utanför. `toHaveCount(1)` på `LOAD_REGION` är därmed **hydration-invariant**. *"Att byta från attribut-diskriminator till struktur är rätt val, inte bara ett fungerande."*

**Anmärkning: hydrationsväntan är inert, och kommentaren över den är faktiskt fel.**

`jobb-hero-search.tsx:591-602` renderar i den icke-hydrerade grenen ett rått fält under samma label — server-renderat och **enabled**. `getByLabel(...).toBeEnabled()` uppfylls av pre-hydration-DOM:en och väntar aldrig på hydration.

Det gör ingen assertion fel: **ingen assertion i testet beror längre på hydration.** `LOAD_REGION` är strukturell, och den positiva kontrollens mål (`jobb-hero-search.tsx:668-670`) renderas **ovillkorligt** — bara typeaheaden (`:579`) och clear-knappen (`:675`) är `hydrated`-grindade.

*"Jag skulle stryka väntan och dess två kommentarsrader: strykning stänger mekaniskt, och en rad som påstår en garanti den inte har är sämre än ingen rad."*

**En mätning hon inte har:** om Playwrights CSS-motor parsar `:not(section[...] *)` (Selectors 4). *"Om den inte parsar felar testet högljutt i stället för att passera fel, så riktningen är säker — men påstå inte att raden är verifierad."*

## Major 2 (räknarens rollborttagning opinnad) — **STÄNGT**

Pinnen använder `not.toHaveAttribute("role")` — **starkare** än `not.toHaveAttribute("role", "status")`: *"den fångar varje roll, inte bara den som råkade tas bort."* M11 KILLED verifierad grön.

Den strukna `issue #292`-kommentaren var rätt att stryka. Ingen information tappad. Följden (filen är `+9/−3`, alltså inte längre mekanisk) är **`code-reviewer`s att avgöra, inte min**.

## Major 3 (`aria-label`/`aria-labelledby`-negationerna) — **STÄNGT**

Återinförda på `announcer.test.tsx:37-40` med `nameFrom: author`-skälet skrivet. Rätt hem: `Announcer` är det enda elementet som renderar regionen, delad av fyra mount-sites över två ytor.

## Den tionde mutationen — tredje formen är RÄTT

Rätt av tre mätta skäl: den flyttade till **hosten** (överlevare 1 bevisade nödvändigt), den asserterar **tomhet** i stället för frånvaron av en viss mening (överlevare 2 bevisade nödvändigt), och filen pinnar nu **båda halvorna av ARIA22-ordningen**.

`[\s\S]` framför `s`-flaggan: *"skälet du skrev är korrekt, inte folklore"* — `tsconfig.json:3` är `"target": "ES2017"`, genuint TS1501.

### ⚠ Den FJÄRDE mutationen: attribut-sådd

`role=status` är `nameFrom: author`, så en mutation som sår annonseringen som `aria-label` på regionen ger en uppläst label och **tomt textinnehåll** — `region?.[1]` är fortfarande `""` och pinnen **passerar**. Den parametriserade formen (`<Announcer initialLabel=…>` som bara `loading.tsx` skickar) lämnar `announcer.test.tsx` grön, och `loading.test.tsx` gör enbart positiva `toContain`-assertions.

*"Det är ett **deduktivt** resultat, inte ett mätt … Jag har inte byggt mutanten — det vore en editering i `src/**` som min charter förbjuder."*

Stängs av en rad: `expect(region?.[0]).not.toContain("aria-label");` — **Minor**. *"Fix eller namngiven skip; fixen är billigare än skippens mening."*

## Minor — stubbarna: **FYNDET HÅLLER INTE** (hon drar tillbaka det på mätning)

- `taxonomy.ts:83` — `if (ids.length === 0) return { kind: "ok", data: [] };`
- `me-jobs.ts:19-20` — `if (!sessionId) return { kind: "unauthorized" };`
- Samma form genomgående i `src/lib/api/` (`job-ads.ts:130-131, :162-163, :200-201, :253-254`)

*"Varje stubbat returvärde är alltså ett värde den **riktiga adaptern bevisligen emitterar**. Under §5 `Tests:` bär premissen då ingen skyldighet alls."* PR-kroppen ska säga **"premissen verifierad trogen produktionsvägen, med filreferens"** i stället för det svagare "inte load-bearing" — *"ett strikt starkare påstående"*. §9.6:s tredje utfall, inte en väg ut.

## Minor — transitionspinnen: **NAMNGIVEN SKIP, inte en Major**

Båda halvorna är pinnade var för sig på `/jobb`, och **överskrivningsmekanismen själv** är pinnad på `announcer.test.tsx`s *"replaces the sentence when a later Announce supersedes it"*. Write-once-klassen dör där. *"Dess marginella kill-mängd på `/jobb` är tom så långt jag kan namnge en mutation. Verklig asymmetri, inget mätt hål."*

## Harness-mätningen — regeln är generell

> En applied-kontroll mäts på **artefakten testet konsumerar** — renderad DOM eller emitterad HTML — aldrig på filens bytes. `replace(…, 1)` är ankarordningsberoende, och i det här repot inleds nästan varje fil med ett docblock som namnger just det den renderar. **En byte-diff bevisar att en fil ändrades, inte att kod ändrades.**

Billig kontroll: verifiera efter mutation att det baseline-test som *borde* fälla faktiskt fäller. Alternativt: strippa kommentarer innan ankaret lokaliseras.

Det retro-validerar KILL:en: tredje formen skrev `expected 'Sök jobb' to be ''`, vilket bevisar att mutanten **exekverade** — en docblock-träff kan inte producera en infångad sträng.

---

## Driving session — vad som gjordes

- **Hydrationsväntan struken** i `282ae693` (hon och `design-reviewer` fann den oberoende). Positiv kontroll bytt till `section.jp-hero p[aria-live='polite'].sr-only`, vilket också tar bort den oprövade `:not()`-konstruktionen hon flaggade.
- **Fjärde mutationen MÄTT innan fix.** Byggd som `initialLabel`-prop + host som skickar den: **överlevde alla åtta tester** i de två sviterna. Hennes deduktion bekräftad. Hennes enrading tillagd i `d2a4103a` och verifierad genom att bygga om mutanten: **KILLED** (`expected ... not to contain 'aria-label'`). Routad till `senior-cto-advisor` eftersom `code-reviewer`s tak var förbrukat när den landade.
- **Stubb-tillbakadragandet** skrivet i PR-kroppen i den starkare formen hon bad om, med filreferenser.
- **Transitionspinnen** namngiven skip i PR-kroppen, med asymmetrin utskriven.
