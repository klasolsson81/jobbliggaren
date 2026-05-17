# JobbPilot ADR-index

Architecture Decision Records (ADRs) dokumenterar arkitekturella val som påverkar fler än ett lager eller som skulle vara svåra att reversera. De är **immutable** — en beslutsändring skapas som ny ADR som superseder den gamla (se adr-keeper-agenten i `.claude/agents/adr-keeper.md`).

## Status-terminologi

- **Accepted** — aktivt beslut som styr projektet
- **Proposed** — under utvärdering, ej implementerat
- **Superseded** — ersatt av senare ADR (länkas till ersättaren)
- **Deprecated** — inte längre relevant, men historik bevarad

## Registrerade beslut

| # | Titel | Status | Datum | Fil |
|---|-------|--------|-------|-----|
| 0001 | Clean Architecture med DDD | Accepted | 2026-04-18 | [0001-clean-architecture.md](./0001-clean-architecture.md) |
| 0002 | Explicit modell-versioner för agenter | Accepted | 2026-04-18 | [0002-explicit-model-versions.md](./0002-explicit-model-versions.md) |
| 0003 | Design as skills (arkitektur) | Accepted | 2026-04-18 | [0003-design-as-skills.md](./0003-design-as-skills.md) |
| 0004 | GitHub Flow över GitFlow | Superseded by 0019 | 2026-04-18 | [0004-github-flow.md](./0004-github-flow.md) |
| 0005 | Go-to-market + monetarisering (Fas 2-prereq) — Alternativ C + invitations/waitlist | Accepted | 2026-05-12 | [0005-go-to-market-strategy.md](./0005-go-to-market-strategy.md) |
| 0006 | Claude Code hooks — kända begränsningar | Accepted | 2026-04-18 | [0006-claude-code-hooks-known-limitations.md](./0006-claude-code-hooks-known-limitations.md) |
| 0007 | Branch protection för main (Fas 0) | Accepted | 2026-04-18 | [0007-branch-protection-fas0.md](./0007-branch-protection-fas0.md) |
| 0008 | Pipeline behavior order (Logging→Validation→Auth→UoW) | Accepted | 2026-04-19 | [0008-pipeline-behavior-order.md](./0008-pipeline-behavior-order.md) |
| 0009 | Inga Repositories; direkt IAppDbContext + IUnitOfWork | Accepted | 2026-04-19 | [0009-no-repository-pattern.md](./0009-no-repository-pattern.md) |
| 0010 | Worker som separat composition root | Accepted | 2026-04-19 | [0010-worker-composition-root.md](./0010-worker-composition-root.md) |
| 0011 | Strongly-typed IDs som `readonly record struct` | Accepted | 2026-04-19 | [0011-strongly-typed-ids.md](./0011-strongly-typed-ids.md) |
| 0012 | Auth-stack: ASP.NET Core Identity + JWT (RS256) | Accepted | 2026-04-19 | [0012-auth-stack-identity-jwt.md](./0012-auth-stack-identity-jwt.md) |
| 0013 | Separat AppIdentityDbContext för Identity-tabeller | Accepted | 2026-04-19 | [0013-separate-identity-dbcontext.md](./0013-separate-identity-dbcontext.md) |
| 0014 | Refresh tokens i DB + Redis för access-token jti (avviker från BUILD.md §11.2) | Accepted | 2026-04-19 | [0014-refresh-token-strategy.md](./0014-refresh-token-strategy.md) |
| 0015 | Frontend-stack för JobbPilot (STEG 4a) | Accepted | 2026-05-06 | [0015-frontend-stack.md](./0015-frontend-stack.md) |
| 0016 | Civic design language som arkitekturkrav | Accepted | 2026-05-06 | [0016-civic-design-language.md](./0016-civic-design-language.md) |
| 0017 | Frontend Authentication Pattern (Custom, Cookie-Based) | Accepted | 2026-05-06 | [0017-frontend-auth-pattern.md](./0017-frontend-auth-pattern.md) |
| 0018 | Cookie and CSRF Strategy for Frontend Auth | Accepted | 2026-05-06 | [0018-cookie-and-csrf-strategy.md](./0018-cookie-and-csrf-strategy.md) |
| 0019 | Solo direct-push till main | Accepted | 2026-05-07 | [0019-solo-direct-push-to-main.md](./0019-solo-direct-push-to-main.md) |
| 0020 | Frontend-DTO-validering vid HTTP-gränsen med Zod | Accepted | 2026-05-11 | [0020-frontend-dto-validation-with-zod.md](./0020-frontend-dto-validation-with-zod.md) |
| 0021 | Master-version-strategi för Resume-aggregat (Fas 1) | Accepted | 2026-05-08 | [0021-resume-master-mutation-strategy.md](./0021-resume-master-mutation-strategy.md) |
| 0022 | Audit log-strategi: pipeline-behavior + marker-interface | Accepted | 2026-05-08 | [0022-audit-log-pipeline-behavior.md](./0022-audit-log-pipeline-behavior.md) |
| 0023 | Worker-pipeline-aktivering + Hangfire-infrastruktur | Accepted | 2026-05-08 | [0023-worker-pipeline-hangfire.md](./0023-worker-pipeline-hangfire.md) |
| 0024 | Audit-retention via PostgreSQL native partitioning + GDPR Art. 17-cascade-orchestration | Accepted | 2026-05-08 | [0024-audit-retention-and-art17-cascade.md](./0024-audit-retention-and-art17-cascade.md) |
| 0025 | ECS task egress accepterad som 0.0.0.0/0 under Fas 0 | Accepted | 2026-05-09 | [0025-ecs-egress-acceptance-fas0.md](./0025-ecs-egress-acceptance-fas0.md) |
| 0026 | ALB HTTP-only acceptance under Fas 0 (tidsfönster 30d + 5 triggers) | Superseded by 0027 | 2026-05-09 | [0026-alb-http-only-fas0.md](./0026-alb-http-only-fas0.md) |
| 0027 | HTTPS aktiverat på dev-ALB; ADR 0026 supersedas | Accepted | 2026-05-10 | [0027-https-aktiverat-supersession.md](./0027-https-aktiverat-supersession.md) |
| 0028 | Admin authorization via marker-interface + HTTP-policy defense-in-depth | Accepted | 2026-05-11 | [0028-admin-authorization-marker-interface-defense-in-depth.md](./0028-admin-authorization-marker-interface-defense-in-depth.md) |
| 0029 | HTTP-auth-pipeline och `IClaimsTransformation`-disciplin | Accepted | 2026-05-11 | [0029-auth-pipeline-and-claims-transformation.md](./0029-auth-pipeline-and-claims-transformation.md) |
| 0030 | Frontend API result kind-union convention | Accepted | 2026-05-11 | [0030-frontend-api-result-kind-union.md](./0030-frontend-api-result-kind-union.md) |
| 0031 | Failed cross-user access detection: strukturerad loggning + CloudWatch-aggregat | Accepted | 2026-05-12 | [0031-failed-access-detection.md](./0031-failed-access-detection.md) |
| 0032 | JobTech-integration: resilience-stack, dedup-strategi, sync-flöde (amended 2026-05-12 §8 PII-stripping + 2026-05-13 v2 path + 2026-05-16 §5 child-scope + §9 X4 admin-410 + 2026-05-16 snapshot-trunkerings-resiliens/hybrid) | Accepted | 2026-05-12 | [0032-jobtech-integration.md](./0032-jobtech-integration.md) |
| 0033 | JobbPilot.Migrate CLI-mode-dispatch (init vs schema) | Accepted | 2026-05-12 | [0033-migrate-cli-mode-dispatch.md](./0033-migrate-cli-mode-dispatch.md) |
| 0034 | DB-role privilege-separation: runtime vs migration-time creds | Accepted | 2026-05-12 | [0034-db-role-privilege-separation.md](./0034-db-role-privilege-separation.md) |
| 0035 | System-event audit-pipeline (bypass-port parallell till IAuditTrailEraser) | Accepted | 2026-05-13 | [0035-system-event-audit-pipeline.md](./0035-system-event-audit-pipeline.md) |
| 0036 | Prod-stack deferred + cloudwatch_ops_alarms-modul (v0.2-prod-launch-checklist-leverans) | Accepted | 2026-05-13 | [0036-prod-stack-deferred-and-ops-alarms.md](./0036-prod-stack-deferred-and-ops-alarms.md) |
| 0037 | Designsystem v2: civic slate-skala + dark mode (ersätter Fas 0-borttagning) | Accepted | 2026-05-16 | [0037-design-system-v2-slate-dark-mode.md](./0037-design-system-v2-slate-dark-mode.md) |
| 0038 | Typografi-omkalibrering: GOV.UK-läsbarhetsgolv (delvis supersession av ADR 0037 — typografi/density; amended 2026-05-17 — input-placeholder-regel hårdnad, Klas-direktiv: absolut regel, auth-format-undantag upphävt) | Accepted | 2026-05-16 | [0038-typography-recalibration-govuk-readability-floor.md](./0038-typography-recalibration-govuk-readability-floor.md) |
| 0039 | SavedSearch-aggregat: SearchCriteria-VO, query-baserad run-semantik och Fas 2/5-gränsdragning | Accepted (Beslut 3 delvis superseded by 0042) | 2026-05-16 | [0039-savedsearch-aggregate-and-query-run-semantics.md](./0039-savedsearch-aggregate-and-query-run-semantics.md) |
| 0040 | Smart CV-härlett filter ovanpå SavedSearch (framtida fas) | Proposed | 2026-05-16 | [0040-smart-cv-derived-saved-search.md](./0040-smart-cv-derived-saved-search.md) |
| 0041 | Dedikerad modal-border-token för WCAG 1.4.11 i dark mode (partiell komplettering av ADR 0037) | Accepted | 2026-05-16 | [0041-dark-modal-border-non-text-contrast.md](./0041-dark-modal-border-non-text-contrast.md) |
| 0042 | Sök-yta-informationsarkitektur: kollaps-filter, multi-värde-kriterier, typeahead, relevans-sort (Beslut 3 i ADR 0039 delvis superseded; impl-notat 2026-05-17 — Beslut C index=btree functional partial-index (Variant A) / dedikerad SuggestPolicy 30/10s / typeahead self-contained debounce-hook ej TanStack / Beslut B jsonb Yta A3 tolerant converter / Beslut E since fast rullande 7d serverstyrt) | Accepted | 2026-05-16 | [0042-search-surface-information-architecture.md](./0042-search-surface-information-architecture.md) |
| 0043 | Taxonomi-ACL för sök-ytan (lokal taxonomi-snapshot + Anticorruption Layer; utvidgar ADR 0042 Beslut C-datakälla, domänkontrakt OFÖRÄNDRAT — superseder ingen ADR; additiva korsref-notat i ADR 0042/0039) | Accepted | 2026-05-17 | [0043-taxonomy-acl-for-search-surface.md](./0043-taxonomy-acl-for-search-surface.md) |

## Planerade ADRs

BUILD.md Bilaga B listar ADRs som ska skrivas när respektive tekniskt val blir aktuellt:

- `NNNN-postgresql-over-sqlserver.md` — när databasval diskuteras mot andra alternativ
- `NNNN-aws-over-azure.md` — när moln-val ifrågasätts
- `NNNN-bedrock-eu-for-system-key.md` — när EU-inference-profile-beslutet formaliseras
- `NNNN-byok-architecture.md` — BYOK-krypteringsflöde
- `NNNN-hangfire-background-jobs.md` — när bakgrundsjobb-val görs

Numrering tilldelas löpande när ADR skrivs. BUILD.md Bilaga B är förslag på ordning, inte bindande.

## Skapa ny ADR

1. Claude Code-användning: `/new-adr <slug>` — triggar `adr-keeper`-agenten
2. Manuell: kopiera mall från existerande ADR (förslagsvis 0001 eller 0004 som är nya och följer nuvarande struktur)
3. Nästa lediga nummer = `ls docs/decisions/ | grep -oE '^[0-9]+' | sort -n | tail -1 | awk '{print $1+1}'`
4. Filnamnsmönster: `NNNN-kebab-case-slug.md` — alltid 4-siffrigt nummer med leading zeros

## Mall-struktur

Varje ADR bör ha följande sektioner:

- **Header:** `# ADR NNNN — <Titel>` + metadata (Datum, Status, Kontext, Beslutsfattare, Relaterad)
- **Kontext:** varför frågan är relevant, vilka krafter som spelar in
- **Beslut:** vad som valts, koncist
- **Konsekvenser:** positiva + negativa + mitigering
- **Alternativ övervägda:** minst 2-3 alternativ som avvisades, med motivering
- **Implementationsstatus:** vad som faktiskt är gjort vs planerat
- **(Frivilligt) Växtväg, Validering, Relaterade beslut** — som relevant

Se ADR 0001 eller 0007 för referens.

---

*Detta index underhålls av `docs-keeper`-agenten (`.claude/agents/docs-keeper.md`). Uppdateras automatiskt när ny ADR tillkommer via `/new-adr`-kommandot.*
