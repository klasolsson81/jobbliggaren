# Security-audit: gh-security-hardening (CodeQL SAST, observe-only Fas 1)

**Agent:** security-auditor (agentId `a2e68a0122b279f3a`)
**Status:** ✓ PASS — CONDITIONAL (1 Minor-rekommendation, ej merge-blockerande)
**Granskat:** 2026-06-07
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.4 + §9.2, GitHub Actions security model (least-privilege, script injection, supply-chain), ADR 0045-ratchet-precedens
**Scope:** `.github/workflows/codeql.yml` (NY), `.github/codeql/codeql-config.yml` (NY), `docs/tech-debt.md` (ren docs)

## Blockers

Inga.

## Major

Inga.

## Minor (defense-in-depth — ej blockerande Fas 1)

1. **Floating `@v4`/`@v5`/`@v6`-actions istället för SHA-pinning**
   Fil: `.github/workflows/codeql.yml` (checkout@v6, setup-dotnet@v5, cache@v5, codeql-action/init@v4, codeql-action/analyze@v4)
   Bedömning: **Acceptabelt Fas 1, ingen åtgärd krävs i denna PR.** SHA-pinning är hårdare supply-chain-hygien, men:
   - Workflowen har **konvention-paritet** med `build.yml` som genomgående kör floating major.
   - `.github/dependabot.yml` täcker `github-actions`-ekosystemet (grupp `actions-all`, månadsvis) → bumpar fångas.
   - Att SHA-pinna *enbart* CodeQL-workflowen vore inkonsekvent mot repo-konventionen; ett byte bör vara ett **repo-brett beslut** (alla workflows + Dependabot `pinned-sha`-stöd) → egen ADR-fråga, ej denna PR.
   - **Self-veto mot §9.6:** ej en TD — repo-brett policyval för Klas, inte fas-/dependency-blockerad åtgärd. Noteras som observation.

## Fokuspunkt-domar

**Workflow-permissions least-privilege — GODKÄNT.** `security-events: write` (SARIF-upload) + `contents: read` (checkout) + `actions: read` (workflow-metadata, krävs för private/internal repos) är exakt minsta nödvändiga. Inget överflödigt (ingen `pull-requests: write`, `id-token`, `packages`). Job-scoped, inte workflow-globalt — strikt least-privilege.

**Script injection — GODKÄNT, ingen yta.** Enda interpolation i shell-kontext är `${{ matrix.language }}` (kontrollerat matrix-värde, ej untrusted input). Inga `github.event.*`-angripar-kontrollerbara fält i `run:`-block.

**`pull_request`-trigger — GODKÄNT (säkert val).** Använder `pull_request` (INTE `pull_request_target`) → forkade PR:er kör läs-begränsat utan secret-åtkomst. Den farliga `pull_request_target`-fällan medvetet undviken.

**GDPR/PII — GODKÄNT, ingen exponering.** SARIF kan teoretiskt innehålla kod-snippets, men (a) ingen hårdkodad PII i kodbasen (§5.4), (b) SARIF går till repots egna privata Security-flik (samma trust-boundary), (c) paths-ignore exkluderar test-/coverage-artefakter. Ingen ny dataväg, ingen ny sub-processor, ingen tredjelands-överföring.

**Observe-only-integritet — GODKÄNT, kan inte blockera merge.** Verifierat mot build.yml: required check = endast `ci`-aggregatet (`ci.needs: [backend, frontend, coverage]`). CodeQL är separat workflow utanför aggregatet + `continue-on-error: true` + `fail-fast: false`. Speglar ADR 0045-mönstret. Ratchet→blockerande dokumenterad som Klas-GO-gated.

**Positivt:** `manual` build-mode säkerhetsmässigt korrekt (utan kompilering vore Mediator.SourceGenerator-genererad auth-pipeline osynlig för SAST). `concurrency`/`timeout-minutes: 20` god hygien.

## Docs-delen

TD-101-korrigeringen gör docs mer sanningsenlig; TD-104 punkt 3 korrekt markerad delvis adresserad med kvarvarande spec-edit flaggad. Konsekvent med §9.7. Ingen säkerhetspåverkan.

## Dom

**0 Blockers, 0 Major, 1 Minor (icke-blockerande).** Inga GDPR-konsekvenser. **PASS — säkerhetsmässigt mergeklar.** Floating-action-pinning bör vid önskemål tas som separat repo-brett policy-beslut.
