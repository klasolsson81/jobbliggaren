# F2-leverans aggregat-review — code-reviewer + dotnet-architect + security-auditor + db-migration-writer

**Datum:** 2026-05-12
**Scope:** `0fc4b76..e228b7f` (13 commits, P7 + P8a + P8a.5-serien)
**Status:** Tag-push pausad tills triage klar.

---

## Verdict-sammanfattning per reviewer

| Reviewer | Blocker? | Major | Minor | Info |
|---|---|---|---|---|
| **code-reviewer** | Nej | 0 (M6 gränsfall) | 9 | 7 |
| **dotnet-architect** | Nej | 2 | 3 | 4 |
| **security-auditor** | Nej | 2 | 3 | 4 |
| **db-migration-writer** | Nej | 0 | 2 | flera |

**Kvalitetsbedömning från code-reviewer:** *"Detta är ett av de starkaste leverans-batterierna jag granskat i repo:t. Mastercard-snitt. ADR-trail + test-coverage + DRY-disciplin + DDD-renhet."*

---

## Konsoliderad fynd-lista — Major/högprioriterade

| ID | Källa | Fynd | Föreslagen åtgärd |
|---|---|---|---|
| **A1** | code-reviewer M6 + dotnet-architect | POST `/api/v1/job-ads` saknar `.RequireAuthorization()` — regression mot ApplicationsEndpoints/ResumesEndpoints. ADR 0005 säger "JobAd-listning/sökning är auth-gated i Fas 2-start". | **In-block-fix** (lägg till `.RequireAuthorization()` på POST + GET-endpoints) |
| **A2** | dotnet-architect | `deploy-dev.yml` auto-triggar bara `schema`-mode, inte `bootstrap`. Samma class of failure som amendment 2026-05-12 designades att eliminera (F2-P0b-glömskan-mönstret). | **CTO-invocation** (multi-approach: in-block-fix vs TD-72) |
| **S1** | security-auditor Sec-Major-1 | `raw_payload jsonb` är PII-yta från JobTech utan retention/stripping. GDPR Art. 5/17/30-implikationer. JobTech ej i processing-register. | **Ny TD** (Fas 2 Major, P8b-gating) + ADR 0032 §8-amendment |
| **S2** | security-auditor Sec-Major-2 | `RunBootstrapAsync` Step 2 (EF MigrateAsync, kan ta 30-120s) saknar mid-flow re-fetch av master-creds. Asymmetri vs init Phase A/C (Sec-Major-1 där). | **In-block-fix** (~5 rader, kopiera mönster från Phase C) |

---

## Konsoliderad fynd-lista — Minor (in-block-fix)

| ID | Källa | Fynd | Åtgärd |
|---|---|---|---|
| **C3** | code-reviewer M3 | `ListJobAdsQueryHandler.cs:54` sort default-case `_ =>` döljer fall-through. Framtida enum-extension utan kompilator-varning. | Byt `_ =>` mot explicit `JobAdSortBy.PublishedAtDesc =>` + ta bort default. CS8509-varning vid framtida enum-extension. |
| **C7** | code-reviewer M7 | `DesignTimeDbContextFactory.cs:14-15` har `Password=jobbpilot` → gitleaks-false-positive | Byt `Password=jobbpilot` → `Password=local`. Funktionellt samma, gitleaks slipper varna. |
| **C8** | code-reviewer M8 | `Roles` + `RdsMasterSecret`-records inline i `Migrate/Program.cs:552-554` → fil 564 rader | Extrahera till `Migrate/Roles.cs` + `Migrate/RdsMasterSecret.cs`. Renare Program.cs. |
| **DB1** | db-migration-writer | TD-13 (PII-encryption) saknar `raw_payload` i berörda-kolumner-listan. Cross-ref från `JobAdConfiguration.cs:25` saknas. | Uppdatera TD-13 + lägg kommentar i JobAdConfiguration. **Inte ny TD** (utöka existerande). |
| **Sm1** | security-auditor Sec-Minor-1 | `EcsReadOurCluster`-statement saknar `ArnEquals ecs:cluster`-condition. Asymmetri vs `EcsRunMigrateTaskInDevCluster` som har den. Defense-in-depth. | Lägg till samma condition. Trivial 4-raders patch. Terraform apply behövs. |

---

## Konsoliderad fynd-lista — Minor (nya TDs / framtida fas)

| ID | Källa | Fynd | Föreslagen TD |
|---|---|---|---|
| **Sm2** | security-auditor Sec-Minor-2 | `GRANT ALL ON ALL TABLES` på identity + public. TRUNCATE/TRIGGER-yta onödig för runtime. Phase C hangfire.* använder strikt `SELECT, INSERT, UPDATE, DELETE` korrekt. | **Ny TD (Fas 2 Minor):** Strikta DML-GRANTs istället för GRANT ALL. Trigger: nästa Phase A-touch. |
| **(M1)** | code-reviewer | Hard-coded `AWS_ACCOUNT_ID` i deploy-dev.yml | **TD (framtida fas):** flytta till repo-`vars` vid första staging-workflow-PR |

---

## Konsoliderad fynd-lista — Info/skippa

Sammanfattning av icke-actionable fynd som är polish eller framtida-fas:
- code-reviewer M1/M2/M4/M5/M9/M10 — polish/optional
- dotnet-architect M3 (JobAdSortBy kvadratisk växt) — vid 6+ enum-värden
- dotnet-architect M4 (UpdateFromSource utan per-ad event) — medvetet per ADR 0032
- dotnet-architect M5 (ValidateCore subtilitet) — XML-doc-kommentar OK
- security-auditor Sec-Minor-3 (RunTask command-override) — AWS-IAM-begränsning, dokumentera inför prod
- security-auditor Sec-Info-1..4 — observationer
- db-migration-writer punkt 9 (AddInvitationsAndWaitlist `#pragma`) — opportunistic vid framtida migration-touch

---

## Praise (citat från reviewers)

**code-reviewer:** *"ADR 0033 + 0034 är exemplariska. Mastercard-snitt. Inga 'spagettikod'-fynd, inga shortcuts. Filer av särskild kvalitet att visa läraren: `JobAd.cs` (DDD-factory + invariant-skydd), `ExternalReference.cs` (VO self-validation), `MigrationsOptionsFactory.cs` (DRY single-source-of-truth), `PagedResultContractTests.cs` (fitness-function via reflection)."*

**dotnet-architect:** *"DDD-design för JobAd är på CTO-nivå (Evans 2003-VOs, factories med preconditions, raised events, Result-pattern). Inga Clean Arch-brott i Domain/Application-lager."*

**security-auditor:** *"ADR 0034 etablerar permanent DB-role privilege-separation — runtime-app får aldrig CREATE ON DATABASE. Saltzer/Schroeder 1975 respekterad arkitekturellt. Migrate-loggning använder konsekvent SHA256-truncate-fingerprints — inga klartext-pwds i log-output någonsin verifierat."*

**db-migration-writer:** *"Migrationen är funktionellt korrekt. Inga blocking fynd. Dubbel-lagring `JobAd.Source` + `External.Source` är avsiktlig + korrekt DDD."*

---

## Aggregerad triage-plan

### Steg 1: Klas-STOPP (just nu) — beslut om A2

**A2** (bootstrap auto-trigga i deploy-dev.yml) kräver CTO-invocation per dotnet-architect-rekommendation. Beslut: in-block-fix vs TD-72.

### Steg 2: In-block-fixes (samma session, separat commit per scope)

1. **`fix(api): F2-P7 review-fynd — auth-gate + sort-default`** (A1 + C3)
2. **`fix(migrate): F2-P8a.5e Bootstrap re-fetch + extract types`** (S2 + C8 + C7)
3. **`fix(infra): EcsReadOurCluster cluster-condition`** (Sm1) + terraform apply
4. **`docs(tech-debt): TD-13 utökas med raw_payload + TD-72/73 nya`** (DB1 + S1 + Sm2)

### Steg 3: ADR 0032 §8-amendment (i samma docs-commit som TD-S1)

### Steg 4: Tag-push v0.2.0-dev efter triage komplett

---

## Disciplin-not

Säkerhets-audit **borde** ha triggats inline vid IAM-policy-utvidgning (`ff136ad`) och nya jsonb-PII-kolumn (`c5aa089`). Per CLAUDE.md §9.2 ska security-auditor invokeras "vid PII/auth/secrets/external integrations". Disciplinmiss av CC.

Möjlig hook-utveckling: post-edit på `infra/terraform/modules/*/main.tf`-IAM-ändringar + domain-properties med jsonb-type ska auto-trigga security-auditor-invocation före STOPP-rapport. **TD-kandidat** — invokera adr-keeper för att evaluera hook-tillägg i `.claude/hooks/` (out-of-scope för F2-P8a.5).
