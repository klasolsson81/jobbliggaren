# dotnet-architect — PR #1466 (#1463)

> Transkriberad ordagrant av ägande session. Charter är read-only. Ingen eskalering.
>
> **Runda 1:** 0 Kritiskt · 0 Viktigt · 2 Nice-to-have.

**Granskat HEAD:** `dc6d967a`, base `7b318483`, worktree `c:/tmp/jbl-cmdtimeout-1463`. Läst blob, inte
arbetsträd. Inga filer redigerade, inga byggen/tester körda.

## Sammanfattning

OK — inga kritiska fynd. 0 Kritiskt, 0 Viktigt, 2 Nice-to-have. Konstruktorkonverteringen är komplett
och biverkningsfri, radien är korrekt vald och §2.1 håller på alla tre axlar. Mätningen mäter det du
påstår, med en precisering nedan.

## Verifierat

**1 — Konstruktorkonverteringen.** Komplett. Noll kvarvarande bara `db.`/`tokenizer.` i filen; 10
`_db.` (9 omdöpta + den nya raden 63) och 1 `_tokenizer.`. Diffens borttagna rader är exakt de 9 + 1,
inga andra kroppsändringar. Primary-constructor-parametrar är redan captured fields, så semantiken är
identisk — konverteringen är rent syntaktisk. `partial` krävs av `[LoggerMessage]` och är korrekt
tillagd. EventId 8436 är unikt i `src/`.

**2 — Konstruktor-mutationen av delat scoped state.** Sund, och din motivering är svagare än den kunde
vara. Tre mätningar bär den: porten är `AddScoped` (`DependencyInjection.cs:1224`), `IAppDbContext` är
`sp => sp.GetRequiredService<AppDbContext>()` (rad 1147) — samma instans — och ingenting i `src/`
använder `AddDbContextPool`/`AddDbContextFactory`, så state kan inte läcka mellan requests via en pool.
`EraseRecruiterAdsCommand` dispatchas bara från `AdminJobAdsEndpoints`. Radien är alltså exakt ett
request, mätt.

Den starkare grunden: porten är en **direkt konstruktorparameter** på
`EraseRecruiterAdsCommandHandler`, så taket appliceras före `Handle` — och därmed före
`UnitOfWorkBehavior.SaveChangesAsync`. Handlern skriver medvetet genom change trackern och inte
`ExecuteDelete` (klassens egen remark, ADR 0022), så raderingen är EF-batchade UPDATE/DELETE som ärver
180. Det är inte en **tolererad** bieffekt — det är **nödvändigt**: ett `try/finally`-återställande per
metod (den enda genuint smalare formen, som kommentaren inte väger) skulle sätta tillbaka 30 s på
skrivhalvan och flytta #1463 från dry run till commit, alltså till den oåterkalleliga halvan.
Konstruktorplaceringen är den smalaste **deklarerade** radien som finns när ADR 0009 vägrar exponera
`Database` på `IAppDbContext`. Provider-options-vägen (`npgsql.CommandTimeout(...)`, som
`MigrationsOptionsFactory` använder) vore app-vid och alltså bredare. Ingen åtgärd.

**3 — InMemory-konsekvensen.** Bekräftad. Exakt en direkt konstruktion i repot:
`RecruiterErasureIngestTests.cs:1434`, mot Npgsql/Testcontainers. Inget unit-test konstruerar porten.
`ValidateOnBuild` instansierar inte scoped services, så DI-grafstester träffar den inte, och
`Jobbliggaren.QA.Corpus` (som också kör InMemory) refererar inte erasure-porten alls.

Vad det binder framåt: `Jobbliggaren.Application.UnitTests` **refererar Infrastructure** (csproj rad
69) och har `TestAppDbContextFactory` på InMemory. En framtida testförfattare som följer repots
default-handlermönster får ett ogenomskinligt `RelationalNotInUse` från en **konstruktor**, utan skylt.
SQLite in-memory är relational och fungerar; InMemory gör det inte. Det är en latent fälla, inte ett
fynd — porten är ett Infrastructure-adapter vars kontrakt redan är Npgsql-specifikt (rå SQL,
`jsonb_path_query`, ARE-regex), så "kan inte köras mot InMemory" är sant om den oavsett taket.

**4 — §2.1, tre axlar.** Ren. Axel 1 (package): inga csproj- eller `Directory.Packages.props`-ändringar
i diffen; Application får inget provider-paket. Axel 2 (port): `IAppDbContext` är orörd — diffen är tre
filer och ingen av dem är den. Axel 3 (member): `SetCommandTimeout` är
`RelationalDatabaseFacadeExtensions`, alltså relational-only, och används på den **konkreta**
`AppDbContext` i Infrastructure — rätt sida av gränsen, och just därför ADR 0009 håller.
`Stopwatch`/`ILogger` är BCL respektive `Microsoft.Extensions.Logging`, båda i bruk i lagret.

**5 — Klämmans kvantitet.** Rätt placerad. `matchingStartedAt` (rad 295) sätts efter all CPU-prep och
läses på rad 323 direkt efter `ToListAsync` — den mäter ett kommando, mot ett per-kommando-tak. Att
utesluta EF-projektionen är korrekt av precis det skäl kommentaren ger.

En precisering som inte ändrar slutsatsen: Npgsqls `CommandTimeout` tillämpas **per I/O-operation**,
inte som en kumulativ väggklocka över hela kommandot. Din `elapsed` är alltså en **övermängd** av det
taket binder (den inkluderar radströmningen). Riktningen är den försiktiga — varningen kan gå tidigt,
aldrig sent — vilket är rätt riktning för en marginalvarning. Projektionen har inget eget marginallarm,
men den är en PK-uppslagning över `= ANY(@p)` (EF 8+ parameteriserar `Contains` som array, ingen
parameterexplosion), så risken sitter inte där.

**Mätningen (interceptorn).** Den mäter det du påstår. `ReaderExecuting(Async)` är den punkt där EF
lämnar över `DbCommand` till providern, efter att `CommandTimeout` applicerats — sista stället
påståendet fortfarande kan vara fel. Kontrollhalvan är icke-vakuös: `ShouldNotBeEmpty` före
`ShouldAllBe` stänger den vakuösa passeringen, egen scope ger färsk `AppDbContext`, och `Clear()`
isolerar. Treatment-filtret på `jsonb_path_query` är likaså vaktat med `ShouldNotBeEmpty`.
Interceptorinstansen är delad över alla contexts via closure, vilket är vad som krävs.

Två saker den **inte** mäter, och båda är oskadliga: (a) att servern faktiskt tillåter 180 s — det är
`statement_timeout = 0`, en separat mätning du redan har; (b) persistens till kommandon efter portens
**egna**. Notera dock att ctor→första kommandot **är** korsat: konstruktorn utfärdar inget kommando, så
matchnings-SQL:en på rad 295 är redan ett senare, separat kommando. Det som återstår omätt är bara
steget därifrån till `SaveChanges`, och det vilar på en DI-registrering jag läst (rad 1147) plus
dokumenterad EF-semantik. Tunt nog att jag inte graderar det Viktigt.

## Fynd

**[Nice-to-have]** `src/Jobbliggaren.Infrastructure/JobAds/RecruiterErasureMatchQuery.cs:59-62`
**Vad:** *"This is the repo's FIRST EF-level SetCommandTimeout on AppDbContext"* och *"the absence of
other EF-level sites is a fact, not a gap"* är falska repo-vitt. Tre pre-existerande anrop finns, alla
på `AppDbContext`: `JobAdBrowseSortQueryPlanTests.cs:120`, `CompanyRegisterPlanFixture.cs:177`,
`CompanyWatchBrowseQueryPlanTests.cs:293` (alla `SetCommandTimeout(300)`). Påståendet är sant om `src/`,
inte om repot.
**Varför:** CLAUDE.md §5 `Comments:` — *"En factually wrong comment — wrong number, wrong gate name,
stale §-reference — is a defect and is fixed."* Det är inte formulering utan ett felaktigt påstående om
repotillstånd, och det leder bort nästa läsare från tre arbetade prejudikat som demonstrerar just den
scope-persistens hela placeringsargumentet vilar på. §12 undantar `Comments:` från STOPP, så det
blockerar ingenting.
**Föreslagen åtgärd:** Ren strykning stänger den mekaniskt (noll `+`-rader): ta bort meningen på rad 59
och slutmeningen på rad 61-62, behåll kontrasten mot `NpgsqlCommand`, som är den bit som bär.

**[Nice-to-have]** `tests/Jobbliggaren.Api.IntegrationTests/JobAds/RecruiterErasureIngestTests.cs`
(testet på rad ~1379)
**Vad:** Förstärkning, inte defekt. Riggen kan korsa scope-persistensen bortom portens eget kommando
till noll extra kostnad.
**Varför:** Skrivhalvan (`UnitOfWorkBehavior.SaveChangesAsync`) är den bit som gör placeringen rätt
snarare än bara oundviklig, och den är den enda länk denna PR lämnar oöverkorsad — i en PR vars hela
existensberättigande var att en omätt providerlänk bet.
**Föreslagen åtgärd:** I treatment-scopen, efter `FindJobAdsAsync`, utfärda **samma** kommando som
kontrollen och hävda 180. Kontroll och prob blir då identisk kommandotext i två scopes som skiljer sig
**enbart** på om porten konstruerades — 30 mot 180. Ren kodändring, ingen ny prosa, stängs genom att
köra om fyndets egen mätning.

## Referenser

- AGENTS.md §2.1 — EF Core-beroenderegeln, tre axlar; ADR 0009 (`Database` medvetet ej på `IAppDbContext`)
- AGENTS.md §5 `Comments:` + CLAUDE.md §12 — felaktig kommentar är defekt, men ej STOPP-klass
- AGENTS.md §2.3 — pipeline-ordning; `UnitOfWorkBehavior` som ensam SaveChanges (ADR 0022)
- CLAUDE.md §9.6 — disposition; båda fynden är Minor och kan namnges som skip i PR-body

---

## Sessionens åtgärd

**N1 — STÄNGD genom ren strykning.** Hela påståendet är borttaget, inte omformulerat; kontrasten mot rå
`NpgsqlCommand` står kvar. `code-reviewer` fällde samma stycke på en **annan** mätning
(`MigrationsOptionsFactory.cs:35` sätter `npgsql.CommandTimeout(600)` på `AppDbContext`-options, alltså
ett EF-nivå-tak även inom `src/`), så strykningen stänger båda. Verifierat: `grep` på
`SetCommandTimeout` i `tests/` ger de tre rader arkitekten namnger.

**N2 — STÄNGD, i en starkare form än den föreslagna.** Treatment-halvan kör nu hela dry-runen via
`EraseAsync(..., dryRun: true)` i stället för `FindJobAdsAsync` ensamt, så varje kommando requesten
utfärdar — alla kaskad-counts och audit-skrivningen — måste bära 180, och `Count.ShouldBeGreaterThan(1)`
hindrar att den degenererar till ett kommando.

⚠ **En mätning som falsifierade min egen text:** att flytta `SetCommandTimeout` ur konstruktorn in i
`FindJobAdsAsync` lämnar testet **grönt** (mätt, inte resonerat) — metoden är handlerns första
port-anrop, så allt efter den ärver taket ändå. Testets XML-doc påstod motsatsen och är rättad; radie-
premissen pinnas i stället av `ErasurePortInjectionRadiusTests`, vars egen mutation (en andra
konstruktor-konsument) verifierats röd.

---

# Omkontroll (scoped, report-only) — delta `dc6d967a..672e3b31`

> Transkriberad ordagrant. HEAD `672e3b31`, arbetsträdet rent. Cap:en förbrukad efter denna.

**Sammanfattning:** Båda mina fynd stängda — N1 genom ren strykning, N2 i en starkare form än jag
föreslog. Ett nytt fynd i deltat: **1 Viktigt**.

**N1 — STÄNGD.** Verifierat i blobben: både *"the repo's FIRST EF-level SetCommandTimeout"* och
*"the absence of other EF-level sites is a fact, not a gap"* är borta. Kvarvarande mening är mätt sann
och gör inget uttömmandepåstående alls. Att `code-reviewer`s oberoende mätning
(`MigrationsOptionsFactory`) föll på samma stycke bekräftar att radering var rätt disposition framför
avgränsning till `src/`: två oberoende motexempel längs två olika axlar är ett påstående som inte
skulle ha skrivits. Deltat narrowade dessutom `statement_timeout`-påståendet — en andra okvalificerad
claim borta, som jag inte bad om.

**N2 — STÄNGD, bredare än jag föreslog, och bredare är rätt.** `EraseAsync(..., dryRun: true)` +
`ShouldAllBe(180)` **utan filter** är strikt starkare än mitt `SELECT 1`-prob: mitt hade visat att
*ett* senare kommando ärver taket, detta visar att **inget** kommando i requesten undslipper det —
inklusive audit-skrivningen. Att `ShouldAllBe` gäller varje observerat kommando gör faktumet
självsanerande: en framtida seed- eller setup-väg i en scope utan port skulle fälla det rött i stället
för att glida förbi ett filter.

## Svar på sessionens två frågor

**1 — Hem: rätt. Predikat: se fyndet nedan.** Arkitektursviten är rätt hem för ett strukturellt
invariant över ett assembly, och att pinna premissen i stället för prosan är rätt drag. Din
mutationsmätning är dessutom den rätta lärdomen: att ceiling-faktumet förblir grönt när anropet flyttas
in i `FindJobAdsAsync` är inte en svaghet i faktumet utan ett bevis för att *placeringen* inte kan
pinnas av ett kommandotak. Den måste pinnas av radien. Rätt slutsats, och rätt att radera
XML-doc-påståendet i stället för att omformulera det.

**2 — Nej, per-I/O-semantiken hör inte hemma i koden.** (a) Det är ett påstående om Npgsqls interna
tillämpning som jag angav ur kunskap, inte ur en mätning i det här repot — att skriva in det vore att
lägga en **omätt** claim bredvid fyra mätta, precis den asymmetri den här ronden ägnades åt att ta bort.
(b) Det ändrar ingen beslutsregel: riktningen är försiktig, så en läsare som antar kumulativ väggklocka
drar samma slutsats. (c) §5 `Comments:` — kommentera där koden inte kan visa saken själv. Om det någonsin
ska ha ett hem är det en **mätning**, inte en kommentar. **Ditt beslut var rätt.**

## Fynd

**[Viktigt]** `tests/Jobbliggaren.Architecture.Tests/ErasurePortInjectionRadiusTests.cs:28`

**Vad:** Vakten skannar **ett av fem ägda assemblies**. Mätt: `IRecruiterErasureMatchQuery` är
`public` (`IRecruiterErasureMatchQuery.cs:20`), registreringen ligger i **`AddPersistence`**
(`DependencyInjection.cs:1224`), och `src/Jobbliggaren.Worker/Program.cs:55` anropar `AddPersistence`.
Porten är alltså resolverbar i Worker. En Hangfire-job-konstruktor som tar porten ger hela den
jobbscopens `AppDbContext` ett flerminuterstak — exakt det scenario remarken beskriver — och skanningen
ser den inte. Samma sak för en Infrastructure-konsument.

**Varför:** Vakten finns för att premissen var oskyddad; en vakt som *läser* som enforcement men täcker
en femtedel av ytan är den farligare formen — den passerar grönt för alltid och tar bort skälet att läsa
om placeringen. Charterns egen grund (AGENTS.md §2.1: Api/Worker är composition roots) säger att det är
just värdassemblierna som avgör radien. Precedens för exakt den här formen finns i samma svit:
`OrgNrRecordLoggingGuardTests.OwnedAssemblies` (rad 77-87) räknar alla fem by name och motiverar
uttryckligen varför Worker står kvar i listan trots att den saknar ankare i dag.

**Föreslagen åtgärd:** Ren kodändring, noll nya beroenden — projektet refererar redan alla fem. Byt
skanningskällan mot samma mönster som syskonet och kör `SelectMany(a => a.GetTypes())`. Stängs genom att
köra om fyndets egen mätning: din befintliga mutation ska förbli **röd**, plus en ny mutation som
placerar konsumenten i **Worker** — den är grön i dag och måste bli röd. Ingen ny prosa krävs.

⚠ En residual som förblir öppen även efter breddningen, och som jag **inte** föreslår att du skriver in:
repots Api-lager hämtar tjänster som **minimal-API-delegatparametrar**, inte konstruktorparametrar. En
endpoint-konsument är alltså osynlig för varje konstruktorskan oavsett assembly-mängd. Breddningen
stänger Worker- och Infrastructure-vägen helt; Api-delegatvägen gör den inte. Deklarera den inte i
kommentar — medlemsaxeln är redan avgränsad i doc:en.

## Sessionens åtgärd

**Viktigt — STÄNGD genom ren kodändring, och mätningen kördes.** `OwnedAssemblies` täcker nu
Application, Infrastructure, Api och Worker, och `GetConstructors` fick
`BindingFlags.Instance | Public | NonPublic`. Mätt mot den fixade filen:

- konsument i **Infrastructure** → **RÖD** (grön före breddningen)
- konsument i **Api** → **RÖD** (grön före breddningen)
- ursprungsmutationen (konsument i Application) → förblir **RÖD**

⚠ **Din residual mättes i stället för att antas:** en `IRecruiterErasureMatchQuery`-parameter på
`MapPost`-lambdan i `AdminJobAdsEndpoints` är **GRÖN även efter breddningen**. Därför är metoden
omdöpt till `ErasurePortIsConstructorInjectedOnlyByTheErasureHandler` och remarkens övertäckande
*"injected by the erasure handler alone"* är **struken** — övertäckningen togs bort ur påståendet i
stället för att förklaras i ett tillagt stycke. `test-writer` föreskrev samma sak oberoende.
