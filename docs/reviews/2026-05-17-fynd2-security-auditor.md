# Security-audit: ADR 0043 Fynd 2 — Taxonomi-ACL backend (MAP-3 + DoS + cache + GDPR)

**Status:** ✅ **GO — commit godkänd. 0 Critical, 0 High, 0 GDPR-Blocker.**
**Granskat:** 2026-05-17 · **Auditor:** security-auditor (agentId `a454b46c63ca3e50a`)
**Auktoritet:** GDPR Art. 4/5/32, CLAUDE.md §3.4/§5.1/§5.4, OWASP API4:2023, CWE-400, OWASP Web Cache Deception, ADR 0043 Beslut D / CTO MAP-3

## Verdict

GO. Rate-limit-talen **20/60s = verifierad BLOCKING-bedömning, korrekt, ingen justering**. Reverse-lookup-cap `SearchCriteria.MaxConceptIds*2` (=20) + per-element MaxLength 32 + Cascade.Stop verifierat. `Cache-Control: private` + ETag korrekt mot Web Cache Deception; `/labels` `private, no-store` extra skarpt. Advisory-lock `pg_advisory_xact_lock(4307001)` enda förekomsten (ingen kollision) + double-checked re-read + transaktion = race-fri idempotent seed. 42P01-grace gated Dev/Test → fail-loud Production korrekt (CLAUDE.md §3.4), bevisat av TaxonomySnapshotSeederProdBubbleTests. Clean Arch ren (ingen EF-entity över Application-gräns, ingen Npgsql i Application, IAppDbContext växer ej). GDPR: taxonomi = publik icke-PII referensdata — soft-delete/audit/encryption korrekt utelämnat. Ingen PII i logg-ytor.

## Fynd

| Severity | Antal | Fynd |
|---|---|---|
| Critical / High / GDPR-Blocker | **0** | — |
| Major | 0 | — |
| Minor | 1 | Faulted-`Lazy<Task>` permanent-fail i `TaxonomyReadModel` — defense-in-depth, ej säkerhetskritisk, ej commit-hinder. **CC åtgärdade in-block** (§9.6): bytt till lås-fri `Volatile`-publicering som endast cachar lyckad laddning (fault → retry, ej permanent fail). |
| Flagga (annat scope) | 1 | Reverse-lookup-label-rendering = FE-ansvar (nextjs-ui-engineer): rendera som text, aldrig `dangerouslySetInnerHTML`. Backend XSS-fri (System.Text.Json escapar; `id` loggas aldrig). |

## Praise

- `private` + ETag-disciplin på auth-gated endpoint exakt rätt mot Web Cache Deception; `/labels` `private, no-store` extra skarpt
- Reverse-lookup-cap härledd ur domänkonstant (`MaxConceptIds*2`) — DRY + complete mediation
- Advisory-lock + double-checked re-read + transaktion = korrekt race-fri idempotent seed
- Fail-loud-prod-kontrakt (42P01 gated Dev/Test) korrekt per §3.4, bevisat med dedikerad prod-bubble-anti-regressionstest
- Ingen PII i någon logg-yta; rate-limit-rejection loggar path utan IP/email

Full granskningstext per punkt (Endpoints/Rate-limit/Cap/Seeder/ReadModel/CleanArch/GDPR): se agent-transkript 2026-05-17. CC-åtgärd: Minor in-block-fixad (TaxonomyReadModel Volatile-cache, commit 75f0510); FE-flagga vidare till nextjs-ui-engineer.
