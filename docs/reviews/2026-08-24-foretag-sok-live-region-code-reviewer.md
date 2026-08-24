# Code-review: /foretag/sok live region (PR #1504)

**Agent:** `code-reviewer` · **Datum:** 2026-08-24 · **PR:** [#1504](https://github.com/klasolsson81/jobbliggaren/pull/1504)
**Gren:** `fix/foretag-sok-live-region-1092` · **Delta:** `origin/main...HEAD`, merge-base `207e9bb4` · **Head:** `1b15966b`
**Status:** ⛔ Blocked
**Auktoritet:** CLAUDE.md §5 `Tests:`, §5 `Comments:`, §7, §9.6, §12, §4
**Scope:** FE only — 9 filer, 384+/29−. Ingen BE-kod i deltat, så granskningsområde 1–3
(Clean Architecture, DDD, CQRS) är inte tillämpliga. 4–6 kördes.

> **Transkriberad av den anropande sessionen.** Agentens charter är report-only och harnessen
> förbjöd henne att skapa report-filen. CLAUDE.md §9.2 lägger transkriptionsplikten på den anropande
> sessionen och binder den vid charterns Output format-cap. Innehållet nedan är hennes, ordagrant.

**1 Blocker / 4 Major / 4 Minor**

## Blockers

**1. Testet för cleanup-blanken vilar på ett tillstånd `src/` inte producerar**
Fil: `web/jobbliggaren-web/src/components/company-criteria/foretag-sok-announcer.test.tsx:69-94`

Nuvarande: `Harness` växlar `show` så att en `Announce` unmountas **ensam i en egen commit**, utan
att någon annan mountas i samma commit. Testnamnet och docblocken hävdar ett produktionsfaktum —
*"so an identical repeat still mutates"*, *"a second search returning the same count as the first
would arrive in silence … here it is closed rather than declared"*.

Mätt (React 19.2.8, jsdom, riktig `<Suspense key>`-växling; skript i scratchpad, **ingen repo-fil
rörd**):

| Scenario | MED blank | UTAN blank |
|---|---|---|
| key-byte, fallback **committar** (normala cykeln) | 2 mutationer | 2 mutationer |
| key-byte, fallback **hoppas över** | **0** | **0** |
| unmount ensam + remount i **senare** commit | 2 | 0 |

Rad 1 falsifierar påståendet direkt: skelettets avvikande mening (`"Söker företag…"`) är det som gör
att en identisk siffra läses upp igen — blanken bidrar noll. Rad 2 visar att hålet **inte** stängs:
React batchar unmount-cleanupens `setMessage("")` med nästa mounts `setMessage(msg)` i samma
passive-effect-flush, så `""` committas aldrig. Rad 3 är enda formen där blanken gör något, och den
är `Harness`ens form.

Krävs: §5 `Tests:` — namnge aktören som producerar tillståndet och assertera dess predikat, **eller**
deklarera tillståndet onåbart och assertera bara att läsidan degraderar säkert. Aldrig vad
produktionen gör. Praktiskt: stryk testet plus påståendet, eller skriv om till ren
kontraktsassertion utan produktionsanspråk.
Motivering: CLAUDE.md §5 `Tests:` → §12 STOPP-klass (PR:en rider inte automerge förrän fyndet är
löst; löst in-block finns ingen STOPP kvar att ta).
Delegera till: **test-writer**

## Major

**1. Deltat bryter ett e2e-test som ingen körning i denna PR rör**
Fil: `web/jobbliggaren-web/tests/e2e/foretag-sok-live-commit.spec.ts:155-159`

```ts
const region = page.locator("p[aria-live='polite'].sr-only");
await expect(region).toHaveCount(1);
```

Nuvarande: `/foretag/sok` renderar efter deltat **två** element som matchar den locatorn —
`foretag-sok-searchbar.tsx:1014` och nya `foretag-sok-announcer.tsx:33`. `toHaveCount(1)` ser 2;
efterföljande `region.textContent()` blir dessutom en strict-mode-violation. `vitest.config.ts:14`
inkluderar bara `src/**`, så de 313 filerna / 3616 testen rörde aldrig den här filen — och e2e-lanen
är `continue-on-error`, så **ingenting kommer att rapportera detta**.
Krävs: locatorn skärps (announcern bär `aria-atomic="true"`, searchbarens region gör inte det) eller
skopas till searchbaren. AGENTS.md §7: "E2E updated when critical flows change".
Delegera till: **nextjs-ui-engineer**

**2. Felgrenarna annonserar en start som aldrig stängs**
Fil: `src/components/company-criteria/foretag-sok-results.tsx:59-64` mot `:97-99`

Nuvarande: `rateLimited`/`notFound`/`forbidden`/`error` returnerar `<ErrorShell>` **före**
`<Announce>` på rad 109. Skelettet har då annonserat "Söker företag…" genom den nya, tillförlitliga
regionen; cleanup-blanken tömmer den; ingenting sätter en ny mening. `ErrorShell` bär `role="alert"`
monterad med sin text redan på plats — precis den ARIA22-form PR:en finns för att ta bort. PR:ens
egen regel på rad 97-99 säger: *"announcing the start and then never closing it would leave a screen
reader waiting on a load that has in fact finished, which is worse than the silence it replaced."*
Fyra nåbara grenar följer inte den regeln. Detta är också enda produktionsvägen där blanken faktiskt
committar.
Krävs: felgrenarna stänger loopen genom samma region (t.ex. `<Announce message={t("loadErrorTitle")} />`
i `ErrorShell`), plus ett test per gren.
Delegera till: **nextjs-ui-engineer**

**3. Ingen call-site-pin på kompositionen — hela mekanismen kan raderas med grön svit**
Fil: `src/app/(app)/foretag/sok/page.tsx:164` och `loading.tsx:29`

Nuvarande: inget test importerar `page.tsx` eller `loading.tsx` för det här. Ta bort
`<ForetagSokAnnouncer>` från `page.tsx` och alla 18 nya/ändrade test förblir gröna medan
`/foretag/sok` annonserar ingenting — `Announce` är inert utan provider by design, så det finns
varken typfel eller runtime-signal. Detta är ordagrant den inversion som
`foretag-sok-results.test.tsx:52-59` själv formulerar som husregel: *"the rule pinned, the call site
not."*
Krävs: en assertion i befintliga `src/app/(app)/foretag/sok/page.test.tsx` (som redan gör
`render(await renderPage(...))`) att regionen finns och är tom, samt motsvarande för `loading.tsx`.
Delegera till: **test-writer**

**4. Faktiskt felaktig kommentar om cleanup-blanken**
Fil: `src/components/company-criteria/foretag-sok-announcer.tsx:44-49`

Nuvarande: *"The cleanup blank is load-bearing, not tidiness … Without it a search returning the same
count as the previous one would be silent at the moment its results arrive."* Mätningen ovan
(Blocker 1) falsifierar båda leden: på den normala vägen är 2 = 2, på vägen utan fallback är 0 = 0.
Blanken är inert för det den påstås lösa.
Krävs: §5 `Comments:` — en faktiskt felaktig kommentar är en defekt och rättas. Stryk påståendet
(§9.6 stänger helst genom radering); om blanken behålls, beskriv vad den faktiskt gör (tömmer
regionen när ingen `Announce` ersätter den — dvs. felgrenarna).
Motivering: CLAUDE.md §5 `Comments:`. **Inte** §12-STOPP — `Comments:` är uttryckligen undantaget —
men Major är merge-blockerande per §6/§9.6.
Delegera till: **nextjs-ui-engineer**

## Minor

1. `foretag-sok-results-skeleton.tsx:13` — *"which is in the DOM and empty before this subtree
   exists"* är falskt för `loading.tsx`-värden: där skapas regionen och skelettet i **samma** commit.
   Det bärande och sanna är "före **meddelandet**", vilket är vad ARIA22 kräver.
2. `foretag-sok-announcer.tsx:23` — *"roughly a frame apart"* mot grannfilens mätta 158 ms
   (`foretag-sok-searchbar.tsx:104`). ~10× fel; klausulen är strykbar utan att argumentet faller.
3. `foretag-sok-results.test.tsx:413` — *"Exactly one region on the surface"*. Ytan renderar minst två
   `role="status"` (searchbaren `:1014` plus denna). Assertionen själv är korrekt skopad till
   renderingen; det är meningen som är fel.
4. `foretag-sok-searchbar.tsx:104` — *"the skeleton appears at 158 ms already carrying
   `loadingResults` in its own `role="status"`"* är nu inaktuell; deltat tog bort den rollen.
   Graderad Minor och inte Major eftersom meningen är en **daterad historisk mätning** (§5:s
   provenance-undantag) och slutsatsen den bär — ingen extra pending-rad behövs — är oförändrad.

*(Observation, inte fynd: `foretag-sok-searchbar.tsx:1012` pekar på `jobb-hero-search.tsx:634`;
regionen ligger på `:668`. Pre-existerande drift, inte skapad av detta delta.)*

## Bra gjort

- Grundfixen är rätt: en persistent, tom region utanför `<Suspense key>` är den enda formen som
  klarar ARIA22, och `useEffect`-routningen ger regionen en egen commit före meddelandet.
- Fyra befintliga case flyttade från `getByRole("status")` till `.jp-results-count` **utan att
  försvagas** — de asserterade vilken siffra som renderas, det gör de fortfarande, och annonseringen
  är separat pinnad.
- De nya `renderHosted`-casen är **inte** vakuösa: de wrappar i `ForetagSokAnnouncer`, vilket är
  produktionens egen komposition, och tomträff-fallet täcker en väg som tidigare var helt tyst.

## Svar på de fem frågorna

1. **Cleanup-blanken:** resonemanget håller inte, och implementationen uppnår inte det som påstås —
   mätt, se Blocker 1. **Ordningsrisken finns däremot inte:** React kör passive **unmount**-effekter
   före passive **mount**-effekter i samma commit, så en cleanup blankar aldrig en färsk mening.
   Slutläget var korrekt i varje mätt scenario.
2. **Context-nullability:** att tolerera null är rätt val i sig (ett kast skulle fälla ett
   loading-state), och testet på `:96` pinnar avsikten. Men det är **just** den toleransen som gör
   Major 3 osynlig — därför är svaret "tolerera null **och** pinna call-siten", inte det ena eller
   det andra.
3. **Teständringarna:** inte försvagade, inte vakuösa. Se Bra gjort.
4. **Kommentarer:** en Major (announcern) och fyra Minor, varav en i en fil deltat inte rör
   (`foretag-sok-searchbar.tsx:104`).
5. **Vad deltat bröt annanstans:** e2e-specen (Major 1) — den enda mätning som saknades i din
   uppräkning, eftersom `pnpm test` inte når `tests/e2e/`.

## Sammanfattning

**1 Blocker, 4 Major, 4 Minor.** Blocker 1 är en §5 `Tests:`-överträdelse och därmed §12-STOPP-klass
— löses den in-block finns ingen STOPP kvar att flagga. Ingen Major får filas som issue (§9.6).
Re-review efter fix: samma agent, report-only, skopad till fix-deltat.

---

# Scopad omkontroll (report-only) — fix-delta `1b15966b..a8ba5461`

**Status:** ⚠ Changes requested — 9/9 egna fynd CLOSED, **2 nya Major i deltat** · **Head:** `a8ba5461`
**Kvot:** förbrukad (CLAUDE.md §9.6)

## Mina nio — status

| # | Fynd | Status | Mätning |
|---|---|---|---|
| B1 | Cleanup-blankens test vilar på ett tillstånd `src/` ej producerar | **CLOSED** | Blanken borta (`foretag-sok-announcer.tsx:62-68`); testet är nu ett supersessionskontrakt om komponenten själv, inget produktionspåstående. §5 `Tests:` triggar inte: premissen (en monterad `Announce` får ny `message`) produceras av komponentens egen API. |
| M1 | Trasig e2e-spec | **CLOSED** | `:not([aria-atomic])` + positiv kontroll. Verifierat: searchbarens region `:1014` sätter ingen `aria-atomic`-**attribut** (React serialiserar inte implicit ARIA), och `grep -rln "foretag/sok" tests/e2e/` → **1 fil**, så ingen annan spec bröts. `tsconfig.json:26-34` = `**/*.ts`, exclude bara `node_modules` → `tsc` typkollar specen; `vitest.config.ts:14` gör det fortfarande inte. |
| M2 | Felgrenarna stänger ej loopen | **CLOSED** (se not) | `ErrorShell` bär `<Announce>`; alla fyra grenar täckta. |
| M3 | Ingen call-site-pin | **CLOSED** | `page.test.tsx:216-225` + `loading.test.tsx`. Icke-vakuösa: searchbaren är mockad (`page.test.tsx:27-29`) och `foretag-sok-subnav` bär varken `role` eller `aria-live` (grep exit 1), så ingen decoy kan fånga `querySelector`. |
| M4 | Faktiskt felaktig kommentar om blanken | **CLOSED** | `"cleanup blank is load-bearing"` → 0 träffar. |
| m1 | skeleton `:13` | **CLOSED** | Säger nu "holds its role before the message reaches it" — sant även för `loading.tsx`-värden. |
| m2 | announcer `:23` "roughly a frame apart" | **EJ CLOSED — uppgraderad, se ny Major 2** | Fel siffra ströks; ersattes av en fel **mekanism**. |
| m3 | results.test `:413` | **CLOSED** | `:404-407` säger RESULTS SUBTREE och namnger de andra. |
| m4 | searchbar `:104` | **CLOSED** | `in its own \`role="status"\`` → 0 träffar. |

**Not till M2:** min stängning gäller **att announce-vägen finns**. `design-reviewer` har ett öppet
Major på exakt samma rad (`foretag-sok-results.tsx:247`). Hennes grad, inte min att omgradera (§9.6).
Läs inte min CLOSED som klartecken för `agents-done`.

## Sessionens tre frågor

**(1) Transitionstestet är genuint icke-vakuöst.** Det asserterar först att öppningsmeningen *är* i
regionen (faller om `Announce` tas ur skelettet eller providern saknas), sedan att den ersatts.
**`rerender` med en awaitad Server Component är en sekvens produktionen producerar:** `page.tsx:164-174`
monterar **en** `ForetagSokAnnouncer` **utanför** `<Suspense key>`; fallbacken monteras under samma
provider och byts sedan mot de lösta resultaten. `rerender` som byter providerns children reproducerar
exakt det (samma typ, samma position → ingen remount, state bevarat). §5 `Tests:` fäster vid
**assertionen**, inte sömmen. Den enda produktionsdetalj sömmen inte återger (~158 ms mellan
commit:arna) bär ingen assertion.

**(2) Uppräkningen är uttömmande — med en sjunde väg sessionen inte räknade, och den är ofarlig.**
ok/räknare · ok/browse-all · ok/tomt · fyra felgrenar · `unauthorized`→redirect — verifierade mot
`foretag-sok-results.tsx:54-65,100-109`. Den sjunde är ett **kast** ur `searchCompanies`/
`getCompanyWatchStatusByOrgNr`. Mätt: det finns **ingen** `error.tsx` i `foretag/sok/` eller
`foretag/`; närmaste boundary är `src/app/(app)/error.tsx`, som ligger **ovanför** `page.tsx` och
därför river providern med sig — regionen **förstörs**, ingen mening blir stående (och `d3642710`
flyttar fokus till boundaryns rubrik). `searchCompanies` fångar dessutom själv
(`company-search.ts:77-79`).

**(3) En av de nya pinnarna kan tillfredsställas vakuöst.** Se ny Major 1.

## Nya Major i deltat

**1. Pinnen "keeps the region OUTSIDE the Suspense boundary" kan inte observera egenskapen den
namnger**
Fil: `web/jobbliggaren-web/src/app/(app)/foretag/sok/page.test.tsx:227-237`

Testet asserterar `live.contains(results) === false` och `results.contains(live) === false` — alltså
enbart att de två noderna inte är ancestor/descendant. Docblocken hävdar något annat: *"Placement is
the whole mechanism … Siblinghood is what survives the swap."*

Mätt (React 19.2.8, repots egen `node_modules`, ingen repo-fil rörd):

```
region OUTSIDE boundary : <div><p role="status" aria-live="polite"></p><div data-testid="results"></div></div>
region INSIDE  boundary : <div><p role="status" aria-live="polite"></p><div data-testid="results"></div></div>
IDENTICAL DOM           : true
```

`<Suspense>` är ingen host-komponent och avger **noll** DOM-noder när den inte suspendar — och
`ForetagSokResults` är mockad synkront (`:30-32`), så boundaryn suspendar aldrig i det här testet.
Flyttar man `<ForetagSokAnnouncer>` **in** i `<Suspense>` — precis den regression docblocken säger
sig stoppa — passerar båda assertionerna och sviten förblir grön. Sessionens mutationsuppsättning
saknar just den mutationen.

Krävs: **strykning** av `it`-blocket och dess kommentar (M3 är stängt av `:216-225` på egen hand). En
ärlig pin skulle kräva att den mockade resultatkomponenten faktiskt suspendar — ny prosa, alltså
följd-PR, inte den här. Strykningen greppar till noll och adderar noll `+`-rader → **mekanisk
stängning**.

**2. "React would batch away the intermediate state" är fel mekanism**
Fil: `web/jobbliggaren-web/src/components/company-criteria/foretag-sok-announcer.tsx:31-32`

Mätt i källan, tre oberoende punkter:
- `foretag-sok-searchbar.tsx:374-378` — `setAnnouncement(announced)` ligger **utanför**
  `startNavTransition`, med kommentaren *"Outside the transition: the live region should update as
  soon as the intent is known, not when the navigation settles."* Meningen committas i sin **egen**
  commit vid t=0.
- `foretag-sok-searchbar.tsx:285` — filen deklarerar att de applicerade props:en ändras *"roughly
  900 ms after the announcement was set"*.
- `foretag-sok-searchbar.tsx:104` — skelettet mäts till **158 ms**.

Två commit:ar separerade av 158–900 ms och en nätverksrondtur kan inte batchas — React batchar per
task/microtask-flush. Delade de en region skulle filtermeningen committas och sedan **skrivas över**,
vilket är en annan och svagare risk än den namngivna. Fyndet är dessutom formklassiskt: Minor 2
stängdes genom att **lägga till en påståendemening** i stället för att stryka — precis vad §9.6 säger
att en stängande fix inte gör — och den tillagda meningen är falsk, vilket lyfter en Minor till Major.

Krävs: **stryk** klausulen. Noll adderade rader → **mekanisk stängning**.

## Sammanfattning

**0 Blocker, 2 nya Major, 0 Minor.** Alla nio ursprungliga fynd CLOSED. Båda nya Major stängs genom
**ren strykning** → mekanisk stängning per §9.6, **ingen ytterligare omkontroll är skyldig mig**.
Kvar innan `agents-done`: `design-reviewer`s öppna Major på `foretag-sok-results.tsx:247`. Verifiera
att HEAD är oförändrad omedelbart före `agents-done`.

**Eskalering att bära vidare:** ingen. Inga CLAUDE.md-konflikter i deltat.
