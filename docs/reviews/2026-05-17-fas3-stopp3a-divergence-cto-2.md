# CTO-beslut (rev2) — FAS 3 STOPP 3a provider-divergens, post-shadow-FK-falsifiering

**Datum:** 2026-05-17
**Roll:** senior-cto-advisor (decision-maker, feedback_cto_decides_multi_approach)
**Scope:** read-only beslut + implementations-spec. CC implementerar.
**Föregående:** `2026-05-17-fas3-stopp3a-divergence-cto.md` (3:e-vägs-beslut,
shadow-FK) — §6 re-invoke-klausul **utlöst** (premiss falsifierad).
**Klas-STOPP:** **JA** — se §6. Avvik från ursprunglig ram (cto-1 sade NEJ);
motiverat: den genuina provider-divergensen architecten fördefinierade som
Klas-STOPP är nu reell och bekräftad efter tre falsifierade in-block-fixar.

---

## 1. Beslut

**(B) — Flytta de fyra read-handler-testerna till Npgsql-integrationslagret
via befintlig `Testcontainers.PostgreSql`. (C) avvisad.**

Konkret: `ReadHandlerManualPostingFallbackTests` + de tre
`Get{Applications,Pipeline,ApplicationById}QueryHandlerTests`-filernas
JobAd-join-täckande fakta flyttas från `JobbPilot.Application.UnitTests`
(InMemory) till integrationsprojekt med riktig Npgsql-provider via den
**redan existerande, redan gröna** Testcontainers-infrastrukturen.
Shadow-FK i `ApplicationConfiguration.cs` **rivs** (inert under InMemory,
men korrekt och ofarlig under Npgsql — dock: död kod utan testflytt, så
bort den; join-nyckeln återgår till `a => a.JobAdId`).

---

## 2. Varför ingen fjärde in-block-väg (beviströskeln)

Re-invoke-prompten satte en hög beviströskel för ännu en in-block-väg. Den
tröskeln möts inte — och ska inte mötas. Tre på varandra följande fixar:

1. architect-design relationell join (`a => a.JobAdId`)
2. (D) projektions-fix (join-härledd `JobAdGuid`)
3. CTO-1 shadow-FK (`EF.Property<Guid?>(a, "JobAdRef")` + `HasColumnName`)

…failade **alla av samma grund**: EF InMemory är ingen relationell store.
CC:s rotorsak är kod-verifierad och korrekt — `HasColumnName` är no-op under
InMemory, så `JobAdRef` blir en oberoende, aldrig-skriven slot (default →
`Nullable<Guid>` utan värde i join-nyckeln, eller inert join). Detta är inte
en ny insikt; det är **samma insikt som cto-1 §2** ("EF InMemory invokerar
converter-lambdan klient-sida"), nu generaliserad: *varje* fix som lutar sig
mot relationell semantik (SQL-translation av converter, kolumn-delning,
join-translation) bryts under InMemory by construction.

En fjärde in-block-väg vore att hävda att nästa relationella trick lyckas
där tre falsifierats av en gemensam, nu fullt förstådd, grundsats. Det vore
inte teknisk bedömning — det vore hopp. Ford/Parsons/Kua (2017, kap. 2):
en fitness-funktion som upprepat faller på samma axel signalerar att
*systemet under test* är fel-isolerat, inte att testet behöver finslipas.
**Den genuina provider-divergensen architecten fördefinierade som
Klas-STOPP-villkor är reell.** Vi är vid den korsningen. Iteration är
icke-konvergerande — bekräftat empiriskt (18 → 21 RÖD).

---

## 3. Motivering mot principer — varför (B), inte (C)

### 3.1 Det som testas ÄR inte en ren unit (Fowler/Cohn — testpyramidens definition)

`ReadHandlerManualPostingFallbackTests` verifierar en **cross-aggregat
relationell LEFT JOIN med value-converter på en nullable-struct-FK-nyckel**
(`ApplicationConfiguration.cs:27–30`, ADR 0048 in-handler-join). Det som
står på spel är *EF Core query-translation*, inte isolerbar C#-handlerlogik.
Handlerns "logik" ÄR LINQ-uttrycksträdet som översätts av provider:n. Det
finns ingen meningsfull provider-oberoende kärna att isolera — bevisat av
att tre försök att tvinga in provider-oberoende failat.

Fowler 2018 (kap. 2, testpyramiden via Cohn 2009): unit-nivån är för logik
som *kan* isoleras från infrastruktur. En query vars hela beteende är
relationell översättning hör **per definition** på integrationsnivån. Att
flytta den dit är inte att betala test-pyramid-kostnad i fel riktning — det
är att **korrigera en feldeklarerad testnivå**. Testet var aldrig en äkta
unit; InMemory gav en falsk känsla av det tills converter+join-kombinationen
exponerade gapet (Meszaros 2007 — "Obscure Test"/fel test-double-nivå).

cto-1 §4 avvisade (B) med "handler-logik som *kan* enhetstestas flyttas till
långsammare lager". Den premissen föll med shadow-FK-falsifieringen:
handler-logiken kan **bevisligen inte** enhetstestas under den enda
unit-provider projektet har. cto-1:s (B)-avvisning byggde på samma
nu-falsifierade antagande som hela cto-1-beslutet. Jag återvänder till
beslutet (cto-1 §6) och vänder utfallet.

### 3.2 (B) maskerar inte längre problemet — den deklarerar det ärligt

cto-1 §4 (B)-avvisning (c): "maskerar problemet, join-nyckeln vore en
converter-fälla för nästa handler". Det argumentet var giltigt **när en
in-block-fix fortfarande troddes finnas**. Nu: det finns ingen in-block-fix.
Att behålla en grön InMemory-svit som *inte kan* testa produktionens
relationella join vore den verkliga maskeringen — en falsk grön signal
(Beck 2002, *TDD* — grön ska betyda "beteendet verifierat", inte "provider
kan inte se felet"). (B) gör motsatsen: testet körs mot exakt den provider
produktionen använder (Npgsql), via exakt den join produktionen kör
(ADR 0048 EN LEFT JOIN, bevarad). Det är ärlighet, inte maskering.

### 3.3 (C) — SQLite-in-memory — avvisad på fyra grunder

1. **Ny top-level test-dependency utan precedens.** `Directory.Packages.props`
   har **ingen** `Microsoft.EntityFrameworkCore.Sqlite`. (C) kräver att lägga
   till den — CLAUDE.md §9.2 ("lägga till nya top-level dependencies utan
   motivering" är förbjudet), BUILD.md §3.1 styr dependency-setet. Detta är
   ett Klas-beslut i sig, inte en CTO-default.
2. **Byter en provider-fidelitetslucka mot en annan.** SQLite-in-memory är
   *relationell* men inte Npgsql-trogen: ingen `xmin`-systemkolumn
   (`ApplicationConfiguration.cs:86`, optimistic concurrency token),
   avvikande `DateTimeOffset`-semantik (SQLite saknar native typ — lagras
   som TEXT/REAL), annan collation/case-semantik, ingen
   `UseSnakeCaseNamingConvention`-DDL-paritet. Vi skulle byta "InMemory ljuger
   om relationell semantik" mot "SQLite ljuger om Npgsql-semantik" — och nu
   över **42 testfiler** (greppat: 46 träffar − 2 docs − factory − sessionlog),
   varav ~38 helt orelaterade till STOPP 3a.
3. **Strategisk regressionsrisk utan koppling till scopet.** Att byta
   provider-semantik under 42 testfiler för att lösa en 4-fil-defekt är
   spekulativ tvärgående infrastruktur (Martin 2017, kap. 22 — YAGNI;
   Fowler 2018, kap. 3 — Speculative Generality). Cto-1 §4 avvisade (C) på
   denna grund; **den grunden står oförändrad** — shadow-FK-falsifieringen
   påverkar inte (C):s blast-radius-argument.
4. **`Testcontainers.PostgreSql` 4.11.0 finns redan** och driver redan
   `JobbPilot.Api.IntegrationTests` + `JobbPilot.Worker.IntegrationTests`
   gröna. (B) återanvänder etablerad, verifierad infrastruktur (CCP/REP,
   Martin 2017 kap. 13 — komponenter som hör ihop finns redan ihop). (C)
   inför en parallell, mindre trogen provider bredvid den. (B) är minsta
   *strategiska* ingrepp; (C) ser lokalt mindre ut men är globalt större.

---

## 4. Implementations-spec (kod-exakt)

### 4.1 Riv shadow-FK (inert kod)

`ApplicationConfiguration.cs` — ta bort rad 32–43 (shadow-FK-blocket +
kommentaren). `JobAdId`-converter rad 27–30 **behålls oförändrad**.

### 4.2 Återställ join-nyckel i de tre handlers

`GetApplicationsQueryHandler.cs:49–53`,
`GetPipelineQueryHandler.cs` (motsv. GroupJoin),
`GetApplicationByIdQueryHandler.cs:39–43`:

Byt tillbaka join-nyckeln till den relationella formen:

```csharp
.GroupJoin(
    db.JobAds,
    a => a.JobAdId,
    j => (JobAdId?)j.Id,
    (a, ja) => new { a, ja })
```

Detta är architectens ursprungliga relationella join — den var **korrekt
under Npgsql** (rapport: "relationell join verifierad grön"). (D):s
projektions-fix (`JobAdGuid = j != null ? (Guid?)j.Id.Value : null`)
**behålls** — den är korrekt under båda providers och oberoende av
join-nyckel-frågan. ADR 0048 EN LEFT JOIN + 3-grens-fallback + query-filter-
disciplin (ingen `IgnoreQueryFilters`, inget manuellt `DeletedAt`) **bevaras
oförändrat**.

> Kommentar uppdateras: ta bort "Väg (D)/shadow"-noterna, behåll ADR 0048-
> referensen + att joinen är relationell-provider-verifierad (testas på
> integrationsnivå, ej InMemory — pekare till denna ADR/review).

### 4.3 Flytta testerna

`db-migration-writer` ska INTE invokeras (ingen schemaändring). Invokera
**`test-writer`** för testflytten (CLAUDE.md §9.2 — nya/flyttade tester).
Mål-projekt: **`JobbPilot.Api.IntegrationTests`** (har redan
`Testcontainers.PostgreSql` + Npgsql-fixture; Worker-projektet är fel
bounded-kontext för Application-query-handlers).

Flytta:

- Hela `ReadHandlerManualPostingFallbackTests.cs` (12 fakta).
- Ur `Get{Applications,Pipeline,ApplicationById}QueryHandlerTests.cs`:
  **endast** de fakta som materialiserar JobAd-joinen (de som failar under
  InMemory). Fakta som inte rör joinen (rena scoping/paginerings-fakta som
  redan är gröna under InMemory) **stannar** — flytta inte mer än divergensen
  kräver (minsta ingrepp; testpyramiden ska inte tömmas i onödan).
- Anpassa till integrationsfixturens seed-/DbContext-livscykel (Testcontainers
  Npgsql, inte `TestAppDbContextFactory`). `TestAppDbContextFactory`
  **orörd** — noll factory-ändring, (C):s 42-fil-risk undviks helt.

### 4.4 Verifiering CC ska producera i STOPP-rapport

1. `dotnet build` 0 fel.
2. `dotnet test tests/JobbPilot.Application.UnitTests` — **0 RÖD** (de
   flyttade fakta borta härifrån; kvarvarande InMemory-fakta gröna).
3. `dotnet test tests/JobbPilot.Api.IntegrationTests` — de flyttade fakta
   GRÖNA mot riktig Npgsql.
4. `dotnet test` full svit grön.
5. `dotnet ef migrations has-pending-model-changes` → **ingen ny migration**
   (shadow-FK rivet → model-diff = pre-cto-1-state; bevisar schema orört,
   ADR 0048 + db-migration-writer-migration oförändrad).
6. `git diff --stat` — ändrade prod-filer ⊆ {ApplicationConfiguration.cs,
   3 read-handlers}; testfiler flyttade (Application.UnitTests − , Api.
   IntegrationTests +). Noll `TestAppDbContextFactory`-ändring. Noll ny
   package i `Directory.Packages.props`.

### 4.5 Atomicitet (feedback_di_with_handlers_same_commit)

Allt i **en commit** (J3 atomisk, inget committat ännu enligt prompt):
config-rivning + handler-join-återställning + testflytt. Splittrad commit
ger broken state (handlers utan shadow-FK men tester kvar i InMemory =
21 RÖD i mellantillstånd; CI fångar, pre-push inte — feedback-noten är
exakt detta scenario). Pathspec-scoped commit (memory:
feedback_pathspec_commit_parallel_cc) om parallell-CC.

---

## 5. Avvisade alternativ

**(C) SQLite-in-memory factory-byte.** Avvisad — §3.3 (ny top-level
dependency utan precedens/BUILD.md §3.1; SQLite≠Npgsql-trogen; 42-fil
strategisk regressionsrisk; Testcontainers finns redan). Cto-1:s
(C)-avvisningsgrund står oförändrad efter falsifieringen.

**4:e in-block-väg (valfri relationell mappnings-trick).** Avvisad — §2.
Beviströskeln efter tre falsifieringar på samma grundsats möts inte;
icke-konvergerande iteration (empiriskt 18→21 RÖD).

**Behåll shadow-FK, flytta inte tester.** Avvisad — shadow-FK är inert
under InMemory (löser inget) och blir död kod om testerna inte flyttas.
Antingen river vi den (B) eller så finns inget problem den löser.

**Behåll shadow-FK + flytta tester.** Avvisad — shadow-FK tillför då noll
värde (Npgsql-joinen fungerade redan utan den, rapport-verifierat) men bär
permanent EF-mappnings-komplexitet (två CLR-properties/kolumn,
`PropertySaveBehavior.Ignore`) utan motsvarande nytta. YAGNI (Martin 2017
kap. 22): riv det som inte längre tjänar något.

---

## 6. Klas-STOPP — explicit bedömning (AVVIK från cto-1)

**JA, Klas-slut-GO krävs innan implementation.** Detta avviker medvetet
från cto-1 §6 (som sade NEJ). Motivering:

- **Architectens ursprungliga ram bekräftas, inte kringgås.** Architecten
  fördefinierade: "join-härledd form ej grön i båda providers → äkta
  provider-divergens → (B)/(C) strategiskt → Klas-STOPP". cto-1 hävdade att
  3:e vägen *undanröjde* divergens-premissen. Den hävdningen är nu
  **empiriskt falsifierad**. Därmed står architectens ram: detta ÄR den
  genuina provider-divergensen, och (B) ÄR det strategiska valet
  architecten ringade in som Klas-beslut. Att då köra (B) på CTO-default
  vore att överrida architectens fördefinierade Klas-STOPP på en grund som
  just kollapsat. Intellektuellt ohederligt.
- **Strategisk blast-radius finns nu.** (B) flyttar Fas-1-testfiler mellan
  testprojekt (J3-blast-radius architecten själv avrådde från utan
  Klas-GO). Inte en lokal mappnings-fix längre — en testarkitektur-
  förändring (testpyramid-nivå-omklassning).
- **CLAUDE.md §9.6 p.5 / §9.2:** CTO går direkt till implementation endast
  när beslutet *inte* är en genuin strategisk korsning. Tre falsifierade
  in-block-fixar + testnivå-omklassning + architect-fördefinierad STOPP =
  genuin strategisk korsning. §9.6: "Klas-STOPP triggas vid större
  strategiska frågor".
- **CTO-1 lärdom dokumenteras:** anta inte att en "smartare mappning"
  upphäver en provider-semantik-gräns innan empiri bekräftar det. Cto-1:s
  NEJ var ett rimligt-men-fel default; rev2 korrigerar mot empiri. Framtida
  CTO-instans: provider-divergens som architect fördefinierat som STOPP ska
  inte CTO-default:as bort på en oprövad teknisk premiss.

CTO levererar implementations-spec (§4) så CC kan exekvera **omedelbart vid
Klas-GO**. CC STOPP:ar och bifogar denna rapport till Klas för parallell-
granskning (ADR 0019 mekanism 3–4). Klas har override; om Klas föredrar
(C) trots §3.3 — dokumentera som Klas-override (roadmap-/risk-kunskap CTO
saknar) och kör (C):s factory-väg, men då med explicit ny-dependency-GO.

**Re-invokera CTO om:** §4.4 p.5 visar ny migration genereras efter
shadow-FK-rivning (skulle betyda model-state ej återställt korrekt), eller
om de flyttade fakta failar mot Npgsql på annan grund än converter/join
(skulle peka på defekt i ADR 0048-joinen själv, ej provider-divergens —
annan rotorsak, annan beslutsgrund).

---

## 7. Trade-offs accepterade

- **Testpyramiden får färre unit-fakta, fler integrations-fakta.** Accepterat
  och korrekt: dessa fakta var aldrig äkta units (§3.1). Pyramiden ska
  spegla verklig isolerbarhet, inte ett önsketillstånd. Långsammare svit
  för dessa ~16 fakta — acceptabelt mot ärlig signal (Beck 2002).
- **`JobbPilot.Api.IntegrationTests` växer.** Acceptabelt — den har redan
  Npgsql-fixturen; CCP (Martin 2017 kap. 13) säger att det som ändras
  tillsammans (Application-query-handlers + deras Npgsql-verifiering) hör
  ihop.
- **Engångskostnad: testflytt + fixture-anpassning.** Större engångsarbete
  än en kodrad, men det är priset för att tre in-block-genvägar uttömts.
  CLAUDE.md §9.6: in-scope-fix, ingen TD (varken annan-fas eller saknad-
  dependency — Testcontainers finns; det hör till nuvarande Fas 3 STOPP 3a).
- **shadow-FK-arbetet kasseras.** Sunk cost; att behålla inert kod för att
  "inte slösa arbetet" vore concorde-fallacy (Martin 2017 kap. 22).

---

## 8. Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 13 (CCP/REP —
  testkomponent där den hör), kap. 22 (YAGNI/sunk cost — riv inert kod,
  inga spekulativa infra-skiften).
- Martin Fowler, *Refactoring* 2nd ed. (2018) — kap. 2 (testpyramiden),
  kap. 3 (Speculative Generality — (C)-avvisning).
- Mike Cohn, *Succeeding with Agile* (2009) — testpyramid-nivådefinition.
- Kent Beck, *Test-Driven Development by Example* (2002) — grön = verifierat
  beteende, ej "provider kan inte se felet".
- Gerard Meszaros, *xUnit Test Patterns* (2007) — fel test-double-nivå ger
  falsk isolering (InMemory som pseudo-relationell).
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly, 2017)
  kap. 2 — upprepad fitness-funktion-failure på samma axel = fel-isolerat
  system, ej finslipning.
- Microsoft Learn — EF Core *In-Memory Database Provider* ("not designed
  for relational behavior; HasColumnName is a no-op"; rekommenderar
  Sqlite/relationell provider eller riktig databas för query-trogenhet).
- ADR 0048 (EN LEFT JOIN, query-filter-disciplin) — **bevarad, ej
  amenderad**; joinen flyttas i testnivå, inte i mönster.
- ADR 0019 (direct-push, granskningsmekanism 3–4 — STOPP-rapport till Klas).
- CLAUDE.md §3.1/§9.2 (ny top-level dependency = Klas-beslut), §7
  (testdisciplin), §9.6 (in-block, ingen TD), §9.2 (CTO decision-maker;
  test-writer för testflytt).
- Föregående: `2026-05-17-fas3-stopp3a-divergence-cto.md` §6 re-invoke-
  klausul (utlöst); architect-design + -inmemory-fix-architect (väg D).
- Falsifierings-evidens: CC-rapport 2026-05-17 (18→21 RÖD efter shadow-FK;
  kod-verifierat `ApplicationConfiguration.cs:40–43`,
  `GetApplicationsQueryHandler.cs:51–52`).
```

CC-rapport-fakta verifierade mot on-disk-state (CTO Read av handlers/config/
factory/ADR 0048/Directory.Packages.props 2026-05-17).
