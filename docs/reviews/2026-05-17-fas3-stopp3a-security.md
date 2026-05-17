# Security-audit: FAS 3 /ansokningar backend-vertikal (ManualPosting + cross-aggregat-read-join)

**Status:** GO
**Granskad:** 2026-05-18
**Auktoritet:** GDPR Art. 5, 6, 17, 32 + CLAUDE.md §5.4 + ADR 0031 + ADR 0032 §8 + ADR 0048 + TD-80 (OWASP A01)
**Typ:** BLOCKING security-audit (plan §1.4). Veto-rätt — inga MVP-undantag för GDPR.
**Scope:** arbetskopians ej committade diff (J3 atomisk).

## Fynd-räkning

| Kategori | Antal |
|---|---|
| Critical | 0 |
| High | 0 |
| GDPR (blocker) | 0 |
| Medium | 0 |
| Low | 2 |

Inga blockers. Inga GDPR-brott. Ingen veto. **GO.**

---

## Område 7 — TD-80 URL-scheme-whitelist (OWASP A01) — PASS

`src/JobbPilot.Domain/Applications/ManualPosting.cs:45-61`

- `Uri.TryCreate(url, UriKind.Absolute, ...)` + scheme-whitelist exakt `Uri.UriSchemeHttp` / `Uri.UriSchemeHttps`. Allt annat avvisas: `javascript:`, `data:`, `vbscript:`, `file:`, `ftp:`, relativa URI:er och schemelösa strängar faller alla på `UriKind.Absolute`-kravet eller scheme-likhetstesten → `Result.Failure`. Verbatim-paritet med TD-80-whitelisten (JobAd.ValidateCore) bekräftad — samma regel, ingen slarvig divergens.
- Längd-caps: Title 300, Company 200, Url 2000 — domän-validering (rad 34-59) + DB `HasMaxLength` + migration `character varying(n)`. Trippel-lager, ingen oavkortad användar-input persisteras.
- Ingen XSS-yta öppnad: `manual_url` är ren persistens; ingen handler renderar den som HTML. Frontend tar `url: z.string().nullable()` — rendering är ej i scope för denna vertikal. Domän-whitelisten stänger `javascript:`-injektion vid källan.
- `string.IsNullOrWhiteSpace(url)` → Url förblir `null` (giltigt; Url är optional). Tom sträng blir aldrig persisterad.

## Område 1+4 — GDPR PII-bedömning — PASS

**Ny PII-kategori?** ManualPosting = användar-angiven jobbtitel/företag/länk/utgångsdatum. Detta är inte en ny PII-*kategori* — det är samma klass av user-content som `CoverLetter`, redan etablerad på `applications`-tabellen och redan täckt av Application-aggregatets soft-delete + audit-yta. Ingen ny sensitiv kategori (hälsa/politik), ingen DPIA-trigger, ingen ny sub-processor, inget nytt cross-region-flöde. Ingen ny ADR krävs för PII-kategori; ADR 0048 dokumenterar redan mönstret.

**Erasure (Art. 17) — verifierad intakt:**
- `ManualPosting` är EF owned-entity (`ApplicationConfiguration.cs:40-54`) → samma fysiska rad som Application (`manual_title`/`manual_company`/`manual_url`/`manual_expires_at`-kolumner i `applications`). Ingen separat tabell, ingen egen livscykel.
- `Application.SoftDelete` (`Application.cs:164-172`) sätter `DeletedAt`. Owned-kolumnerna ligger på samma rad → soft-deletas atomiskt med parent. Ingen kvarlämnad ManualPosting-rad är möjlig.
- Global query filter `builder.HasQueryFilter(a => a.DeletedAt == null)` (`ApplicationConfiguration.cs:88`) exkluderar hela raden — inklusive ManualPosting-kolumnerna — från alla läsningar efter erasure.
- `DeleteAccountCommandHandler.cs:50-65` cascade: hämtar alla Applications för JobSeeker, anropar `app.SoftDelete(clock)` per styck, atomiskt via UnitOfWorkBehavior. DeleteAccount-cascaden når ManualPosting gratis eftersom den är owned på Application-raden. **Art. 17 erasure obruten.**

**Encryption at rest:** ManualPosting lagras i klartext, identiskt med `CoverLetter` som har `// TODO(GDPR): ... kryptera kolumnen i Fas 2` (`ApplicationConfiguration.cs:31`). Konsistent med etablerad precedens på samma aggregat — se Low-1.

## Område 6 — Logging hygiene (CLAUDE.md §5.4) — PASS

- `Grep` över hela `src` på `ManualPosting|manual_url|manual_title|manual_company`: 14 träffar, alla i handlers/config/migration/DTO/domän — **noll i logg-anrop, noll i External/-klienter**.
- `LoggingBehavior.cs:34-41` loggar enbart `typeof(TMessage).Name` + elapsed ms + exception — aldrig command-payload. `LogFailed` loggar `Exception` men handlers kastar `Result.Failure` (ingen exception med PII-payload) vid valideringsfel.
- Audit-pipeline (`CreateApplicationCommand` `IAuditableCommand`) skriver `EventType`/`AggregateType`/`AggregateId` — ingen ManualPosting-payload i audit_log.
- Klartext-loggning av manual_url/ManualPosting förekommer ingenstans. §5.4-konform.

## ADR 0048 / 0031 / 0032 §8 — cross-user-scoping + soft-delete + failed-access — PASS

**(1) Cross-user-scoping ej försvagad:**
- `GetApplications` (`:18-29`), `GetPipeline` (`:16-32`), `GetApplicationById` (`:20-38`): currentUser → JobSeeker-resolve oförändrad; `Where(a => a.JobSeekerId == jobSeekerId)` (resp. `a.Id == applicationId && a.JobSeekerId == jobSeekerId`) appliceras **före** `GroupJoin`. JobAd-joinen tillför endast jobb-metadata till redan jobSeeker-scopade rader — den vidgar inte radmängden, läcker inte andra användares ansökningar. `GroupJoin/SelectMany/DefaultIfEmpty` är en left-projektion, inte en filter-vidgning.

**(2) Soft-deletad JobAd läcker ej:**
- Alla tre handlers: ingen `IgnoreQueryFilters()`, inget manuellt `DeletedAt`-predikat. `db.JobAds`-sidan ärver global query filter `j => j.DeletedAt == null` (`JobAdConfiguration.cs:82`, verifierad). Soft-deletad JobAd exkluderas före join → `DefaultIfEmpty` ger `j == null` → `JobAdGuid` null + JobAdSummaryDto-fallback. Soft-deletad annons-metadata exponeras EJ. ADR 0048 Beslut (c) efterlevd exakt. Shadow-FK bekräftat rivet (`Grep` på `HasForeignKey.*JobAd|HasOne` i ApplicationConfiguration: noll träffar).
- Fallback-not: när `j == null` men `ManualPosting != null` projiceras manuell metadata — detta är Application-ägd data för samma jobSeeker, inte JobAd-läckage. Korrekt.

**(3) Failed-access-logg (ADR 0031 / TD-67) bevarad:**
- `GetApplicationByIdQueryHandler.cs:80-93`: efter projektion-omskrivning är failed-access-grenen oförändrad — `dto is null` → existens-check `AnyAsync(a => a.Id == applicationId)` → `failedAccessLogger.LogCrossUserAttempt("Application", ..., "GetApplicationById")`. Klient ser identisk `null`/404 oavsett okänt-id vs tillhör-annan. Timing/existens-läckage oförändrat (ADR 0031-accepterad nivå). Regression-skydd intakt.

## Övriga områden

- **Område 2 (secrets):** ej berört — inga secrets/connection strings i diffen.
- **Område 3 (authz):** `CreateApplicationCommand : IAuthenticatedRequest`; handlers gör explicit `currentUser.UserId.HasValue`-guard + JobSeeker-resolve. IDOR ej möjlig — alla queries jobSeeker-scopade. PASS.
- **Område 5 (cross-region):** ej berört — ingen ny extern integration, samma RDS EU.

---

## Low (icke-blockerande, ej veto)

**Low-1 — ManualPosting i klartext (konsistent med CoverLetter-precedens).**
`ApplicationConfiguration.cs:40-54` — ManualPosting-kolumnerna lagras okrypterat, identiskt med `CoverLetter` (`:31`, har redan `// TODO(GDPR): kryptera i Fas 2`). Jobbtitel/företag/länk är lägre känslighet än CV-innehåll/OAuth-token (ej BYOK-klass). Förslag: när CoverLetter-krypteringen tas i Fas 2, inkludera manual_*-kolumnerna i samma åtgärd. Ingen separat TD krävs — fång in under befintlig CoverLetter-TODO. Ej blocker: ingen försämring mot etablerad baslinje, samma aggregat, EU-at-rest via KMS-volym kvarstår.

**Low-2 — manual_expires_at saknar framtidsvalidering.**
`ManualPosting.cs:28-63` validerar inte att `ExpiresAt` ligger i framtiden. Inte en säkerhetsfråga (ingen injektion, ingen PII-exponering) — endast data-kvalitet. Defensiv polish, lämnas till feature-ägarens bedömning. Ej veto.

---

## Sammanfattning

Vertikalen är säkerhetsmässigt och GDPR-mässigt ren. TD-80-whitelisten är verbatim-korrekt och stänger OWASP A01-ytan. Cross-user-scoping orörd, soft-deletad JobAd läcker ej (ADR 0048 c efterlevd utan IgnoreQueryFilters/manuellt predikat), failed-access-loggen (ADR 0031) bevarad efter projektion-omskrivning. ManualPosting bryter inte Art. 17-erasure (owned → soft-deletas atomiskt med Application, omfattas av DeleteAccount-cascade och global query filter). Ingen klartext-loggning av manual_url/ManualPosting (§5.4). Ingen ny PII-kategori, ingen DPIA/sub-processor/cross-region-trigger.

**GO. 0 Critical, 0 High, 0 GDPR-blocker, 0 Medium, 2 Low.** Inga commits gjorda av security-auditor.
