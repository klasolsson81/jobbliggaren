# Security-audit: SavedSearch namn-berikning (ADR 0043 Approach A, commit 04b679e)

**Status:** GO — 0 Critical / 0 High / 0 GDPR-Blocker
**Granskad:** 2026-05-17
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.3/§5.4, ADR 0039/0043
**Typ:** Lättviktig BLOCKING-input (read-projektion, ingen ny endpoint/migration/auth-yta)
**Granskare:** security-auditor (VETO-rätt — ej utnyttjad)

CTO:s lågrisk-bedömning bekräftas. Ingen ny extern yta, ingen PII, ingen
auth-/scoping-regression, ingen injection-yta. Verifierat mot kod på disk,
inte mot premiss. Två icke-blockerande noteringar (1 Minor, 1 Minor) lämnas
till CC för in-block-bedömning per §9.6 — ingen av dem håller upp commit.

---

## 1. Resource consumption / DoS — BEKRÄFTAR CTO (ingen DoS-yta), MED korrigering

**Verdict: ingen DoS-yta. Men CTO:s "EN port-anropning (distinct)"-argument
matchar INTE den faktiska implementationen — det spelar dock ingen roll för
slutsatsen.**

Faktum på disk (`ListSavedSearchesQueryHandler.cs:50-55`): resolven sker
**per sparad sökning, ×2** (Ssyk- + Region-anrop i en `foreach`-loop), inte
i ett distinct-samlat portanrop. Klass-doc-kommentaren rad 16
("Alla concept-id över hela listan resolveras i EN port-anropning (distinct)
— ej per-sökning") **motsäger koden direkt**. Detta är en
dokumentations-/kommentar-defekt, ingen säkerhetsdefekt — se Minor 1.

Varför det ändå inte är en DoS-yta (oberoende verifierat, inte övertaget från CTO):

- `ITaxonomyReadModel.ResolveLabelsAsync` (`TaxonomyReadModel.cs:39-52`) =
  `GetStateAsync` (varm väg: `Volatile.Read` + `IsCompletedSuccessfully`-
  check, noll IO) följt av en `foreach` med `Dictionary.TryGetValue`. Ingen
  DB-touch, ingen HTTP-hop, ingen allokering utöver resultatlistan på den
  varma vägen. CTO:s O(1)-in-process-karaktärisering stämmer mot koden.
- Arbets-tak per request är hårt bundet av domän-invarianter, inte av
  ITaxonomyReadModel (porten har ingen cap — korrekt observerat av CTO):
  - Per sparad sökning: `SearchCriteria.MaxConceptIds = 10`
    (`SearchCriteria.cs:39`, enforceras `:68-76`) → ≤10 Ssyk + ≤10 Region
    = ≤20 `TryGetValue` per sökning.
  - Varje concept-id: regex `^[A-Za-z0-9_-]{1,32}$`
    (`SearchCriteria.cs:31-32`) → bounded nyckel-storlek, ingen
    hash-DoS-/ReDoS-yta (linjär `RegexOptions.Compiled`-charklass-match).
  - Multiplikatorn N (antal sparade sökningar per JobSeeker):
    `ListSavedSearchesQuery` är JobSeeker-scopad, oägd av angripare för
    andras konton, och **icke-paginerad med dokumenterat låg domänvolym**.
- Hela operationen sker inom **en redan auth-gated, redan rate-limitad**
  `/saved-searches`-list-request (`ListReadPolicy`). Beslut D:s cap
  (`MaxConceptIdsPerCall=20` på `ResolveTaxonomyLabelsQueryValidator`)
  gäller HTTP-endpointen `GET /taxonomy/labels` — Approach A anropar
  aldrig den endpointen. **CTO:s kärnargument (Beslut D-cap gäller
  endpointen, ej porten/denna handler) håller mot koden. Bekräftas.**

**Notering — naturlig gräns på antal sparade sökningar per JobSeeker
saknas (Minor, ej Blocker):** `CreateSavedSearchCommandHandler.cs` har
ingen per-JobSeeker count-cap (ingen `Count()`-check innan `Add`). I teorin
kan en autentiserad JobSeeker skapa godtyckligt många sparade sökningar →
N obegränsad → ListSavedSearches gör N×≤20 dictionary-lookups + N rader
materialiseras `AsNoTracking` i minne per list-request. Detta är **inte en
ny yta som denna commit introducerar** — den obegränsade
SavedSearch-tillväxten och den icke-paginerade list-queryn fanns före
04b679e. Commit 04b679e adderar endast en in-memory O(1)-faktor ovanpå en
redan obegränsad N. Riskklass: en self-inflicted/single-tenant
resource-amplifier bakom auth, ingen cross-tenant- eller oautentiserad
vektor, ingen IO per element. Inte en GDPR- eller säkerhets-Blocker.
Loggas som observation; pagineringen/count-cap är en `ListSavedSearches`-
designfråga (samma slutsats som CTO trade-off-punkt 3), inte ett krav på
denna commit. CC: ingen åtgärd krävs i denna batch (saknad
funktion-dependency / framtida fas per §9.6 — paginerings-/cap-domän
finns inte).

## 2. JobSeeker-scoping / cross-tenant — BEKRÄFTAR (invariant obruten)

`ListSavedSearchesQueryHandler.cs:26-42`: scoping-kedjan är intakt och
oförändrad av denna commit:
`currentUser.UserId` (null → `[]`) → `JobSeekers.Where(UserId == ...)`
→ `jobSeekerId` (default → `[]`) → `SavedSearches.Where(JobSeekerId ==
jobSeekerId)`. Namn-berikningen opererar **enbart** på `s.Criteria.Ssyk/
Region` från redan JobSeeker-filtrerade rader. Taxonomi-cachen är global,
identisk för alla användare, oföränderlig publik referensdata — den kan
per konstruktion inte bära cross-tenant-data. Ingen läcka.

`GetSavedSearchQueryHandler.cs:33-50`: detalj-vägen behåller dubbel-
predikatet (`Id == savedSearchId && JobSeekerId == jobSeekerId`) plus
ADR 0031 failed-access-detection (`LogCrossUserAttempt` vid existerande-
men-ej-ägd id). Labels returneras tomma (`:71-72`) — ingen
ITaxonomyReadModel-injektion här, scoping orörd. Korrekt och konsekvent.

## 3. PII / injection / log-injection — BEKRÄFTAR (ingen yta)

- **PII:** taxonomi-labels = publik referensdata (länsnamn, yrkesnamn).
  Ingen persondata. `TaxonomyLabelDto = (conceptId, label)` bär enbart
  redan-existerande concept-id (från användarens egen sparade sökning)
  + publikt namn. Ingen ny PII-kategori, ingen ny PII-persistens
  (`SavedSearchDto` är read-DTO ut ur Application-gränsen; `SearchCriteria`
  VO / `saved_searches.criteria` jsonb orört — ADR 0039-kontrakt
  bekräftat intakt).
- **XSS:** reverse-lookup-fallback `$"Okänd kod ({id})"`
  (`TaxonomyReadModel.cs:48`) reflekterar concept-id. Concept-id är
  regex-begränsat `^[A-Za-z0-9_-]{1,32}$` vid `SearchCriteria.Create`
  → teckenmängden innehåller inga HTML-/JS-metatecken (`<>"'&/`).
  Även utan den begränsningen renderar frontend
  (`saved-search-list.tsx:24-39`) labels som JSX-textnoder
  (`parts.join(" · ")` → `{criteriaSummary(savedSearch)}` på `:50`),
  inte `dangerouslySetInnerHTML`, inget `eval`/`new Function`.
  Dubbelt skydd (bounded input + auto-escaped JSX). Design-reviewer
  verifierade frånvaron av `dangerouslySetInnerHTML` — bekräftas
  oberoende här. Ingen XSS-yta.
- **Log-injection:** ingen `ILogger`/log-anrop i
  `ListSavedSearchesQueryHandler` eller `TaxonomyReadModel`
  (grep verifierad). Concept-id/labels loggas ingenstans i
  handler-vägen. `GetSavedSearch` använder `IFailedAccessLogger`
  som loggar `(entityType, savedSearchId, userId, operation)` — inga
  concept-id, inga labels, ingen användar-styrd fritext. Ingen
  log-injection-yta. CLAUDE.md §5.1 (ingen känslig data i klartext)
  respekterad — labels är ej känsliga och loggas ändå inte.

## 4. Clean Arch / GDPR — BEKRÄFTAR

- Ingen EF-entity över Application-gränsen: handlern projicerar till
  `SavedSearchDto` (record), `ITaxonomyReadModel` är Application-ägd port
  (`JobAds/Abstractions/`), impl i Infrastructure. Dependency rule
  respekterad (CLAUDE.md §2.1) — samma mönster som handlern redan har
  mot `IAppDbContext`. Ingen ny lager-yta.
- Ingen ny PII-persistens, ingen migration, `saved_searches`/VO/jsonb
  orört (ADR 0039 Beslut B.1 — bekräftat på disk: `s.Criteria.Ssyk/
  Region` läses som concept-id, VO oförändrat; `SavedSearchDto`-fälten
  är additiva projektioner). Ingen `DeletedAt`/retention/audit-fråga
  aktiveras (ingen ny PII-kategori).
- Inga secrets, ingen auth-yta, ingen BYOK-yta, ingen cross-region-yta
  (ingen extern hop introducerad — porten är in-process).
- Konsentyta oförändrad (ingen AI, ingen ny sub-processor, ingen ny
  extern dataström).

---

## Sammanfattning

| Område | Verdict |
|---|---|
| DoS / resource consumption | Ingen DoS-yta. CTO:s slutsats bekräftad; "distinct EN-anropning"-argumentet matchar dock ej koden (Minor 1) — irrelevant för slutsatsen |
| JobSeeker-scoping / cross-tenant | Invariant obruten, bekräftad |
| PII / XSS / log-injection | Ingen yta, bekräftad (dubbelt XSS-skydd) |
| Clean Arch / GDPR | Inget kontraktsbrott, bekräftad |

**0 Critical, 0 High, 0 GDPR-Blocker. GO.** VETO ej utnyttjad.

### Minor (CC §9.6 — in-block-bedömning, håller EJ upp commit)

1. **Felaktig/missvisande klass-doc-kommentar**
   `ListSavedSearchesQueryHandler.cs:15-16`: "Alla concept-id över hela
   listan resolveras i EN port-anropning (distinct) — ej per-sökning."
   Koden (`:50-55`) gör motsatsen: per-sökning ×2 anrop i en loop.
   Säkerhetsmässigt irrelevant (in-process O(1), tak bundet av
   domän-invarianter oavsett aggregering), men kommentaren ljuger om
   implementationen → bryter Mastercard-test (CLAUDE.md §1) och kan
   vilseleda framtida granskning av just DoS-frågan. Rekommendation:
   rätta kommentaren till att beskriva faktisk per-sökning-loop
   (alternativt — om man vill — refaktorera till en distinct-samlad
   resolve så kod matchar kommentaren; rent städ, ej säkerhetskrav).
   In-block-fix, samma fas/tema. Delegeras till CC/dotnet-architect.

2. **Saknad per-JobSeeker count-cap på SavedSearch (observation, ej
   denna commits scope)** — se Område 1. Single-tenant, bakom auth,
   ingen IO per element, redan existerande N-tillväxt. Paginering/
   count-cap tillhör `ListSavedSearches`-design (saknad
   funktion-dependency per §9.6) — ingen åtgärd i denna batch.
   Loggas som känd observation för framtida fas.

### Praise

- Dubbelt XSS-skydd: bounded concept-id-regex + auto-escaped JSX-render.
- Graceful degradation i porten (`"Okänd kod (<id>)"`, aldrig throw)
  förhindrar att stale taxonomi-drift blir en felväg/info-läcka.
- JobSeeker-scoping + ADR 0031 failed-access-detection bevarad exakt på
  detalj-vägen trots tom-label-scopningen.
- Inga log-anrop i den nya vägen — ingen risk att concept-id/labels
  hamnar i loggar (CLAUDE.md §5.1 respekterad i förebyggande led).
- ADR 0039 jsonb/VO-kontrakt verifierbart orört (additiv read-DTO).
