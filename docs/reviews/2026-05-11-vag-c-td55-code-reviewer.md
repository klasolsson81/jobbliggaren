# Code-review: TD-55 Block A — PagedResult retro-fit

**Status:** APPROVE
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §2.1, §2.3, §3.4, §3.6, §4.1, §5.2, §6.2, §7
**Scope:** Backend (`c2f539e`) + Frontend (`0b0886d`), branch `main` lokalt
**Reviewer:** code-reviewer (Claude Opus 4.7)

---

## Sammanfattning

**Båda commits godkända för direct-push.** 0 blockers, 0 majors, 2 minors, 3 nits.
Två-commit-split per CTO-beslut (Alt 3) håller och respekterar Conventional
Commits scope-disciplin (§6.2) samt REP/CCP — backend och frontend kan releaseas
oberoende om så krävs.

Bygg- och testresultat verifierat via commit-message-rapportering:
- Application 196/196, Architecture 27/27, Api Integration 178/178, Vitest 150/150.

---

## Granskningsmatris

### §2.1 Clean Architecture — PASS
- `PagedResult<T>` ligger i `JobbPilot.Application/Common/` — korrekt lager.
- Inga EF Core- eller Infrastructure-imports i Application-tillägg.
- `PagedResultContractTests` ligger i Architecture-projekt — reflection mot
  `JobbPilot.Application.AssemblyMarker.Assembly`, ingen Infrastructure-koppling.

### §2.3 CQRS — PASS
- Queries returnerar nu `PagedResult<DTO>` — ingen domänobjekt-läcka.
- `IQueryHandler<TQuery, PagedResult<DTO>>`-signaturer korrekt uppdaterade
  i båda handlers.
- ValidationBehavior är registrerad FÖRE handler-execution i
  `MediatorPipelineBehaviors.InOrder` — `PageNumber ≥ 1` och `PageSize ∈ [1,100]`
  garanteras av FluentValidation-reglerna innan `Empty(query)` kan trigga
  PagedResult-konstruktorns `ArgumentOutOfRangeException`. Inget edge case
  oskyddat.

### §3.4 Error handling — PASS
- `PagedResult`-konstruktor argument-validering via
  `ArgumentNullException.ThrowIfNull` + `ArgumentOutOfRangeException.ThrowIfNegative/LessThan`
  är idiomatic .NET 8/9 — bra defensiv kod.
- `Empty(query)`-helpern bevarar query:ns `PageNumber/PageSize` istället för
  att hårdkoda 1/20 → konsistent kontrakt mot caller, inget skew mellan
  edge-case-svar och happy-path-svar.

### §3.6 LINQ — PASS
- Separat `CountAsync` följt av `Skip/Take/ToListAsync` — exakt det mönster
  §3.6 beskriver. Två SQL-roundtrips men `CountAsync` genererar
  `SELECT COUNT(*)` utan materialisering, vilket är effektivare än
  `.ToList().Count`.
- `.AsNoTracking()` bibehållet på read-queries.
- `ListJobAdsQuery` får `private const int MaxItems = 500` med `.Take(MaxItems)`
  efter `OrderByDescending(PublishedAt)`. Hard cap defense-in-depth motiverad
  i kod-kommentar med referens till Fas 2 + TD.

### §4.1 + §5.2 Frontend — PASS
- `isPagedApplications` / `isPagedResumes` är `value is T` user-defined
  type guards. Validerar:
  - `value !== null && typeof value === "object"`
  - `Array.isArray(v.items)` — fångar `items=null` (Array.isArray(null) === false)
  - `typeof v.totalCount/page/pageSize === "number"` — fångar saknade fält och null
- `payload: unknown = await res.json()` — explicit `unknown`-cast, ingen `any`.
- Inkonsistens i fallback-shape mellan handlers (minor 1 nedan).

### §6.2 Conventional Commits — PASS
- `feat(api): TD-55 — …` och `feat(web): TD-55 — …` — scope-konsekvent,
  imperativ form, TD-referens.
- Två commits = REP/CCP korrekt. Skulle gått som ett gemensamt
  scope `feat(api,web): …` men split är mer disciplinerad och möjliggör
  independent revert om ena sidan bryter.

### §7 Testing — PASS
- Nya tester:
  - `Handle_TotalCount_IsIndependentOfPageSize` (Applications) — regression
    mot exakt den bug-klassen TD-55 stänger.
  - `GetResumesQueryHandlerTests` pagination-test har samma assertion på
    alla tre sidor (`TotalCount.ShouldBe(5)` för page1/2/3) — bra dekkning.
  - `Handle_WithMoreThanMaxItems_CapsResultAt500` (ListJobAds) — regression
    för hard cap.
  - `PagedResultContractTests` — 3 tester, en generisk reflection-pass och
    två explicita per-query-assertions.
- Edge cases för `Empty(query)` bevisar nu att `PageNumber/PageSize`
  bevaras genom no-user och no-jobseeker-grenarna.

---

## Specifika frågor från Klas

### 1. Är `Empty(query)`-helpern konsistent med PagedResult-kontraktet?

**Ja.** TotalCount=0, Items=Array.Empty (icke-null), Page/PageSize från query.
Konstruktor-validering passerar eftersom validator har garanterat
Page/PageSize ≥ 1 innan handler kallas. Tester verifierar:
`result.Page.ShouldBe(1)` + `result.PageSize.ShouldBe(20)` i no-user-grenen.

### 2. Är `.Take(500)` MaxItems-konstanten väl placerad och kommenterad?

**Ja.** `private const int MaxItems = 500;` är handler-lokal — `ListJobAds`
är den enda call-siten, så konstanten hör inte hemma i `Common/`. Kommentar
(3 rader) refererar Fas 2 JobTech-integration som motivering för defer.
Liten nit: kommentaren säger "Se TD-NY" — TD-numret är inte specifikt
("NY" är platshållare). Om Klas hunnit numrera TD:n bör commit följas av
en doc-touch som ersätter "TD-NY" med t.ex. "TD-56" eller motsv. **Nit 1.**

### 3. Är PagedResultContractTests reflection-baserad robust mot framtida queries med PageNumber/PageSize-properties som inte är paginerings-relaterade?

**Mestadels ja.** Logiken kräver BÅDE `PageNumber` (int) OCH `PageSize` (int)
som public instance properties. Det är osannolikt att ett record som inte
är en paginerad query skulle ha exakt dessa två namn med exakt int-typ —
det är en stark heuristik, inte semantisk garanti.

**Svaghet upptäckt:** `GetAuditLogEntriesQuery` använder `Page` (inte
`PageNumber`) + `PageSize` och returnerar redan `PagedResult<T>`. Testet
upptäcker den INTE som paginerad query (saknar `PageNumber`-namn). Det
betyder att om någon framtida `GetAuditLogEntriesQuery`-refactor återinför
bare-array-return kommer testet INTE fånga det. **Minor 2.**

Föreslagen fix (utanför scope, inte blocker): namn-heuristiken kan utökas
till `"PageNumber" || "Page"`, eller skifta till markerings-interface
(`IPagedQuery`) för explicit opt-in. Det är arkitektur-fråga för Fas 2
när paginerings-shape standardiseras mot JobTech-API.

### 4. Är frontend runtime-guard tillräckligt strikt (kan en motpart skicka items=null och passera)?

**Tillräckligt strikt för TD-55-scope.** Genomgång:
- `items=null` → `Array.isArray(null)` är `false` → guard return false. **OK.**
- `items=[]` → `Array.isArray([])` är `true` → passes. **OK** (tom lista är
  giltigt resultat).
- `items=[bogus_object]` → guard validerar inte item-shape, bara att array
  finns. Caller-kod gör `result?.items ?? []` → `.sort(...)` som läser
  `updatedAt` — om backend skickar fel form på items går komponenten sönder
  först i render-tid, inte i fetch-tid.
- `totalCount=null` / `totalCount="5"` (string) → `typeof !== "number"` →
  guard return false. **OK.**
- Saknad property (`undefined`) → `typeof undefined !== "number"` → false. **OK.**

Item-shape-validering är out-of-scope för TD-55. Den frågan löses senare
med Zod på edge per BUILD.md-konventionen. **Nit 2** — kommentar i guard
kunde nämna att shape-validering är top-level, inte djup, för framtida
läsare.

---

## Fynd

### Blockers
*(inga)*

### Majors
*(inga)*

### Minors

**Minor 1: "TD-NY"-platshållare i `ListJobAdsQueryHandler.cs`**
Fil: `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQueryHandler.cs:13`
Nuvarande: `// query-params och URL-kontrakt designas mot JobTech-API:t. Se TD-NY.`
Föreslås: Ersätt med faktiskt TD-nummer (eller ta bort TD-referensen helt
om numret inte är allokerat ännu).
Motivering: CLAUDE.md §5.1 — `TODO` utan ticket-referens listas som minor.
"TD-NY" är funktionellt motsvarande otalokerat TODO.
Delegera till: docs-keeper eller Klas (trivial sed).

**Minor 2: `PagedResultContractTests` missar `Page`-namngivning**
Fil: `tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs:69`
Problem: `HasPagedSemantics` kräver property-namn exakt `"PageNumber"` +
`"PageSize"`. `GetAuditLogEntriesQuery` använder `"Page"` istället och
detekteras INTE som paginerad query. Testet ger falsk trygghet för den
queryn — om någon refactorar den till bare-array kommer testet inte
flagga.
Föreslås: Utöka heuristiken: `p.Name is "PageNumber" or "Page"`, eller
introducera `IPagedQuery`-marker-interface för explicit opt-in.
Motivering: Architecture-test ska vara robust regression-skydd för
hela klassen "paginerade queries", inte bara för specifik namngivning.
Delegera till: Fas 2 (när paginerings-shape standardiseras mot JobTech-API),
eller in-block om Klas vill stänga det nu — det är ≤ 30 min CC-tid (4h-regel,
§9.6 default fix in-block, men Klas har sista ordet om scope-tillåtelse efter
commit).

### Nits

**Nit 1: Inkonsekvent fallback-typ mellan getApplications och getPipeline**
Fil: `web/jobbpilot-web/src/lib/api/applications.ts`
Observation: `getPipeline()` returnerar `PipelineGroupDto[]` och faller
tillbaka till `[]` vid !sessionId / !res.ok. `getApplications()` returnerar
`GetApplicationsResult | null` och faller tillbaka till `null`. Diskrepansen
är legitim — pipeline är en composite view utan paging, applications-listan
är paginerad och `null` distinguishes "no result available" från "empty page".
Ingen fix krävs, men noteras för framtida konsistensbeslut.

**Nit 2: Type-guard-kommentar nämner inte shape-djup**
Fil: `web/jobbpilot-web/src/lib/api/resumes.ts:42-43`,
`web/jobbpilot-web/src/lib/api/applications.ts:60-61`
Kommentaren säger "Lättviktig runtime-validering" men förklarar inte att
item-shape inte valideras djupare. För framtida läsare kunde det stå:
`// Validerar paged-envelope. Item-shape valideras inte djupare — Zod på
edge är out-of-scope för TD-55.`

**Nit 3: `Array.Empty<T>()` vs collection expression `[]`**
Fil: båda `Empty`-helpers
Observation: `new(Array.Empty<ApplicationDto>(), 0, query.PageNumber, query.PageSize)`.
Kunde vara `new([], 0, ...)` med C# 14 collection expressions för stilkonsistens
med övrig kodbas. Funktionellt identiskt. Nit eftersom collection expression
i konstruktor-position med generisk typ-inferens via signatur kanske inte
ger samma `Array.Empty<T>()`-allokerings-elision — `Array.Empty<T>()` är
explicit garanterad cache-singleton.

---

## Bra gjort

- **TD-55 stänger en faktisk runtime-skew** — inte en kosmetisk refactor.
  `GetApplicationsResult`-typen fanns redan på frontend men användes mot
  en backend som skickade bare array. Den buggen skulle ha exploderat i
  produktion. Bra fångat och bra fixat.
- **`Empty(query)`-helpern** bevarar query-shape genom edge cases istället
  för hårdkodade defaults — kontrakts-konsistens på äkta nivå.
- **Separat count-query** följer §3.6 exakt. Kod-kommentar refererar
  konventionen explicit, vilket gör framtida granskning enklare.
- **`PagedResultContractTests`** är just-in-case-skydd för en kontrakts-klass
  som annars är lätt att glömma. Reflection-baserad arkitektur-test är
  precis rätt verktyg för "alla typer av kategori X måste följa regel Y".
- **`ListJobAds` hard cap med motivering** — defense-in-depth utan att
  blanda in full paginerings-design som inte kan slutföras innan JobTech-API
  är kartlagt. Det är pragmatisk arkitektur, inte teknisk skuld i skuldens
  ordinära mening.
- **Två-commit-split** är disciplinerat. Frontend och backend kan revertas
  oberoende. Det är värd lite extra commit-message-arbete.
- **Frontend type-guard** är minimalt, korrekt och citerar konventionsregel
  i kod-kommentar. Återanvändbart pattern för framtida endpoint-uppdateringar.
- **Regression-test** `Handle_TotalCount_IsIndependentOfPageSize` är exakt
  rätt assertion — den fångar exakt den bug-klass TD-55 stängde. Bättre
  än att bara assert:a "paginering funkar".

---

## Verdict

**APPROVE** — direct-push till `main` per ADR 0019 godkänt.

Inga blockers, inga majors. Minor 1 (TD-NY-platshållare) bör fixas i en
quick-touch-commit innan push om TD-numret är allokerat. Minor 2
(architecture-test reflection-svaghet) kan adresseras i Fas 2 eller
in-block — Klas-beslut.

Inga TD-skapande-rekommendationer från denna review. CTO-beslutet att
deferera `ListJobAds` full paginering till Fas 2 är välmotiverat (saknad
function-dependency: JobTech-API-kontraktet, §9.6 kriterium 2).

---

**Slut på rapport.**
