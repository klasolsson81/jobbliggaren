# 2026-09-06 — #1681 part 2 — code-reviewer

## Code-review: #1681 part 2 — materialiserad läsväg (ADR 0139) (PR #1691)
**Status:** ⚠ Changes requested
**Auktoritet:** CLAUDE.md §2.1, §2.3, §3.6, §5 (`Comments:`, `Tests:`), §7, §9.6, §10
**Scope:** Application · Infrastructure (raw SQL + migration) · Api-integrationstester · Web (RSC,
DTO/ACL, messages). Bas `origin/main` `c2d782e8`, HEAD `7c577203`.
**Bundet och EJ omprövat:** formvalet (Form 1, `= ANY(ARRAY(subselect))`), en form för båda ytorna,
batchad grading, vilka läsningar som flyttar, fingerprint som diskriminator, `MaxPerCriterion = 1000`,
att bolagssidans läsningar stannar registerbundna, och att `ListCompanyWatchCriteriaQuery` blir
tyngre för befintliga konsumenter (`docs/reviews/2026-09-06-1681-part2-form-cto.md`). Den utestående
fan-in-mätningen mot verkligt schema rapporteras inte som fynd.

**Rå-SQL-sömmarna: kontrollerade, alla korrekta.** Alla sex komponerade satser (`ItemsSql`,
`CountSql`, `MaterialisedAdCountSql`, `MaterialisedAdIdSetSql`, `MaterialisedAdIdsSql`, samt
`FromWhere`/`MaterialisedAdsFromWhere`/`MaterialisedAdsOrderBy`) lästes som komponerad text, inte som
fragment, och den exakta inledande blanktecken-/radbrytningsformen verifierades byte för byte
(`cat -A`). Varje söm bär antingen ett ledande blanksteg i en vanlig citerad sträng eller en ledande
radbrytning i ett raw string literal. Parameterlistan matchar satsen i alla tre `Build*Command`.
Ingen upprepning av `j.published_atFROM`-defekten.

### Blockers
Inga.

### Major

1. **`?visa=matchande` påstår "alla aktiva annonser visas här" ovanför en TOM lista i två av fyra
   tillstånd** — Fil: `web/jobbliggaren-web/src/app/(app)/foretag/smarta-bevakningar/[id]/annonser/page.tsx:232-246`
   (kopian), `src/Jobbliggaren.Application/CompanyWatches/Queries/BrowseCriterionAds/BrowseCriterionAdsQueryHandler.cs:132-133`
   (den tomma sidan)
   Nuvarande: `BrowseAdIdsAsync` returnerar en TOM sida när kriteriet är `TooBroad` eller
   `NotMaterialised`. I `OnlyMatching`-armen faller `SetTooLarge` och `NotMaterialised` igenom till
   just den läsningen (`:81-109`), så sidan renderar `ads.matchingTooBroadOnList` ("…så alla aktiva
   annonser visas här") respektive `ads.matchingNotMaterialisedOnList` ("…så alla aktiva annonser
   visas här") **medan `ads.items` är tom**. Tomtillståndet är korrekt undertryckt (`:286`), så
   användaren ser en rubrik, en mening som säger att alla annonser visas, och sedan ingenting — en
   mening som är falsk om det som står på skärmen, och vars tomma lista läses som en nolla.
   `matchingNotMaterialisedOnList` är falsk i 100 % av de fall den renderas; `matchingTooBroadOnList`
   är falsk exakt i den nya orsaken (materialiseringens breddgrind), sann i de två gamla
   (`> MaxSetSize`, portens vägran). Nåbart: varje predikatredigering gör alla den användarens
   `?visa=matchande`-länkar till detta tillstånd fram till nästa körning — vilket är precis fönstret
   fingerprintgrinden finns för — plus paginering (`buildHref` bevarar `scope`), bokmärke och direkt
   URL.
   Krävs: armen får inte påstå att den ofiltrerade listan visas när den inte gör det. Antingen egen
   copy för de två materialiseringstillstånden i den filtrerade armen, eller samma "ingen lista"-
   behandling som den ofiltrerade armen redan ger. Samma fix måste stryka de påståenden ändringen
   falsifierade: `BrowseCriterionAdsQueryHandler.cs:49-54` ("the filter is INERT rather than empty in
   the **two** unanswerable arms … **both get the UNFILTERED list**" — det är tre armar nu, och två av
   dem får ingen lista), `:86-88` (räknar upp `NotAssessed` och `SetTooLarge`, utelämnar
   `NotMaterialised` som också faller igenom) och `src/Jobbliggaren.Api/Endpoints/CompanyWatchCriteriaEndpoints.cs:117-118`
   (samma påstående, utanför deltat men falsifierat av det).
   Motivering: CLAUDE.md §5 (`Frontend:` — en tom lista under en mening som lovar en lista ÄR den
   oärliga nollan ADR 0120 finns emot), §5 `Comments:` (faktafel), §10.
   Delegera till: `nextjs-ui-engineer` (copy + armarna), `dotnet-architect` (docblocken)

2. **"Free after MatchingBatchAsync" är falskt på den obedömbara vägen** — Fil:
   `src/Jobbliggaren.Application/CompanyWatches/Queries/ListCompanyWatchCriteria/ListCompanyWatchCriteriaQueryHandler.cs:88-90`
   Nuvarande: kommentaren säger *"Free after MatchingBatchAsync: the magnitude was measured there and
   memoised…"*. När profilen saknar SSYK-grupper returnerar `MatchingBatchAsync` tidigt
   (`CriterionMatchingAdSetResolver.cs:190-200`) **utan att någonsin anropa `MagnitudeAsync`**, så
   raden nedan kostar en `CountActiveAdsAsync` per kriterium — upp till 20 satser som kommentaren
   säger är gratis. Handlerns egen test bevisar det:
   `tests/Jobbliggaren.Application.UnitTests/CompanyWatches/Queries/ListCompanyWatchCriteriaQueryHandlerTests.cs:289-320`
   stubbar `CountFor` per kriterium och asserterar att magnituden *ändå* besvaras.
   Krävs: den andra halvan av meningen ("cannot become a SECOND measurement") är sann alltid och kan
   stå; "Free after MatchingBatchAsync … was measured there" ska strykas eller villkoras. Fynd
   stängbart genom radering.
   Motivering: CLAUDE.md §5 `Comments:` — faktafel är en defekt och fixas. Det biter extra här: hela
   PR:ns satsbudget mot `MeListRead` är den siffra CTO:n eskalerar till `security-auditor`, och detta
   är kommentaren som underskattar den.
   Delegera till: `dotnet-architect`

3. **Två docblock räknar armar som inte längre stämmer** — Fil:
   `src/Jobbliggaren.Application/CompanyWatches/Queries/CriterionMatchingAdSetResolver.cs:323, :329`
   och `web/jobbliggaren-web/src/app/(app)/foretag/smarta-bevakningar/[id]/page.tsx:199`
   Nuvarande: `CriterionMatchingAds` bär numera FYRA armar (`Resolved`, `NotAssessed`, `SetTooLarge`,
   `NotMaterialised`) men typens docblock inleder *"The three answers this question has, as a CLOSED
   hierarchy — the private constructor means no fourth kind can be declared elsewhere"* och räknar på
   `:329` upp tre av fyra ("are three DIFFERENT things and a consumer must not collapse them") — utan
   `NotMaterialised`, alltså utan armen hela PR:n handlar om. På FE-sidan säger kommentaren över
   personblocket *"Four states and none of them collapses into another"* och räknar upp tre plus den
   degraderade läsningen; deltat lade till en femte renderad arm (`matching.notMaterialised`,
   `:211-215`) utan att röra räkningen.
   Krävs: siffrorna och uppräkningarna ska stämma med koden (fyra respektive fem), eller strykas.
   Motivering: CLAUDE.md §5 `Comments:` — fel siffra är en defekt. Att det är just
   *icke-kollapsnings*-doktrinen som räknar fel gör den till dokumentationsdefekt i den regel PR:n
   levererar.
   Delegera till: `dotnet-architect` (BE), `nextjs-ui-engineer` (FE)

4. **Plan-påståendena om LATERAL:en har inget hem** — Fil:
   `src/Jobbliggaren.Infrastructure/CompanyRegister/CompanyWatchBrowseQuery.cs:313-316` och
   `:279-283`
   Nuvarande: `MaterialisedAdIdSetSql`-docblocket säger *"the plan is unchanged from the un-wrapped
   statement: same Index Only Scan (Heap Fetches: 0), same Bitmap Index Scan, **44 buffers against
   43**. The gate appears as a `One-Time Filter`"*, och `MaterialisedGate` säger *"verified in the
   plan as `One-Time Filter`"*. Ingendera finns i mätrapporten stycket självt citerar två stycken
   tidigare — `docs/reviews/2026-09-06-1681-part2-read-form-measurement.md` Result 3 (`:144-168`)
   redovisar den **o-wrappade** satsen, `shared hit=43`, ingen lateral och ingen `One-Time Filter`.
   Strängarna "44 buffers", "One-Time" och "lateral" finns inte i rapporten, inte i ADR 0139, och
   pinnas inte av `CompanyWatchBrowseQueryPlanTests`. Docblockets egen retorik är *"was measured, not
   assumed"*, och en läsare som följer den närliggande pekaren hittar 43 och ingen lateral. Dessutom
   generaliseras `One-Time Filter` från `MaterialisedGate` till BÅDA satserna, men i
   `MaterialisedAdCountSql` sitter grinden i ett `CASE`-uttryck, inte i ett `WHERE` — där visar en
   plan inte `One-Time Filter`.
   Krävs: antingen mätningen skrivs in i rapporten den citerar (med form, datum och kommando), eller
   så stryks de omätta meningarna. Filen har husprecedensen på rad `:132-138`: *"The numbers this
   paragraph used to cite have been withdrawn … Measure it before quoting it."*
   Motivering: CLAUDE.md §5 `Comments:` (levande mätt siffra i spårad fil utan
   regenereringsväg), §9.6 Filing discipline-doktrinen om odaterade påståenden.
   Delegera till: `dotnet-architect`

### Minor

1. **28 971 har fått ett andra hem** — Fil:
   `src/Jobbliggaren.Application/CompanyWatches/Abstractions/ICompanyWatchBrowseQuery.cs:170` och
   `src/Jobbliggaren.Application/CompanyWatches/Queries/GetCriterionAdMagnitude/GetCriterionAdMagnitudeQuery.cs:119`
   Nuvarande: samma daterade mätning ("28 971 aktiva annonser", "~3x this ceiling"), samma argument
   (taket är icke-vakuöst), i två docblock. Vid nästa ommätning uppdateras en av dem.
   Krävs: ett hem — det andra pekar dit. Motivering: CLAUDE.md §5 `Comments:`.
   Delegera till: `dotnet-architect`

2. **Bar `!` i renderingen, mot syskonfilens egen uttalade regel** — Fil:
   `web/jobbliggaren-web/src/app/(app)/foretag/smarta-bevakningar/[id]/annonser/page.tsx:227`
   Nuvarande: `count: magnitudeText!`. Detaljsidan i samma PR (`[id]/page.tsx:125-133`) inför ett
   lokalt narrowing uttryckligen för att slippa detta ("non-null assertions, which would be claims
   the type system cannot check"). Krävs: samma narrowing här. Motivering: CLAUDE.md §4.
   Delegera till: `nextjs-ui-engineer`

3. **Två formatteringsrester** — Fil: `web/jobbliggaren-web/src/lib/dto/company-criteria.ts:249-250`
   (dubbel tomrad efter det flyttade blocket) och
   `src/Jobbliggaren.Application/CompanyWatches/Abstractions/ICompanyWatchBrowseQuery.cs:119-120`
   (ingen tomrad mellan `CountMatchingCompaniesAsync`-signaturen och nästa `/// <summary>`, så
   XML-docen klistras mot föregående metod). Motivering: CLAUDE.md §3, §4.
   Delegera till: `nextjs-ui-engineer` / `dotnet-architect`

### Bra gjort
- Integrationstesterna skriver medlems- och tillståndsrader genom **produktionsmaterialiseraren**,
  skapar kriteriet via aggregatets factory och redigerar via `UpdateCriteria`/`Rename` — inklusive
  negativa kontroller och den rename-arm som ensam utesluter tidsstämpelgrinden (§5 `Tests:` uppfyllt
  utan undantag).
- Sömkommentaren på `MaterialisedAdIdSetSql:332-336` gör den skeppade raw-string-defekten
  orepresenterbar genom att fixera formen (avslutande blanksteg i vanlig sträng), i stället för att
  be nästa läsare komma ihåg den.
- Fyrtillståndsdisciplinen bärs strukturellt i båda ändar: konstruktorerna i `MaterialisedAdCount` /
  `MaterialisedAdIds` / `MaterialisedAdPage` / `CriterionAdMagnitudeDto` avvisar varje omöjlig
  kombination, och zod-`refine` gör samma sak vid ACL-gränsen.

### Sammanfattning
0 blockers, 4 major, 3 minor. Major 2 och 3 stängs mekaniskt genom radering; major 1 kräver copy och
armval (`nextjs-ui-engineer`) plus tre docblock-strykningar; major 4 stängs genom radering eller
genom att mätningen skrivs in i rapporten den citerar. Rå-SQL-sömmarna och migrationen är rena, och
inget fynd rör de val `senior-cto-advisor` band. Re-review efter fix: samma agent, report-only,
scopad till fix-deltat (CLAUDE.md §9.6).
