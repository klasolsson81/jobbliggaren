# 2026-09-06 — #1681 part 2 — dotnet-architect

**Fas:** #1681 part 2 (ADR 0139) — annonsläsvägen på materialiserad mängd
**HEAD:** `7c577203` · **Bas:** `origin/main` @ `c2d782e8` · **Branch:** `feat/1681-read-path`
**Läge:** report-only, första ronden. Ingen fil rörd.
**Transkriberad av sessionen** — agentens charter är read-only (inget `Write`/`Edit`).

## Sammanfattning

**0 kritiska, 3 viktiga, 4 nice-to-have.** Lagergränsen, de två nyckeltyperna, den råa
SQL-kompositionen och transaktionsdisciplinen håller alla. Fynden ligger i **resolvern** (den bundna
batchen skapade en andra kopia av guard-kedjan och en osymmetrisk memo) och i **en pinne som inte
täcker den klausul dess egen docblock utpekar som kontraktsbärande**.

## Domar på de sex frågorna

1. **Lagergränsen — HÅLLER.** Varken `CompanyWatchCriterionMember` eller
   `CompanyWatchCriterionMaterialisation` når `IAppDbContext`; `ScbCompanyRegisterLayerTests:92–93`
   namnger båda explicit och failar bygget. Application refererar dem bara som `<c>`-text, aldrig som
   typer. Resultattyperna bär enbart `enum` / `int` / `JobAdId` / `PagedResult<JobAdId>`.
2. **Application ÄR rätt hem för de nya typerna.** De är portens parametrar och returvärden; Domain
   vore fel (materialiseringens färskhet är ingen domäninvariant), Infrastructure ett gränsbrott.
   Samma dom för `CriteriaFingerprint`.
3. **Två nyckeltyper — resonemanget FINNS och är KORREKT.** Företagshalvans levande anropare
   verifierad: `PreviewCriterionMatchMagnitudeQueryHandler:37`.
4. **Lateral-distinktionen HÅLLER** — på starkare grund än den nedskrivna, se Nice-to-have 4.
5. **Transaktions-/change-tracker-disciplinen ORÖRD.** `CriteriaFingerprint.Of` är en ren funktion,
   beräknad före både `SelectCandidatesAsync` och `ReplaceAsync`.
6. **AGENTS.md §2.1 axel 3 — inget korsade.** Inga nya `PackageReference` i Application; noll
   `Npgsql` / `.Relational` / `AsSplitQuery` / `EF.Functions` bland tillagda rader.

## Fynd

### [Viktigt] Guard-kedjan är skriven två gånger
`CriterionMatchingAdSetResolver.cs:177` och `:268`

`MatchingBatchAsync` (fas 1) och `ResolveAsync` bär var sin identiska sekvens: assessability →
`magnitude.TooBroad` → `magnitude.NotMaterialised` → `Magnitude > MaxSetSize` →
`ListActiveAdIdsAsync` → `Refused` / `TooBroad` / `NotMaterialised` → filtrera den ordnade listan.
Ekvivalenta i dag; två kopior i morgon.

CTO:ns bindning var *"batchad gradering på resolvern, inte i listhandlern"* — uppfylld på
**typnivå**, men klassens egen docblock motiverar sin existens med *"a second copy would drift"*.
PR:en skapar just den andra kopian, en nivå in — den vacuous-guarantee-klass repot namnger två
gånger (#805-3, #842). AGENTS.md §2.3, SRP.

**Åtgärd:** faktorisera ut det delade steget så guard-ordningen har exakt ett hem
(`ResolveIdsAsync(id, criteria, ct) → (Terminal?, Ids?)`).

### [Viktigt] Memon är enkelriktad medan docblocken påstår att den är dubbelriktad
`CriterionMatchingAdSetResolver.cs:172` (påstående) och `:177–` (koden)

`MatchingBatchAsync` läser **aldrig** `_matching` innan den löser; `MatchingAsync:141` gör det.
Anropas single före batch mäts kriteriet en **andra** gång och `_matching[id]` skrivs över.
Docblocken påstår motsatsen utan förbehåll: *"cannot produce a SECOND measurement of the same
fact."*

**Ej nåbar i dag** — listrutten kör bara batchen, detaljrutterna bara single, resolvern är
request-scopad. Alltså en invariant utan täckning, inte en levande bugg. Men påståendet är skrivet
som om täckningen fanns (§5 `Comments:`).

**Åtgärd:** memo-check först i fas 1-loopen; spegelvänt test till
`MatchingBatchAsync_MemoisesIntoTheSameMapsTheSingleCriterionPathReads:458`.

### [Viktigt] Ordningspinnen täcker den INRE klausulen, porten utpekar den YTTRE
`CompanyWatchBrowseQueryPlanTests.cs:610`

`AdIdSetQuery_OrdersByATotalKey` asserterar `"ORDER BY j.published_at DESC, j.id"` — lateralens
inre ordning. Den yttre `ORDER BY a.published_at DESC, a.id`, som läsaren faktiskt observerar, är
opinnad: raderas den förblir pinnen grön.

Portens docblock (`CompanyWatchBrowseQuery.cs:320`) utpekar uttryckligen den **yttre** som
kontraktsbärande. Det behavioural syskonet fångar det inte tillförlitligt — en Nested Loop bevarar i
praktiken den inre ordningen, vilket är hela skälet docblocken avvisar att luta sig mot den.
Konsumenten är inte kosmetisk: `CriterionMatchingAdSetResolver` filtrerar den ordnade listan för att
producera både annonsordningen och talets destination.

**Åtgärd:** en andra `ShouldContain("ORDER BY a.published_at DESC, a.id")` i samma test.

### [Nice-to-have] Lateral-grunden är den svagare av två tillgängliga
`CompanyWatchBrowseQuery.cs:306–325`

Distinktionen mot ADR 0139:s avvisade lateral **håller**, men den nedskrivna grunden är
*"real table vs function scan"* plus en buffertsiffra. Den strukturella grunden är starkare och
förfaller inte: (a) den yttre relationen är pinnad på PK, så lateralen exekverar **högst en gång**,
medan den avvisade formen hade N yttre rader; (b) `ARRAY(SELECT … WHERE criterion_id = @criterion_id)`
refererar ingen yttre kolumn och hissas till en `InitPlan`. En lateral över en rad är ett omslag,
inte en fan-out. Buffertsiffran är dessutom ett levande mätvärde i en spårad fil (§5 `Comments:`).

### [Nice-to-have] Odokumenterad invariant på just den axel någon frestas att ändra
`CompanyWatchBrowseQuery.cs:254`

Inuti lateralen slås medlemsmängden upp på `@criterion_id`, inte `m.criterion_id` — korrekt i dag
enbart därför att den yttre `WHERE` pinnar exakt en rad. En framtida batchning skulle läsa **samma**
medlemsmängd för varje kriterium och tyst attribuera en bevaknings annonser till en annan.

### [Nice-to-have] "Free after MatchingBatchAsync" är falskt i NotAssessed-armen
`ListCompanyWatchCriteriaQueryHandler.cs:88` — slår assessability-grinden returnerar batchen innan
`MagnitudeAsync` anropas, så loopen betalar N omemoiserade `CountActiveAdsAsync`. Ingen
kostnadsregression, men tillståndet är det vanliga tidigt i onboarding.

### [Nice-to-have] "Unforgettable" gäller porten, inte assemblyt
`CompanyWatchBrowseQuery.cs:521` — `MaterialisedAdIdsSql` bär ingen grind och `BuildAdIdsCommand`
tar inget fingerprint; korrektheten vilar på att `BrowseAdIdsAsync` anropar `ReadAdCountAsync`
först. Beslutet är rätt (grinden betalas en gång); formuleringen bör namnge sekvensregeln.

## Vad agenten uttryckligen INTE graderar som fynd

- **Den råa SQL-kompositionen är korrekt.** Alla tre satserna komponerades för hand ur exakta bytes
  (`cat -A`) och varje söm bär sitt separerande tecken. Klassen är dessutom bevisad, inte bara läst:
  texterna **exekveras** mot riktig Postgres i Worker.IntegrationTests, och den söm som redan
  shippat en gång ger syntaxfel — den kan inte gå tyst.
- `enable_seqscan = off` i `ExplainMaterialisedAsync` — regressionen är strukturellt frånvarande här
  och begränsningen står i hjälparens egen docblock.
- Count-then-page-glappet i `BrowseAdIdsAsync` — husets sanktionerade mönster, §3.6.
- `MaterialisationState`-asymmetrin mellan fail-loud NULL och ärlig nolla — onåbar, enumen är sluten.
- Fan-in-mätningen mot riktigt schema — känd utestående, åter-reses inte.
