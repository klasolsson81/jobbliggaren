# TD-13 FAS 3.5 C4.2 / C5 / C6 — security-audit (BLOCKING)

**Datum:** 2026-05-19
**Agent:** security-auditor (agentId a530b7c3ce93e609c) — read-only; rapport applicerad till review-trailen av CC (§9.4: agent-output, ej ADR-prosa)
**On-disk:** uncommittad C4.2/C4.3/C4.4/C5/C6 (HEAD `8907ebd`)
**Auktoritet:** GDPR Art. 5/17/30/32 · CLAUDE.md §5.1/§5.3/§5.4 · ADR 0049 (Accepted) · ADR 0024 · OWASP Cryptographic Storage / Defense-in-Depth · AWS KMS envelope · Microsoft Learn (EF Core 10 Interceptors / Value Conversions / ExecuteDelete+transactions)

## Verdict: GO — 0 Critical / 0 High / 0 Major / 0 GDPR

Veto ej utövat. STOPP I-gaten passerad ur säkerhets-/GDPR-perspektiv. Inga MVP-undantag behövdes (inga blockers att undanta).

## Verdict per audit-fokus (7/7 PASS)

1. **Ingen plaintext-PII-läcka — PASS.** `content_enc` håller `v1:`+AES-256-GCM-ciphertext; legacy `content` jsonb skrivs ALDRIG av EF post-#1c (`builder.Ignore(rv => rv.Content)` + `ContentLegacyJson` `PropertySaveBehavior.Ignore` before+after — strukturellt omöjlig write-back, ej konventionsmässig). Ingen plaintext-PII i logg (interceptorer/backfiller/job loggar endast räknare + JobSeekerId-Guid; `.Content`/`.CoverLetter`/`.Note` = `nameof()`/SQL-predikat, aldrig värdematerialisering). Migration = `ALTER COLUMN content DROP NOT NULL` (metadata-only, ingen content-drop/ALTER TYPE/dataexponering).
2. **Cross-user-DEK-isolering — PASS.** C5 per-owner fresh DI-scope (Scoped cache/owner/ctx — ingen batch-loop-läcka); DEK zeroad efter prefetch; owner-resolution ResumeVersion skugg-FK ResumeId→spårad Resume→JobSeekerId (fel ägare ej resolverbar; aggregat-brott → fail-closed); KMS encryption-context/AAD owner-bindning oförändrad från C3.
3. **Fail-closed — PASS.** Write: ingen cachad DEK → CryptographicException FÖRE DML. Read: autentiserad scope utan DEK → kasta; system-scope → Content null (aldrig ciphertext-som-plaintext). Ingen klartext/default-DEK-fallback någonstans. Backfiller: KMS-fel propageras (ingen tyst skip som lämnar plaintext permanent).
4. **GDPR Art. 17 crypto-erasure-atomicitet (C6) — PASS.** `DeleteDataKeysAsync` inne i hard-delete-transaktionen före SaveChanges/Commit; rollback → ingen partiell erasure; `ExecuteDeleteAsync` enlistas i ambient tx (delad scoped AppDbContext); idempotent (0 rader = no-op).
5. **SQL-injektion — PASS.** Owner-scoped raw SQL parameteriserad ({0}/{1}); konstant-predikat-queries utan användardata/concatenation (§5.4).
6. **Arch-test-tillräcklighet — PASS.** Projektion-guard + system-scope-scan + #1c-modell-invariant + markör-krav täcker bypass-vektorerna; GetResumeByIdQueryHandler materialiserar entiteten (ej krypterad SQL-projektion).
7. **Migrations-säkerhet — PASS.** Expand-phase non-destruktiv, reverterbar-där-säkert, ingen dataexponering.

## Findings

0 Critical / 0 High / 0 Major / 0 GDPR.

**Minor M1 (ej blockerande, framtida härdning):** `UserDataKeyStore.DeleteDataKeysAsync` ambient-tx-enlistning vilar på delad-scoped-DbContext-invarianten; korrekt implementerad + architect-verifierad; `CryptoErasureHardDeleteTests` täcker happy-path/cross-user/idempotens men ej en explicit framtvingad-fault-rollback-regressionstest. Rekommendation: `HardDelete_FaultAfterDekDelete_RollsBackErasure` i framtida härdnings-pass. **Ej GO-blocker** — atomiciteten är korrekt i nuvarande kod; test-djup, ej defekt.

## Klas-notering (ej blocker)
ADR 0049 Mekanik-not 5c/6 + C4.2-preciseringarna (nullable ContentEnc / ContentLegacyJson read-only / `ALTER COLUMN content DROP NOT NULL`) är architect/CTO-bedömda mekanik-preciseringar; Klas-override till formell ADR-amendment kvarstår öppen och hör till STOPP V-rapporten, ej denna audit.
