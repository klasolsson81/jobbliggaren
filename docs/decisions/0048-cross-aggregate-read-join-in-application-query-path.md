# ADR 0048 — Cross-aggregat-read-join i Application-query-vägen (in-handler vs read-model-port)

**Datum:** 2026-05-17
**Status:** Accepted
**Kontext:** in-block del av STOPP 2 plan-godkännande. FAS 3 /ansokningar-redesign — Klas underkände list-vyn live 2 ggr (rader visade UUID, ej jobbtitel/företag). Discovery (STOPP 1) fann att Application-read-vägen aldrig joinar JobAd-aggregatet. Detta är första cross-aggregat-joinen i Application-läsvägen i kodbasen och behöver en mönster-precedens som avgränsar mot ADR 0043:s read-model-port.
**Beslutsfattare:** senior-cto-advisor (agentId ac00cccfcd6962a67 — STOPP 2-triage, query-filter-disciplin Beslut 2; agentId ad50e53e28872171d — rev2, write-side-avgränsning); Klas Olsson (GO 2026-05-17 — "ADR 0048 = Accepted direkt, ej Proposed"; motiv: "Proposed→Accepted-cooldown onödig formalism när motiveringen finns och jag är beslutsfattaren oavsett"); Claude Code (ADR-leverans denna session)
**Relaterad:** ADR 0043 (taxonomi-ACL read-model-port — **kontrast/avgränsning, EJ supersession**; in-handler-join här är medvetet motsatsen till 0043:s port, motiverat av frånvaron av anti-corruption/context-gräns), ADR 0009 (ingen Repository — aggregate-per-DbSet-invariant; `IAppDbContext` växer EJ av denna join), ADR 0032 (JobTech-integration / JobAd soft-delete + EF global query filter), ADR 0046 (FAS 3-scope — kontexten denna join byggs i), ADR 0047 (design-reviewer Area 5 — varför /ansokningar omarbetas), ADR 0020 (Zod single source — frontend speglar `JobAdSummaryDto`). Relaterade: `docs/design/ansokningar-redesign-plan.md` §1.1–§1.5, CLAUDE.md §2.3 (CQRS), §3.6 (AsNoTracking/projektion), §8 punkt 9 (ADR = DoD).

---

## Kontext

FAS 3 /ansokningar-redesign. Klas underkände `/ansokningar` live **två gånger** — list-raderna visade UUID (`application.id.slice(0,8)`) i stället för jobbtitel och företag. Discovery (STOPP 1, `grep` + filläsning, ej gissning) fann rotorsaken: Application-read-vägen (`ApplicationDto` / `ApplicationDetailDto`, tre query-handlers `GetPipeline` / `GetApplicationById` / `GetApplications`) projicerar **endast `JobAdId` (rå Guid)**. JobAd-aggregatet (`Title` / `Company` / `Url` / `Source` / `PublishedAt` / `ExpiresAt`) joinas **aldrig** — frontend hade ingen jobb-metadata att rendera, därav UUID-fallback.

Lösningen är en left join av JobAd i de tre handlers + en ny `JobAdSummaryDto?` på DTO:erna. Detta är den **första cross-aggregat-joinen i Application-läsvägen** i kodbasen, vilket gör mönstervalet till ett arkitekturbeslut, inte en lokal fix.

Krafter som spelar in:

- **ADR 0043-precedensen drar mot en read-model-port.** ADR 0043 löste cross-context-läsning (JobTech-taxonomi) via en dedikerad `ITaxonomyReadModel`-port **specifikt för att inte** införa cross-aggregat-koppling. En naiv läsning av 0043 skulle kräva en `IJobAdReadModel`-port även här.
- **Men 0043:s motiv gäller inte här.** 0043:s port motiverades av (1) **anti-corruption** — extern JobTech-jargong och concept-id-översättning (Evans 2003 kap. 14), och (2) **ADR 0009-invarianten** — `TaxonomyConcept` är inget aggregate root och får inte ligga på `IAppDbContext`. Application↔JobAd är en **enkel intern 1:0..1-länk i samma DbContext**: ingen extern modell, ingen översättning, ingen bounded-context-gräns, båda är redan aggregate roots på `IAppDbContext`. Att införa en port här vore **spekulativ inkapsling** (YAGNI; Fowler 2018 kap. 3 — Speculative Generality) — exakt det anti-mönster ADR 0043 avvisade i sin *negativa* riktning (Beslut B/MAP-1 Variant B/C).
- **JobAd har en EF global query filter** (`JobAdConfiguration.cs:82` — `HasQueryFilter(j => j.DeletedAt == null)`, ADR 0032 soft-delete-semantik). Joinen måste respektera den utan att duplicera invarianten.
- **Skrivvägen får inte dras med.** Manuella ansökningars skrivväg är ett separat beslut (`ManualPosting` value object, dotnet-architect agentId a4c1483aeaee7fcea Variant A) — ADR 0048 får inte tolkas som mandat för cross-aggregat multi-aggregate-create på write-side.

## Beslut

> Beslut fattat av senior-cto-advisor (agentId ac00cccfcd6962a67 STOPP 2 + ad50e53e28872171d rev2), Klas-GO 2026-05-17. Status **Accepted** — explicit Klas-GO ("ADR 0048 = Accepted direkt, ej Proposed"; Klas är beslutsfattare oavsett, Proposed→Accepted-cooldown är onödig formalism när motiveringen finns).

### Beslut (a) — In-handler cross-aggregat-read-join är det godkända mönstret

In-handler **left join + DTO-projektion** är det godkända mönstret för **enkla samma-DbContext 1:0..1-aggregatlänkar** i CQRS-read-vägen. Inget Domain-objekt och ingen EF-entity passerar Application-gränsen — endast read-DTO ut (`JobAdSummaryDto?` nästlad i `ApplicationDto` / `ApplicationDetailDto`). `.AsNoTracking()` bevaras på query-vägen (CLAUDE.md §3.6). Joinen är en read-side-projektion, inte en domän-koppling: aggregaten refererar fortfarande varandra endast via strongly-typed id (`JobAdId`), join:en lever uteslutande i query-handlern.

**Tillämpningsundantag (additivt, senior-cto-advisor-triage 2026-05-18, TD-13/ADR 0049 C3 — EJ supersession):** kolumn-**projektionsmönstret** ovan gäller cross-aggregat-metadata (JobAd → `JobAdSummaryDto?`). Det gäller **inte** intra-aggregat krypterade PII-kolumner (TD-13/ADR 0049: `applications.cover_letter`, `application_notes.content`, `follow_ups.note`, `resume_versions.content`). En SQL-projektion av ett krypterat fält kringgår `FieldDecryptionMaterializationInterceptor` (EF Core 10: interceptorn triggar endast vid entitets-materialisering — ADR 0049 Mekanik-not 4). Handlers som returnerar ett krypterat fält måste därför **materialisera den ägande entiteten** och mappa i minnet. Cross-aggregat-left-join-delen (JobAd) förblir projicerad oförändrad — undantaget rör enbart de krypterade egen-aggregat-fälten. En arch-test-spärr verkställer invarianten. Cross-ref ADR 0049 Mekanik-not 4.

### Beslut (b) — Kontrast/avgränsning mot ADR 0043 (komplementär, EJ supersession)

En **read-model-port** (som ADR 0043:s `ITaxonomyReadModel`) krävs när cross-context-läsning involverar **anti-corruption** (översättning av en extern modell) **eller** en **bounded-context-gräns**. En **in-handler-join** räcker för **interna enkla aggregatlänkar inom samma DbContext utan översättning**. ADR 0048 **superseder inte** ADR 0043 — det är en **komplementär avgränsning** (när-vilket): 0043 gäller fortfarande oförändrat för taxonomi-ytan; 0048 fastställer regeln för den andra klassen av cross-läsning. Beslutsregeln framåt: extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join.

### Beslut (c) — Query-filter-disciplin (CTO Beslut 2, STOPP 2)

JobAd har en EF global query filter (`JobAdConfiguration.cs:82` — `HasQueryFilter(j => j.DeletedAt == null)`, ADR 0032). I dessa join-handlers:

- **`IgnoreQueryFilters()` är FÖRBJUDET** — skulle exponera soft-deletad annons-metadata och vore en regression mot ADR 0032 soft-delete-semantik.
- **Manuell `DeletedAt`-predikat i handlern är FÖRBJUDET** — dubblerar query-filter-invarianten (DRY/SPOT-brott, Hunt/Thomas 1999; invarianten har redan en SPOT i `JobAdConfiguration`).
- En soft-deletad JobAd → fallback sker via **default-joinen**: query-filtret exkluderar den raden **före** join, `DefaultIfEmpty` ger `null`, och `JobAdSummaryDto?` blir `null`. Inget special-case-fall behövs — den korrekta beteendet faller ut av att respektera query-filtret.

### Beslut (d) — Write-side-avgränsning (CTO rev2)

ADR 0048 gäller **enbart read-vägen**. Skrivvägen för manuella ansökningar håller sig **inom Application-aggregatet** via `ManualPosting` value object (dotnet-architect agentId a4c1483aeaee7fcea, Variant A). Variant B ("lokal JobAd `Source=Manual`") avvisades på tre kod-verifierade invariant-brott + GDPR-ACB **just för att inte** vidga cross-aggregat-kopplingen till write-side multi-aggregate-create. ADR 0048 **vidgas EJ** till write-side och får inte citeras som precedens för cross-aggregat-skrivning.

## Alternativ som övervägdes

### Alt 1 — In-handler left join + DTO-projektion (VALT)
**För:**
- Löser rotorsaken (jobbidentitet i list-/detaljvyn) direkt på den yta felet finns.
- Ingen spekulativ inkapsling — en port här löser inget problem som faktiskt finns (ingen extern modell, ingen översättning, ingen context-gräns).
- `IAppDbContext` växer inte — båda aggregaten är redan DbSets (ADR 0009-invarianten orörd).
- `.AsNoTracking()` + DTO-projektion bevarar read-vägs-disciplinen (CLAUDE.md §3.6).
**Emot:**
- En ny join-yta att underhålla i tre handlers.
- Precedensen kan missbrukas för komplexa joins där en read-model vore lämpligare — mitigeras av avgränsningen i Beslut (b).

### Alt 2 — Dedikerad `IJobAdReadModel`-port (likt ADR 0043) (AVVISAT)
**För:**
- Konsekvent med ADR 0043:s port-mönster vid en ytlig läsning.
**Emot:**
- Spekulativ inkapsling (YAGNI; Fowler 2018 kap. 3 — Speculative Generality). 0043:s två motiv (anti-corruption, ADR 0009-invariant) gäller **inte** Application↔JobAd: enkel intern 1:0..1-länk, samma DbContext, ingen extern översättning, båda redan aggregate roots.
- Inför exakt det anti-mönster ADR 0043 avvisade i sin negativa riktning — port utan problem att lösa.

### Alt 3 — Ingen join; behåll UUID-raden (AVVISAT)
**För:**
- Noll kodändring.
**Emot:**
- Klas underkände vyn **två gånger** — UUID-rad är den underkända ytan, inte en accepterad fallback.
- Löser inte rotorsaken (Application-read-vägen saknar jobb-metadata helt).

## Konsekvenser

### Positiva
- Rotorsaksåtgärd för jobbidentitet i `/ansokningar` list-/detaljvyn — Klas-underkännandet adresseras vid källan, inte symptomatiskt.
- Mönster-precedens dokumenterad: framtida cross-aggregat-läsning vet **när join vs port** (Beslut b-avgränsningen) — beslutet blir inte godtyckligt nästa gång.
- Query-filter-disciplinen är inskriven (Beslut c) — soft-delete-semantiken (ADR 0032) skyddas och dubbel-invariant undviks redan i mönstret.
- ADR 0043-relationen är explicit komplementär (ej supersession) — ingen falsk konflikt mellan de två cross-läsnings-mönstren.

### Negativa
- **En till query-vägs-join-yta att underhålla** (tre handlers). Mitigering: trivial 1:0..1 left join + DTO-projektion, inom samma DbContext — ingen ny infrastruktur, ingen port-fil, ingen extern hop.
- **Precedensen kan missbrukas** för komplexa joins där en read-model vore bättre. Mitigering: Beslut (b)-avgränsningen är formulerad som beslutsregel (extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join), inte som öppet mandat.

## Implementation

- **Tre query-handlers** (`GetPipeline` / `GetApplicationById` / `GetApplications`) utökas med left join av JobAd; `.AsNoTracking()` bevaras; ingen `IgnoreQueryFilters()`, inget manuellt `DeletedAt`-predikat (Beslut c).
- **`JobAdSummaryDto?`** (`record class`, CLAUDE.md §3.3) läggs till nästlad i `ApplicationDto` / `ApplicationDetailDto` — `null` när JobAd saknas eller är soft-deletad (faller ut av query-filtret + `DefaultIfEmpty`).
- **Frontend** speglar `JobAdSummaryDto` via Zod (ADR 0020 — single source) och renderar titel/företag i stället för UUID-slice.
- **Write-side orörd** — `ManualPosting` value object-vägen (dotnet-architect Variant A) ligger utanför denna ADR:s scope (Beslut d).
- **Gates:** code-reviewer + design-reviewer (Area 5 flödesbegriplighet, ADR 0047) på den omarbetade `/ansokningar`-ytan; visual-verify (Klas godkänner skärmbilder) per FAS 3-stängnings-VETO (ADR 0046).
- **ADR-index** (`docs/decisions/README.md`) uppdaterat additivt med ADR 0048-raden; bidirektionell cross-ref till ADR 0043 verifieras av docs-keeper vid session-end (se Referenser).

## Referenser

- CLAUDE.md §2.3 (CQRS — read-DTO ut, inget Domain-objekt över gränsen), §3.6 (`.AsNoTracking()` default + projektion till DTO), §3.3 (DTO = `record class`), §8 punkt 9 (ADR = DoD vid arkitekturbeslut)
- `docs/design/ansokningar-redesign-plan.md` §1.1–§1.5 (discovery + redesign-plan, STOPP 1)
- ADR 0043 (taxonomi-ACL read-model-port — **kontrast/avgränsning, EJ supersession**; in-handler-join är medvetet motsatsen, motiverad av frånvaron av anti-corruption/context-gräns)
- ADR 0009 (ingen Repository — aggregate-per-DbSet; `IAppDbContext` växer ej), ADR 0032 (JobAd soft-delete + EF global query filter `JobAdConfiguration.cs:82`), ADR 0046 (FAS 3-scope), ADR 0047 (design-reviewer Area 5 — kontext varför /ansokningar omarbetas), ADR 0020 (Zod single source — frontend speglar `JobAdSummaryDto`)
- Eric Evans, *Domain-Driven Design* (2003) kap. 14 (Bounded Context / Anticorruption Layer — ADR 0043-kontrastgrunden)
- Robert C. Martin, *Clean Architecture* (2017) (CQRS read-model på Application-gränsen)
- Martin Fowler, *Refactoring* 2nd ed (2018) kap. 3 (Speculative Generality — Alt 2-avvisningsgrund)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) DRY/SPOT (Beslut c — ingen dubblerad query-filter-invariant)
- Nygard, *Documenting Architecture Decisions* (2011) (ADR-värdhet — mönster-precedens som framtida läsare kan följa)
- Beslutsunderlag: senior-cto-advisor agentId ac00cccfcd6962a67 (STOPP 2), agentId ad50e53e28872171d (rev2); dotnet-architect agentId a4c1483aeaee7fcea (write-side Variant A)

---

*ADR-index underhålls av docs-keeper. Detta beslut fastställer in-handler cross-aggregat-read-join som godkänt mönster för enkla samma-DbContext-aggregatlänkar i CQRS-read-vägen, avgränsat mot ADR 0043:s read-model-port (komplementär, ej supersession) och mot write-side (ej vidgad).*
