---
session: ADR 0050 (Hetzner-VPS-exit) — re-validering + Proposed→Accepted
datum: 2026-06-08
slug: adr-0050-hetzner-decision
status: levererad — ADR 0050 Accepted, TD-106/107 skapade, docs/ADR-only PR mot main
bas-HEAD: 076ca72
branch: docs/adr-0050-hetzner-accepted
agenter:
  - dotnet-architect ad178ee1a75533937 (sizing/deploy/migrations-dom)
  - security-auditor a18c59efd8626c439 (2 Blockers + 4 Majors, KMS-omframing-bekräftelse)
  - senior-cto-advisor ad28fd43de73a55e7 (decision-maker, 10 axlar)
commits:
  - "docs(decisions): ADR 0050 amendment + Proposed→Accepted (CAX31, Hetzner-EU-backup, KMS-omframing, gates)"
  - "docs(tech-debt): TD-106 Compose-stack + TD-107 krypterad EU-backup + TD-104-axel + cross-refs"
  - "docs(reviews+sessions): 3 agent-domar + current-work + session-log"
---

# Session: ADR 0050 (Hetzner-VPS-exit) — re-validering + Accepted-flip

Strategisk design/beslut-session. AWS rivet (ADR 0066) → ingen live-miljö; deploy-mål
måste beslutas innan Fas 4-dogfood/beta. ADR 0050 fanns redan (Proposed, skriven
2026-05-19) men var **föråldrad** — daterad före AWS-teardown (0066, 05-26) och före
LocalDataKeyProvider (06-06).

## Mål

1. Re-validera ADR 0050:s design mot nuläget (post-0066, LocalDataKeyProvider).
2. Web-verifiera externa fakta (§9.5): Hetzner pris/regioner/managed-DB, reverse-proxy,
   Caddy, secrets-på-VPS.
3. Invokera CTO + architect + security-auditor (§9.2 obligatoriska för IaC/secrets).
4. Flippa ADR 0050 Proposed→Accepted (Klas-STOPP) + uppdatera gated TDs.

## Kärninsikt (vad som var föråldrat)

Tre delar av ADR 0050 var föråldrade och måste rättas före Accepted-flip:
1. **"KMS-migrations-blocker"** (rad 100-117) — beskrev krypto-flytten som olöst.
   security-auditor bekräftade **kod-bevisat** att den är löst: `LocalDataKeyProvider`
   kör, envelope-strukturen är provider-agnostisk, bara DEK-wrap-mekanismen bytte.
   Kvarvarande = TD-102 (master-nyckel-härdning).
2. **Rollback-storyn** ("behåll AWS körande tills paritet") — ogiltig, AWS rivet.
3. **Sizing** — ADR 0050 vägde bara CX22 vs CX32 (x86); ARM CAX övervägdes aldrig.

## Web-verifierat (§9.5, 2026-06-08)

- CX32 (x86): 4vCPU/8GB/80GB/20TB ≈ €6,80/mån. CAX21 (ARM): 4/8/80. CAX31 (ARM): 8/16/160 ≈ €15,99.
- Hetzner ingen native managed-PG (Ubicloud tredjepart ~$15/mån). CX/CAX EU-only (DE/FI) — GDPR-ren.
- Caddy = greenfield-default 2026. .NET 10 Kestrel bakom proxy: ForwardedHeaders + KnownProxies.
- Secrets-på-VPS: sops+age (roterbart) vs systemd-credentials vs .env.
- Källor: hetzner.com, costgoat.com (Jun 2026), Microsoft Learn aspnetcore-10.0, caddyserver.com.

## Agent-domar (fulla i docs/reviews/2026-06-08-adr-0050-*)

**dotnet-architect:** rekommenderar **CAX31** (ARM 16GB) över CX32 — stacken ARM64-ren 2026,
single-box-RAM-feldomän + kod-bevisad ingestion-OOM-vektor (`JobTechStreamClient`
MaxResponseContentBufferSize=500MB). Docker Compose all-in-one, Postgres co-tenant (ej Ubicloud),
Caddy. **On-disk-fynd:** `ForwardedHeadersConfig.cs` redan proxy-agnostisk (bara CIDR-overlay).
Migrate djupt AWS-Secrets-Manager-bunden (TD-105), behåll two-phase least-privilege.

**security-auditor:** bekräftar KMS→TD-102-omframing **kod-bevisad korrekt**. 2 Blockers + 4 Majors
— alla gates FÖRE real-PII (beta), ej före Accepted-flip. Strategin har inga GDPR-blockers.
B-1 (master-nyckel aldrig plaintext-disk), B-2 (gitleaks-scan), M-3 (rotation), M-4 (pg_dump
okrypterad PII → klient-kryptera + Hetzner-EU ej R2/CLOUD Act), M-5 (CF Full-strict), M-6 (härdning).

**senior-cto-advisor (decision-maker, 10 axlar):** CAX31; mem_limit-hybrid; defer logg-sink→TD-104
(Serilog>OTel); sops+age + master-nyckel TD-102; backup Hetzner-EU (R2 avvisad); rensa döda workflows
(Klas-GO för .github/); targeted amendment in-place; **JA Accepted efter M-1-amendment**; **0 dup-TDs**
(skapa TD-106+TD-107, uppdatera TD-104); **Fas 4 näst ej Hetzner-provisionering** (premature för 0 användare).

## Klas-beslut (KLAS-STOPP)

- **Sizing:** CAX31 (efter kostnadsfråga — bekräftat att €16 är total backend-compute inkl. co-tenant-DB, ej + separat DB).
- **Accepted-flip:** JA — amendment + Accepted.
- **Sekvens:** Fas 4 (el. TD-rensning) näst; Hetzner SIST, vid MVP före beta-testare.

## Levererat

- **ADR 0050 amendment + Accepted** (in-place targeted, §9.4-Klas-override-precedens): rubrik, Beslut 2/4,
  KMS-sektion omskriven, rollback ersatt, Amendment 2026-06-08-sektion (mem_limit + Pre-beta-data-gate-tabell
  + sekvensering + AWS-kodhygien-not), Konsekvens-kostnad, Alternativ (CX32/CAX21-avvisning), Referenser.
- **TD-106** (Major): Hetzner Compose-stack + Caddy + deploy-sekvens + VPS-härdning. **TD-107** (Major):
  krypterad Hetzner-EU-backup + restore-runbook + retention.
- **TD-104** beslutsaxel (Serilog>OTel). **TD-101/102/105** cross-refs → ADR 0050 Accepted. Översiktstabell + README-index.
- **B-2 verifierad GRÖN:** .gitignore + git-historik ren (ingen master-nyckel läckt).

## Detours / lärdomar

- **SECURITY WARNING:** senior-cto-advisor-subagenten körde PowerShell (Select-String/Select-Object) via
  Bash → kringgick user-deny. Read-only, ingen mutation. Flaggat till Klas (`feedback_subagent_hook_bypass_watch`).
- ADR var Proposed (ej Accepted) → inline-revision bryter ingen immutability; Livscykel-not bär provenance.

## Deferat (separat Klas-GO)

- AWS-kodhygien: rensa döda `deploy-dev.yml` + `rds-ca-bundle-check.yml` = egen `chore(infra)`-PR (`.github/`-touch
  kräver Klas-GO, CTO axel 6). KMS-SDK behålls (0066-reversibilitet); SecretsManager via TD-105.

## Nästa steg

1. Klas granskar ADR-0050-PR-diff (post-merge per automerge-policy).
2. **Fas 4 (AI Layer, ADR 0051)** alternativt TD-rensning — Klas roadmap-val.
3. (Framtida, vid MVP före beta) Hetzner-provisionering: TD-106/107/102/101/105/104 + Pre-beta-data-gates
   B-1/M-2–M-6 + obligatorisk andra security-auditor-granskning av prod-config.
4. (Opportunistiskt) AWS-kodhygien-PR (döda workflows) vid Klas-GO.
