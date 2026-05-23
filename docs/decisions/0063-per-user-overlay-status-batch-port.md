# ADR 0063 — Per-user overlay-status på publika list-DTOs: dedikerad batch-port, ej DTO-vidgning

**Datum:** 2026-05-23
**Status:** Accepted (Klas-GO 2026-05-23)
**Kontext:** F6 P5 Punkt 2 PR5 — UX-fix efter Klas visual-verify av v0.2.58-dev. `/jobb`-listan (20 kort/page) saknar `Sparad`- och `Har ansökt`-statusindikering, vilket tvingar användaren att öppna varje modal för att se status. Detta är **första gången** ett per-user-overlay-behov möter en publik anonym list-DTO (`JobAdDto`) i kodbasen — mönstervalet blir därför en arkitekturprecedens, inte en lokal fix.
**Beslutsfattare:** senior-cto-advisor (agentId `abf2d7322f2bb6ee1` — multi-approach-triage 2026-05-23, Variant B vald över A/C); Klas Olsson (pending GO för Proposed→Accepted-flip post-utkast); Claude Code (ADR-leverans denna session, explicit Klas-GO för auto-mode-PR5-utkast per memory `feedback_klas_can_override_adr_verbatim_source` — substansen grundad i CTO-dom + ADR 0048 Beslut b-extrapolering).
**Relaterad:** ADR 0042 (sök-yta IA — `JobAdDto`-shape som publik list-projektion), ADR 0043 (taxonomi-ACL — port-precedens för cross-context-läsning), ADR 0045 (perf-budget — hot-path-latens, list-querien public-cacheable), ADR 0048 Beslut (b) (in-handler-join vs read-model-port — extern/intern-regeln utvidgas hit till axeln publik↔privat domän), ADR 0053 (modal-yta använder dedikerade single-anrop — paritetsmönsterprecedent från PR1–4), ADR 0062 (sök-komposition SPOT bakom `IJobAdSearchQuery`-porten). Relaterade: CLAUDE.md §2.3 (CQRS — read-DTO ut, inget Domain-objekt över gränsen), §3.6 (`.AsNoTracking()` default + projektion), §3.3 (DTO = `record class`).

---

## Kontext

F6 P5 Punkt 2 PR5 efter Klas visual-verify av v0.2.58-dev. På `/jobb`-listan (20 kort/page) vill användaren se direkt vilka annonser hen redan sparat och vilka hen ansökt på, utan att öppna varje modal. Två separata per-user-overlay-domäner behöver projiceras ovanpå den publika annons-listan:

- **Sparade jobb** (`SavedJobAds`-aggregat per F6 P5 Punkt 2 Del A, ADR 0024-cascade)
- **Ansökningar** (`Applications` med `JobAdId`-koppling per ADR 0053-amendment 2026-05-23, `CreateApplicationFromJobAdCommand`)

Detta är **första gången** ett per-user-overlay-behov möter en publik anonym list-DTO (`JobAdDto`) i kodbasen. `JobAdDto` är **publik list-projektion** — den används av:

1. `/jobb`-sökyta (anonyma + inloggade)
2. Framtida OG-cards / sociala metadata (per ADR 0042 IA)
3. Framtida SSR-cache av sökresultat (ADR 0045 Beslut 1 hot-path-budget — public-cacheable)

Per-user-status är en **separat read-modell** med annan livslängd, annan cache-semantik och annan accessrättighet (auth-gated).

Krafter som spelar in:

- **CQRS-segregering (Martin 2017 kap. 23; Vernon 2013 kap. 4):** publika list-projektioner och per-user read-modeller är två separata read-vägar. Att blanda dem ger SPOT-brott (Hunt/Thomas 1999) mot ADR 0062 sök-komposition (där `IJobAdSearchQuery` är single source för list-projektionen).
- **Cache-pollution (Fowler 2002 kap. 16):** `GET /api/v1/job-ads` är hot-path enligt ADR 0045 Beslut 1. Att vidga `JobAdDto` med per-user-fält gör endpointen non-cacheable per-user — en regression mot perf-budgeten även innan SSR-cache är implementerad.
- **ADR 0048 Beslut (b) avgränsning:** "extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join". Per-user-overlay på publik annons-projektion är **anti-corruption mellan publik och privat domän** — en kontext-korsning i annan dimension än bounded-context (publik↔privat istället för bounded-context-gräns). Det är inte en enkel intern 1:0..1-länk. Mönstret är analogt med ADR 0043 taxonomi-ACL-read-model-port (cross-context-läsning → dedikerad port).
- **HTTP-idiom POST-på-läs är inte nytt i kodbasen:** `ResolveTaxonomyLabelsQuery` batch-resolver (ADR 0043-implementation) använder redan POST för batch-lookup med stor request-body. Inte ett nytt idiom-brott.
- **Per-row endpoint (Variant A) bryter ADR 0045 perf-budget:** 40 round-trips per page (20 kort × 2 status-typer) är hot-path-regression. Avvisad direkt.
- **Modal-yta har redan paritetsmönster från PR1–4** (ADR 0053-amendment 2026-05-23): single-anrop per ID (`isJobAdSaved(id)`, `GET /me/applications/has-applied/{id}`). List-yta behöver eget batch-mönster — single-anrop-paritet bryts inte, list-yta får sin egen klass av port.

## Beslut

> Beslut fattat av senior-cto-advisor (agentId `abf2d7322f2bb6ee1` 2026-05-23 multi-approach-triage). Status **Proposed** pending Klas-GO för Accepted-flip.

### Beslut (a) — Dedikerad batch-port är det godkända mönstret för per-user-overlay på publika list-DTOs

Per-user-status (Sparad / Har ansökt) projiceras ovanpå publika list-DTOs via **dedikerad batch-port**, inte via `JobAdDto`-vidgning. Den publika list-projektionen (`JobAdDto` / `IJobAdSearchQuery` per ADR 0062) förblir **publik och anonym-renderbar** — per-user-overlayet hämtas i ett separat anrop som frontend komponerar in vid render.

Endpoint: **`POST /api/v1/me/job-ad-status`** (anonym-tolerant per §Kontext — handler returnerar tom DTO för anonym; modal-single-endpoint `GET /me/applications/has-applied/{id}` är dock auth-gated). Rate-limit per anonym IP + per user lyfts som TD-87 (fas-konsistent batch med Saved/Recent-endpoints). *Förtydligande från redaktionell konsistens-fix 2026-05-23 — CTO-dom (agentId a5b8f9db1079a1a12) Minor 9 Variant A: tidigare "auth-gated" motsade §Kontext-intent "no 401-friktion på publik söksida".*

**Request:**

```json
{
  "jobAdIds": ["uuid", "uuid", ...]
}
```

Max 100 IDs per batch — verkställs av FluentValidation. 20 är dagens list-storlek (ADR 0042 IA); 100 ger headroom för framtida paginerings-storlek utan att öppna för obegränsad fan-out.

**Response:**

```json
{
  "savedIds": ["uuid", ...],
  "appliedIds": ["uuid", ...]
}
```

Distinct IDs (en applikation per JobAd även om flera Application-rader finns).

**Backend-mekanik:** `GetJobAdStatusBatchQuery` → handler kör **två separata queries** på `IAppDbContext`, båda med `.AsNoTracking()` (CLAUDE.md §3.6):

- `db.SavedJobAds.Where(s => s.JobSeekerId == seekerId && jobAdIds.Contains(s.JobAdId)).Select(s => s.JobAdId.Value)`
- `db.Applications.Where(a => a.JobSeekerId == seekerId && a.JobAdId != null && jobAdIds.Contains(a.JobAdId.Value)).Select(a => a.JobAdId.Value.Value)`

Två separata queries (inte en union/join) håller varje read-väg ren mot sitt aggregat och låter EF Core query-filter-invarianten (soft-delete-semantik) verka utan special-case-fall — analogt med ADR 0048 Beslut (c).

### Beslut (b) — `JobAdDto` får INTE vidgas med per-user-fält

`JobAdDto` förblir publik anonym list-projektion. Att lägga till `IsSaved` / `HasApplied` på `JobAdDto` är **förbjudet** av tre skäl:

1. **CQRS-segregering (Martin 2017 kap. 23):** publik list-projektion och per-user read-modell är två separata read-vägar med olika livslängder, accessrättigheter och cache-semantik.
2. **Cache-pollution (Fowler 2002 kap. 16):** vidgning gör `GET /api/v1/job-ads` non-cacheable per-user. Hot-path enligt ADR 0045 Beslut 1. Per-user-state får inte bo i public-cacheable list-projektion.
3. **SPOT-brott mot ADR 0062:** `IJobAdSearchQuery` är single source för list-projektionen. Per-user-fält där tvingar in privat domän i publik sök-komposition.

Framtida liknande overlay-behov (t.ex. "har sett denna annons" / "match-score per annons" Fas 4 / "anteckning per annons") följer **samma mönster** — separat batch-port, inte `JobAdDto`-vidgning.

### Beslut (c) — List-yta och modal-yta använder olika anropsmönster (paritet inom yta-klass, inte mellan)

- **List-yta** (20+ rader på `/jobb`, framtida `/sparade`): **batch-port** per Beslut (a).
- **Modal-yta** (detaljmodal per ADR 0053): **dedikerade single-anrop** (`isJobAdSaved(id)`, `GET /me/applications/has-applied/{id}`) — paritetsmönster precedent från F6 P5 Punkt 2 PR1–4.

Single-anrop-paritet inom modal-yta bryts inte av denna ADR — modal-yta är fortsatt single-anrop. Batch-port är en separat klass för list-ytor där fan-out per row vore en perf-regression.

### Beslut (d) — Avgränsning mot ADR 0048: utvidgning till axeln publik↔privat domän (komplementär, EJ supersession)

ADR 0048 Beslut (b) säger: *"extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join"*.

ADR 0063 fastställer att **"context-korsning" inkluderar publik↔privat domän** — inte bara bounded-context-gräns (ADR 0043) eller provider-assembly-axel (ADR 0062 Beslut 4). När en read-modell projicerar per-user-state ovanpå en publik anonym DTO är det en kontext-korsning i den dimensionen, och port-mönstret gäller även om båda aggregaten råkar dela `IAppDbContext`.

ADR 0063 **superseder inte** ADR 0048 — det är en **komplementär avgränsning** som lägger ett tredje exempel på "context-korsning" till de två som redan finns (bounded-context i ADR 0043, provider-assembly i ADR 0062 Beslut 4). Beslutsregeln framåt: in-handler-join gäller fortsatt för **enkla samma-DbContext 1:0..1-aggregatlänkar utan publik/privat-spänning** (ADR 0048-scope orört); port-mönstret gäller när någon av dessa axlar korsas:

1. Bounded-context-gräns med anti-corruption (ADR 0043)
2. Provider-assembly-axel (ADR 0062 Beslut 4)
3. Publik↔privat domän över public-cacheable list-projektion (denna ADR)

## Alternativ som övervägdes

### Variant A — Per-row endpoint (40 round-trips/page) (AVVISAT)
**För:**
- Trivial implementation, ingen batch-validering.
**Emot:**
- 40 HTTP round-trips per page (20 kort × 2 status-typer) bryter ADR 0045 Beslut 1 hot-path-latens-budget direkt. Skala-regression som inte kan motiveras mot perf-budget-vakten.
- Skalar inte med paginerings-storlek.

### Variant B — Batch-endpoint `POST /api/v1/me/job-ad-status` (VALT)
**För:**
- Ett anrop per page-render. Inom ADR 0045 hot-path-budget.
- `JobAdDto` förblir public-cacheable — ingen cache-pollution.
- Följer ADR 0048 Beslut (b)-regeln utvidgad till publik↔privat-axeln.
- HTTP-idiom (POST-på-läs för batch-lookup) är redan etablerat i kodbasen via `ResolveTaxonomyLabelsQuery` — inte ett nytt idiom-brott.
- Mönster-precedens för framtida per-user-overlay-behov (Fas 4 match-score per annons, "har sett", anteckning per annons).
**Emot:**
- Extra rund-trip vid page-render (men inom perf-budget).
- POST-på-läs är HTTP-mässigt mindre cacheable än GET — irrelevant här, response är per-user och ska inte cachas mellan användare.

### Variant C — Eager-fetch via `JobAdDto`-vidgning (`IsSaved` / `HasApplied`-fält) (AVVISAT)
**För:**
- Noll extra round-trips. Ren UI-rendering.
**Emot:**
- **CQRS-brott (Martin 2017 kap. 23):** blandar publik list-projektion och per-user read-modell.
- **Cache-pollution (Fowler 2002 kap. 16):** `GET /api/v1/job-ads` blir non-cacheable per-user, regression mot ADR 0045 Beslut 1 hot-path-budget även innan SSR-cache är implementerad.
- **SPOT-brott mot ADR 0062:** tvingar in per-user-state i `IJobAdSearchQuery`, sök-kompositionens single source.
- **OG-cards / framtida SSR:** vidgad DTO med per-user-fält fungerar inte för anonyma renderingar — kräver alltid auth-context.
- **Skapar dålig precedens:** "lägg till per-user-fält på publik DTO" som mönster återkommer för varje framtida overlay-behov (match-score, anteckning, har sett) — vi får en `JobAdDto` med 10 per-user-fält över tid.

## Konsekvenser

### Positiva
- `JobAdDto` förblir publik anonym list-projektion — bevarar OG-cards, SSR-cache och anonym-render-användning utan ompackning.
- ADR 0045 hot-path-budget respekteras — list-querien public-cacheable, per-user-overlay separat anrop.
- ADR 0062 sök-komposition SPOT bevaras — `IJobAdSearchQuery` förblir single source för publik list-projektion utan per-user-läckage.
- Mönster-precedens dokumenterad: framtida per-user-overlay-behov (match-score per annons, "har sett", anteckning) har klar väg framåt — separat batch-port, inte DTO-vidgning.
- ADR 0048 Beslut (b)-regeln utvidgad explicit till publik↔privat-axeln — beslutsregeln framåt är inte godtycklig nästa gång.
- HTTP-idiomet (POST-på-läs för batch-lookup) är redan etablerat — ingen ny idiom-introduktion.

### Negativa
- **Extra rund-trip vid page-render** (en `POST /me/job-ad-status` per page). Mitigering: en enda batch-rund-trip per page är inom ADR 0045 hot-path-budget; per-row-alternativet (Variant A) hade gett 40 rund-trips.
- **Ny endpoint att underhålla.** Mitigering: trivial handler (två `.AsNoTracking()`-queries i samma DbContext), ingen ny infrastruktur. Mönstret återanvänds för framtida overlays.
- **POST-på-läs frångår strikt REST-idiom.** Mitigering: redan etablerat via `ResolveTaxonomyLabelsQuery` — kodbasens praxis, inte avvikelse. GET med stor query-string vore värre för batch-lookup med 100 IDs.

## Implementation

- **Backend (Application-lager):** `GetJobAdStatusBatchQuery` (record class) + `GetJobAdStatusBatchQueryHandler` (CLAUDE.md §3.3, §2.3). FluentValidation-validator för `jobAdIds.Count <= 100` och non-empty. `.AsNoTracking()` på båda queries (CLAUDE.md §3.6). Två separata `Where`-filter på `SavedJobAds` resp. `Applications` (Beslut a). Pipeline-behaviors enligt ADR 0008-ordningen (Logging → Validation → Authorization → UoW).
- **Backend (Api-lager):** `POST /api/v1/me/job-ad-status`-endpoint, anonym-tolerant per §Kontext (ingen `.RequireAuthorization()` — handler returnerar tom DTO för anonym). Modal-single-endpoint `GET /me/applications/has-applied/{id}` är dock `.RequireAuthorization()`-gated (modal-yta är auth-kontext per Beslut c). Rate-limit per anonym IP + per user lyfts som TD-87 (fas-konsistent batch). *Förtydligande från CTO-dom 2026-05-23 (Minor 9 Variant A).*
- **Frontend:** ny TanStack Query mutation/query `fetchJobAdStatusBatch(jobAdIds)`. Komponeras i `/jobb`-list-rendering: efter `JobAdDto[]` hämtas, anropa batch-endpoint med `jobAdIds`, mappa response till `JobAdCard`-rendering (`isSaved`/`hasApplied` boolean per kort). Zod-schema-spegling (ADR 0020).
- **`JobAdDto` orörd** — ingen `IsSaved`/`HasApplied`-vidgning (Beslut b).
- **Modal-yta orörd** — single-anrop-paritet från PR1–4 består (Beslut c, ADR 0053-amendment 2026-05-23).
- **Gates:** code-reviewer + dotnet-architect på handler-implementation (ADR-precedens-respekt: query-filter-disciplin, AsNoTracking, ingen Repository); design-reviewer på `/jobb`-rendering (Area 5 flödesbegriplighet per ADR 0047); visual-verify av Klas på status-tagg-rendering.
- **ADR-index** (`docs/decisions/README.md`) uppdaterat additivt med ADR 0063-raden (docs-keeper-uppgift efter denna ADR-leverans).

## Referenser

- CLAUDE.md §2.3 (CQRS — read-DTO ut, inget Domain-objekt över gränsen), §3.6 (`.AsNoTracking()` default + projektion), §3.3 (DTO = `record class`), §8 punkt 9 (ADR = DoD vid arkitekturbeslut)
- ADR 0042 (sök-yta IA — `JobAdDto`-shape som publik list-projektion)
- ADR 0043 (taxonomi-ACL — port-precedens för cross-context-läsning, anti-corruption-mönster)
- ADR 0045 (perf-budget — Beslut 1 hot-path-latens-budget, list-querien public-cacheable)
- ADR 0048 Beslut (b) (in-handler-join vs read-model-port — extern/intern-regeln; ADR 0063 utvidgar till publik↔privat-axeln komplementärt, **EJ supersession**)
- ADR 0053 (modal-yta single-anrop-paritetsmönster från PR1–4)
- ADR 0062 (sök-komposition SPOT bakom `IJobAdSearchQuery`-porten; ADR 0063 bevarar SPOT)
- Robert C. Martin, *Clean Architecture* (2017) kap. 23 (CQRS — separation av publik och per-user read-modell)
- Vaughn Vernon, *Implementing Domain-Driven Design* (2013) kap. 4 (read-modell-segregering)
- Martin Fowler, *Patterns of Enterprise Application Architecture* (2002) kap. 16 (caching-overväganden för publika list-projektioner)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) DRY/SPOT (Beslut b — `IJobAdSearchQuery` är single source för list-projektionen)
- Nygard, *Documenting Architecture Decisions* (2011) (ADR-värdhet — mönster-precedens för framtida per-user-overlay-behov)
- Beslutsunderlag: senior-cto-advisor agentId `abf2d7322f2bb6ee1` (multi-approach-triage 2026-05-23, Variant B vald över A/C)

---

*ADR-index underhålls av docs-keeper. Detta beslut fastställer dedikerad batch-port som godkänt mönster för per-user-overlay på publika list-DTOs, komplementär avgränsning till ADR 0048 Beslut (b) på axeln publik↔privat domän, EJ supersession. `JobAdDto`-vidgning med per-user-fält är förbjudet.*
