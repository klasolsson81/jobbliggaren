---
session: TD-13 FAS 3.5 — PII-fält-kryptering (KMS-envelope)
datum: 2026-05-18
slug: td13-fas35-c1-c3-pii-encryption
status: C1/C2/C3 + hotfix levererade & pushade (main grön bbf8081); C4 architect-design låst, ej påbörjad — session-checkpoint (§1.5 context-budget)
commits:
  - 9952a0c docs(decisions) ADR 0049 (Proposed) + STOPP D discovery/CTO
  - a039bb0 docs(decisions) ADR 0049 Proposed→Accepted + mekanik-not + STOPP I CTO-triage
  - ddf6c55 fix(web) canonical Tailwind v4 radio-group (sidofix)
  - 78958ce feat(security) C1 KMS-envelope-fundament
  - 1162f1c fix(infra) Approach D — C1 J3-regression (FieldEncryptionOptionsValidator)
  - 018e001 feat(security) C2 per-användar-DEK-store
  - 1851632 docs(decisions) ADR 0049 Mekanik-not 3
  - bbf8081 feat(security) C3 fält-kryptering interceptor-par
---

# TD-13 FAS 3.5 — PII-fält-kryptering

## Mål
ADR 0049: 4 PII-kolumner (applications.cover_letter, application_notes.content,
follow_ups.note TEXT + resume_versions.content JSONB) krypteras app-side via
KMS-envelope (per-användar-DEK), icke-destruktivt. raw_payload medvetet
exkluderad (CTO Beslut 3 — redan saniterad, ADR 0032/0039-load-bearing).

## Levererat denna session (STOPP D → C3)

**STOPP D:** Discovery (verbatim 5 kolumners EF-config, raw_payload load-bearing
×3, KMS-mönster, web-search-grund) → senior-cto-advisor 5-besluts-dom
(per-användar-DEK, crypto-erasure JA, raw_payload exkluderad, hybrid
lazy+backfill, jsonb→text expand/contract) → ADR 0049 utkast→Accepted
(Klas-GO) → CTO-triage 3 STOPP-I-frågor (interceptor==lazy-VC-intention;
AppDbContext.Set keyless; JSON-VC-gate). Mekanik-not 1 (interceptor ersätter
"lazy ValueConverter", EF-doktrin).

**C1** (`78958ce`): KMS-envelope-fundament. `IFieldEncryptor`/`IDataKeyProvider`
(Application-portar, Domain orört ADR 0009) + `KmsEnvelopeEncryptor`
(AES-256-GCM, `v1:`+base64-sentinel, fail-closed, DEK-32-enforce) +
`KmsDataKeyProvider` (KMS GenerateDataKey/Decrypt, owner-AAD, §5.4 ingen
nyckel i logg) + `FieldEncryptionOptions`. AWSSDK.KeyManagementService 4.0.8.8.
TDD 485→495. Gates GO. In-block: Major 1 (DEK-längd) + Minor 1/3.

**Hotfix Approach D** (`1162f1c`): C1:s globala
`.Validate().ValidateOnStart()` applicerade Production-invariant på ~6
KMS-fakande integ-test-hostar → alla Api.IntegrationTests host-boots broken
på main (J3). CTO Approach D: `FieldEncryptionOptionsValidator`
(`IValidateOptions`, kanonisk .NET-form) — hård fail Production/Staging, warn
Development/Test; runtime-guard = faktiskt fail-closed-skydd. ADR Mekanik-not
2. **Lärdom (CTO-noterad):** mitt C1-omdöme "ej CTO-triage, ren
ADR-conformance" var fel — architect Minor 2-förvarning korrekt;
ADR-ordalydelse om *mekanism* triggar CTO-triage när agent flaggar
miljö-/fas-risk. 344/344 Api-integ verifierad.

**C2** (`018e001`): `UserDataKey` keyless Infrastructure-entitet (PK
job_seeker_id,dek_version; EJ IAppDbContext; FK ON DELETE CASCADE
defense-in-depth) + `IUserDataKeyStore` (GetOrCreate via KMS,
DeleteDataKeysAsync crypto-erasure idempotent) + `ScopedUserDataKeyCache`
(scoped memoise, Dispose ZeroMemory — C1 security Minor 2 STÄNGD+bevisad) +
migration. Arch-test-spärr FRÅGA 2. Seam 1 (WorkerTestFixture deterministisk
fake-KMS) + Seam 3 (InternalsVisibleTo). security-auditor GO.

**ADR Mekanik-not 3** (`1851632`): decrypt-on-read DEK-prefetch Approach B
(`IMaterializationInterceptor` synkron + §3.5 → `DecryptionKeyPrefetchBehavior`
värmer cache före query).

**C3** (`bbf8081`) — TD-13:s KÄRNA, svåraste batchen, 4 hardpoints:
- #1 DTO-projektion kringgår MaterializationInterceptor → CTO Approach A
  (GetApplicationByIdQueryHandler materialiserar ej projicerar; ADR
  Mekanik-not 4 + ADR 0048 additivt tillämpningsundantag; Klas-GO utökad scope).
- #2 re-entrancy-deadlock (interceptor→store→same-ctx SaveChanges, 45-min
  testhängning) → architect Approach A (write-interceptor ren synkron
  cache-konsument; markör-rename `IRequiresFieldEncryptionKey`/
  `FieldEncryptionKeyPrefetchBehavior`; 5 write-commands markerade; Mekanik-not 5a).
- #3 system-scope decrypt (MarkGhosted/AccountHardDeleter Hangfire
  materialiserar krypterad utan auth/DEK → krasch) → CTO (iv)
  scope-differentierad fail-closed (auth-scope kasta; system-scope ciphertext
  passthrough); arch-test-spärr; Mekanik-not 5b. **Klas-GO på riktning.**
- #4 EF DI: auto-discovery falsifierad + ManyServiceProvidersCreatedWarning
  (prod-reell) → architect singleton-interceptorer + (sp,options)
  .AddInterceptors + Context.GetService (Microsoft Learn-verifierad);
  Mekanik-not 5c. ApiFactory speglar.
- EU-region-guard (security-auditor C3 Medium) i validatorn + 8 tester.
- Svit: unit 493 / arch 63 / Worker-integ 48 / Api-integ 344 — alla gröna.
  security-auditor GO (0 Crit/High/GDPR) + code-reviewer GO (0 Block/Major;
  Minor 1 död kod in-block-fixad, Minor 2 sentinel-literal → C4-touch
  code-reviewer-sanktionerad).

## Beslut & detourer
- raw_payload exkluderad ur envelope (CTO Beslut 3) — sparar negativ ROI,
  ADR 0032/0039-kohesion.
- ADR-livscykel: 5 mekanik-noter (1–5c) — alla informella preciseringar
  tvingade av EF Core 10-doktrin, paritet, §9.6 p.5; ej formella amendments.
  Klas kan override:a 5b/5c → amendment vid STOPP V (flaggat).
- C3:s 4 hardpoints krävde ~13 CTO/architect-eskaleringar — interceptor-
  envelope mot befintlig arkitektur (DTO-projektion, EF re-entrancy,
  Hangfire-scope, EF DI) gav återkommande djup-friktion. Foundation solid.
- 45-min testhängning = re-entrancy-deadlock (löst i C3 → svit ~9s).
- MTP-test-invokation: `dotnet exec <dll> -class <FQN>` (ej `dotnet test
  --filter` — dumpar help/zero-tests).

## Nästa session
Återuppta C4 (architect-design LÅST, se current-work.md "NÄSTA SESSION").
C4.0 gate-test → C4.1–C4.4 → C5 backfill → C6 crypto-erasure-hook →
STOPP I-rapport → STOPP V (Klas-GO deploy/arkivera TD-13/FAS 4-prompt).
CTO/architect-kedja non-stop till STOPP V (Klas-direktiv 2026-05-18).

## Oattribuerat (flaggat Klas)
`docs/JobbPilot.zip` + `docs/jobbpilot-v3-bundle/` i working tree — ej CC,
ej TD-13, ej rörda/raderade, exkluderade ur commits.
