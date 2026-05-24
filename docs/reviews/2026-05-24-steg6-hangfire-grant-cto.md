# CTO-rond — STEG 6 Hangfire GRANT-incident (post-deploy v0.2.66-dev)

**Datum:** 2026-05-24
**Agent:** senior-cto-advisor (agentId `a9f2e123b1080b00f`)
**Triggrad av:** 500-incident på `POST /api/v1/admin/job-ads/backfill-ssyk` — `Npgsql.PostgresException 42501: permission denied for schema hangfire`. Klas-direktiv: "korrekta vägen, inte snabbaste".

---

## Beslut

**Plan B — splittad `jobbpilot_hangfire`-roll, GRANT:ad till både Api och Worker, ägd av Terraform/RDS-init.**

Avvisar Plan A (runtime-DDL från Worker), Plan C (lateral access), Plan D (throwaway-kod), STOPP C (push till post-demo — onödig).

GO-typ: **Klas-låst** för Terraform-apply-tidpunkt mot demo-fönster, även om beslutet är CTO-entydigt mot principer.

## Motivering mot principer

- **Saltzer & Schroeder 1975 — Least Privilege (princip 4):** `jobbpilot_app` har ingen legitim funktion mot hangfire-schemat. Plan C kollapsar två distinkta security-subjekt — exakt vad least-privilege förbjuder. Security-auditor (agentId `ab002ec9ede71d352`) flaggade detta formellt INNAN incidenten.
- **Martin 2017 Clean Architecture kap. 22 + 24:** Hangfire-storage är bounded mechanism med egen ägar-modell. Plan B bevarar gränsen: `jobbpilot_app` ↔ public/identity, `jobbpilot_hangfire` ↔ hangfire, `jobbpilot_worker` ↔ public/identity.
- **Evans 2003 DDD kap. 14 (Bounded Contexts):** Hangfire **är** separat bounded context (background-job-orkestrering). Dedikerad roll = kontext-översättningsanchor.
- **Ford/Parsons/Kua 2017 (Evolvable Architecture):** Plan A skapar tyst sync-beroende mellan Worker-kod och Terraform-state. Plan B är evolutionary-correct: schema/rolldesign lever där alla andra rolldesign-beslut lever.
- **Persistence Ignorance (Microsoft Learn):** Schema-grants är inte applikationskod. Runtime-DDL från Worker bryter detta.
- **CLAUDE.md §13:** "Aldrig tyst förändring" — Plan A skulle bryta runbook §4 utan att uppdatera det.

## Avvisade alternativ

- **Plan A (runtime GRANT):** Tyst sync-beroende, persistence-ignorance-brott, distansiation från runbook §4. Inte legitim code-driven-bootstrap (har ingen versionerad migration-history).
- **Plan C (jobbpilot_app permanent GRANT):** Bryter security-auditor-varning ordagrant. Saltzer & Schroeder least-privilege-brott. Säkerhets-skuld permanent tills explicit refaktorerad.
- **Plan D (throwaway RecurringJob):** Anti-pattern (CLAUDE.md §5 anda). Försvagar admin-endpoint som arkitekturkomponent. Skapar dead-yta i deployad kod.

## Demo-realitet

Plan B tids-estimat ~45-60 min konservativt:
- Terraform-modul: CREATE ROLE + GRANTs + ALTER DEFAULT PRIVILEGES (~15 min)
- AWS Secrets Manager-secret + IAM-policy (~10 min)
- Task-def-overlay Api + Worker (~10 min)
- Terraform apply + smoke-test (~15 min)
- Runbook §4-uppdatering (~10 min)

Söndag kväll 24 maj → måndag MVP-demo: tidsmässigt rimligt. Backfill-endpoint är admin-endpoint, INTE feature i demo-flödet — om Plan B inte hinner saknas bara admin-backfill-knapp i demon (acceptabelt fallback).

## In-block-fixar (samma commit-batch som STEG 6)

5 steg implementation-plan:
1. Terraform: ny role `jobbpilot_hangfire` + GRANT USAGE/INSERT/SELECT/UPDATE/DELETE på hangfire.* + GRANT USAGE/SELECT på sequences + ALTER DEFAULT PRIVILEGES för framtida tabeller
2. AWS Secrets Manager: `jobbpilot/dev/postgres-hangfire` med connection-string för nya rollen
3. IAM: task-roles (Api + Worker) får policy för att läsa nya secreten
4. Task-defs (Api + Worker): `ConnectionStrings:HangfireStorage` overlay från nya secreten
5. `docs/runbooks/hangfire-schema.md` §4: uppdatera rolltabellen med tredje rad `jobbpilot_hangfire`

Worker behåller två connection-strings (`Postgres` via jobbpilot_worker för app-data, `HangfireStorage` via jobbpilot_hangfire för hangfire-schema).

## Genuina TDs

Ingen ny TD från detta beslut.

## Disciplin-observation

Security-auditor (`ab002ec9ede71d352`) flaggade Api-lateral-access-risk som "operationell not — inte Block här (dev-fas)". I efterhand: detta var **formell varning** som materialiserade sig som runtime-incident. CLAUDE.md §9.2 säger security-auditor invokeras vid "kod som rör PII, auth, secrets eller external integrations" — Hangfire-connection-string är secret + external integration. Resultatet skulle ha vägts som blocking input. Process-gap för framtida discipline-not, ej TD.

## Time-pressure-svar

Klas-direktiv "Stabilitet och kvalitet före quickfix" gjordes I SAMMA SESSION som time-pressure är känd → det är inte abstrakt. Att CC/CTO väger om Klas-direktivet baserat på time-pressure som Klas redan vägt in är paternalism. Om Plan B inte ryms söndag kväll: STOPP, rapportera, låt Klas välja (a) fortsätt måndag morgon, (b) demon utan backfill-knapp, (c) explicit Klas-override till Plan C med TD.

## Klas-STOPP-flagga

**Klas-GO krävs** för Terraform-apply-tidpunkt mot demo-fönster. Beslutet är CTO-entydigt mot principer — Klas väger demo-kontext, inte teknisk approach.

## Referenser

- Saltzer, J. H., & Schroeder, M. D. (1975). *The Protection of Information in Computer Systems*. Proceedings of the IEEE, 63(9). Princip 4 Least Privilege.
- Robert C. Martin, *Clean Architecture* (2017), kap. 22, 24.
- Eric Evans, *Domain-Driven Design* (2003), del IV kap. 14.
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017), kap. 2, 6.
- CLAUDE.md §2.1, §5.4, §9.2, §9.6, §13.
- `docs/runbooks/hangfire-schema.md` §4.
- ADR 0023 Worker-pipeline.
- Security-audit STEG 6 (`ab002ec9ede71d352`).
