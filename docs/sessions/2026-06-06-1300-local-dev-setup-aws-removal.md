---
session: Lokal dev-uppstart — AWS avvecklat, allt kör på Windows-laptop
datum: 2026-06-06
slug: local-dev-setup-aws-removal
status: PR öppen mot main (feat/local-dev-setup-aws-removal), CI pending
commits:
  - "feat(infra): lokal AES-256-GCM DEK-provider + ta bort AWS SES (ADR 0066)"
  - "fix(worker): ValidateOnBuild=false för lokal Development-boot (ADR 0023-amendment)"
  - "fix(jobsources): normalisera JobTech-datum till UTC vid ACL-boundary"
  - "docs(sessions): ADR 0023/0049-amendments + TD-101/102/103 + session-log"
---

# Lokal dev-uppstart utan AWS (ADR 0066-pivot)

## Mål

AWS permanent avvecklat (ADR 0066). Planen: lokal dev → MVP för beta-testare →
Hetzner (BE) + Vercel (FE). Denna session: full lokal stack (PostgreSQL + Redis
+ API + Worker + FE) körande på Windows-laptop utan molntjänster, med
Platsbanken-import verifierad. Två runtime-AWS-beroenden i Infrastructure måste
ersättas: `AWSSDK.SimpleEmailV2` (SES) + `AWSSDK.KeyManagementService` (KMS
field-encryption, TD-13/ADR 0049).

## Beslut (agent-drivna, inline per CLAUDE.md §9.2)

**CTO Rond 1 (SES, senior-cto-advisor `a543cdfca974e9d19`):** Variant C — behåll
`ConsoleEmailSender` (redan default, loggar till Seq). **Ta bort** `SesEmailSender`
+ `AWSSDK.SimpleEmailV2` (död kod — Hetzner har ingen SES). Ingen spekulativ
SMTP-sender (YAGNI). → TD-101 (transaktionell mejlväg Hetzner, Major).

**CTO Rond 2 (KMS, senior-cto-advisor `a9ec05283affd0b73`):** Variant A — ny
`LocalDataKeyProvider : IDataKeyProvider` som wrappar per-användar-DEK med lokal
AES-256-GCM master-nyckel istället för KMS. Vald via config-switch
`FieldEncryption:Provider` (Kms default / Local), paritet med
`EmailOptions.Provider`. **Behåll** `KmsDataKeyProvider` + paketet som referens.
Nyckel-insikt: `IFieldEncryptor` (KmsEnvelopeEncryptor) är redan ren BCL `AesGcm`
utan AWS — enda AWS-touchpoint var `IDataKeyProvider`. Master-nyckeln i
`appsettings.Local.json` (gitignored). → TD-102 (self-managed-nyckel-prod-modell
Hetzner, Major, ADR 0049-amendment).

**dotnet-architect (`af417c6b80a044d23`):** Låste DI-mekaniken — switcha endast
`IDataKeyProvider` + KMS-klient; `IFieldEncryptor`/options/validator ovillkorliga.
Default `Provider="Kms"` (noll regression — verifierat mot ApiFactory/
WorkerTestFixture last-wins KMS-fake; prod-fail-safe). Wrapped-DEK byte-prefix
`[0x4C,0x01]`; owner-AAD `aggregate=jobseeker;owner={guid:D};purpose=td13-field`.

**security-auditor (`a8bbbf8bedd7c2e6d`) + code-reviewer (`a04d8c82575d775d5`):**
Båda GODKÄNT, 0 Blocker/Major. Verifierade: AEAD-korrekthet, cross-user-AAD-
binding (säkerhetsinvariant), fail-closed på alla vägar, ingen nyckel i logg/
exception, ZeroMemory, secret gitignored. Minor: stale SES-doc-kommentarer
(fixade in-block per §9.6) + Hetzner-fas-ADR-flagga (→ TD-102).

## Tre latenta buggar avtäckta av AWS→lokal-pivoten

Alla tre samma klass: kod som fungerade på Fargate (Production/UTC) men failade
vid första lokala Development-körning. Inga orsakade av crypto/SES-ändringen.

1. **Migration-gap:** lokal DB var migrerad till #23 men kodbasen hade 6 pending
   (inkl `F6P4FtsSearchVector`/`search_vector`). `JobbPilot.Migrate` är helt
   Secrets-Manager-beroende → körs inte lokalt. Löst med
   `dotnet ef database update --connection "<local>"` (29 migrationer applicerade).

2. **Worker bootade inte i Development (CTO `aaba3ba73662b3dcd`, Variant A):**
   `Host.CreateApplicationBuilder` sätter `ValidateOnBuild=true` i Development →
   eager-validerar HELA Application-assemblyns Mediator-handlers, inkl Api-only
   (Auth/Invitation/Waitlist) vars deps Worker medvetet inte registrerar (ADR
   0023 HTTP-fri). Fargate körde Production (`ValidateOnBuild=false`). Löst med
   `ConfigureContainer(DefaultServiceProviderFactory{ValidateScopes=true,
   ValidateOnBuild=false})`. ADR 0023-amendment + TD-103 (Variant C assembly-split).

3. **Platsbanken-import failade på timezone (CTO `a5a9dab35b2c1a213`, A1):**
   JobTech-datum (`PublicationDate`/`LastPublicationDate`, `DateTimeOffset?`)
   deserialiseras av System.Text.Json med LOKAL maskin-offset (+02:00 i Sverige).
   Skrevs orört till `job_ads`-`timestamptz`-kolumner → Npgsql avvisar (kräver
   Offset=0/UTC). Fargate=UTC dolde det. Fix: `.ToUniversalTime()` vid ACL-
   boundary i `PlatsbankenJobSource` (publishedAt/expiresAt/occurredAt). Audit-
   write-failure (System.JobAdsSynced) var en kaskad — försvann med fixen.

## Verifierat sluttillstånd

- `docker compose`: postgres-dev (5435) + redis-dev (6379) + seq (5341) healthy.
- API: bootar `Provider=Local`, `/api/ready` Healthy (Postgres + Redis), inga AWS-
  SDK-anrop vid boot.
- Worker: bootar (Variant A), Hangfire-schema auto-skapat (Development), 10
  recurring jobs registrerade.
- **Platsbanken-import: fetched=61, added=54, updated=0, archived=0, skipped=7,
  errors=0. job_ads=54, published_at lagrade som UTC (+00).** Audit-rad
  `System.JobAdsSynced {Added:54, Errors:0}` skriven.
- `/api/v1/job-ads?pageSize=2` med session → HTTP 200, riktiga annonser.
- Registrering fungerar (DEK skapas lazily, inte vid registrering — korrekt).
- FE: `pnpm dev` → `http://localhost:3000` HTTP 200.
- Tester: Domain 422 + Application 624 (inkl 16 LocalDataKeyProvider + 6 validator
  + 3 JobTech-UTC-regression) + Architecture 78 — alla gröna. Build 0 warnings.

## Operativt / pending

- BUILD.md §3.2 (rad 172 SES, 175 KMS — hela §3.2 AWS-infra) är inaktuell efter
  ADR 0066 men RÖRDES INTE (spec-edit kräver Klas + approve-spec-edit.sh).
  Flaggat i PR-body för framtida BUILD.md-pivot-session.
- `LocalDataKeyProvider` end-to-end-DEK-skapande triggas först vid krypterad
  fält-skrivning (resume e.d.) — unit-verifierat (16 tester), ej e2e denna session.
- Test-writer-advisory: `internal` Infrastructure-interfaces kan inte NSubstitute-
  mockas utan `InternalsVisibleTo("DynamicProxyGenAssembly2")` — löst med hand-
  skrivna fakes (ingen prod-ändring). Csproj-beslut för dotnet-architect om det
  återkommer.

## Nästa session

- Granska + merga PR `feat/local-dev-setup-aws-removal` efter CI grönt.
- Vid behov: utöka end-to-end-verifiering av LocalDataKeyProvider (skapa
  krypterad fält-data → bekräfta DEK-wrap i `user_data_keys`).
- Hetzner-deploy-planering aktualiserar TD-101 (mejl) + TD-102 (master-nyckel-ADR).
