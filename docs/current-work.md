# Current work — JobbPilot

**Status:** **FAS 2 POLISH-BLOCK LEVERERAD 2026-05-11 ~16:30 — väntar Klas-diff-granskning innan push.** 5 audit-fynd fixade in-block (N-1 + N-3 + H-4 + N-2 + H-3), 4 TDs lyfta (TD-58/59/60/61). Backend 594 → 607. Inget pushed ännu — 4 commits redo per Conventional Commits.
**Senast uppdaterad:** 2026-05-11
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu — Fas 2 Polish-block (pending push)

**Stationär-CC-session 2026-05-11 ~16:30 — arch-audit-fynd-fix.** Klas valde Alt 2 (polish-block) efter audit Fas 1 Discovery levererad 2026-05-11 ~12:15. Audit klassade 0 Blocker / 0 Major / 4 Minor / 3 Nit. Klas-val: kör 5 in-block-fix i en session.

### Block-leverans

| Block | Scope | Output | Status |
|-------|-------|--------|--------|
| A | N-1 events + N-3 DomainException + H-4 paging-rename | Domain-events + Domain.Common.DomainException + Page-konvention | ✓ Klart |
| B | N-2 IdempotentAdminRoleSeeder env-gate | IHostEnvironment-gated catch + 5 unit-tests + prod-bubble anti-regression | ✓ Klart |
| C | H-3 role-fetch → IClaimsTransformation | SessionRoleClaimsTransformation + sentinel-claim + arch-test allowlist | ✓ Klart |
| D | TDs + docs + commits | TD-58/59/60/61 + session-logg + steg-tracker | ✓ Klart (pending push) |

### CTO-beslut tagna (3 entydiga)

- **N-1 Riktning A** (events uppåt): Evans 2003 + Vernon 2013 kap. 8 + Martin 2017 kap. 13 CCP. Domain events är historiska fakta; frånvaro av subscriber idag är inte argument mot raise. GDPR-cascade-bevis kräver event-trail.
- **N-3 Alt A** (DomainException i Domain.Common): CLAUDE.md §2.1 dependency rule + Martin 2017 kap. 8 OCP. Alt B (Application-placering) bryter dep-rule; Alt C (per-aggregate) bryter OCP; Alt D (lämna) bryter §3.4 + §5.1.
- **N-2 Alt A** (env-gate): CLAUDE.md §3.4 fail-loud + Twelve-Factor §10 dev/prod parity + Martin 2017 kap. 13 CCP. Alt B (fixture-refactor) bryter 4h-regel; Alt C (log-level) bevarar fail-silent.

### Agent-review-leverans

**3 Major fixade in-block** per CLAUDE.md §9.6 (kvalitet > tempo):

1. **Block B security-auditor Major:** test-fixture-removal-blindzon. Fix: separat `ProdSeederBubbleFactory` som BEHÅLLER seedern + skipar Identity-migration → bevisar 42P01-bubbling i Production E2E (inte bara predicate-funktionen isolerat).
2. **Block C security-auditor Major:** `CancellationToken.None` på `GetRolesAsync` = resurs-läckage-risk. Fix: inject `IHttpContextAccessor` + använd `HttpContext?.RequestAborted`.
3. **Block C dotnet-architect Minor (uppgraderad till Major in-block):** `HasClaim(Role)`-idempotency-guard otillförlitlig vid mid-session-promotion. Fix: sentinel-claim `jobbpilot:roles_resolved` sätts post-fetch.

Alla Minor fixade in-block per 4h-regel (test-naming, XML-doc-remarks, defensiv cast, kommentar-justering, arch-test för IClaimsTransformation-allowlist).

### TDs lyfta (4)

| ID | Område | Defer-fas | Scope |
|----|--------|-----------|-------|
| TD-58 | H-1 IAccountHardDeleter ISP-split | Fas 6 admin-impersonation | ~2h |
| TD-59 | H-2 ICurrentJobSeeker user→JobSeekerId-port | Fas 6 impersonation | ~2-3h |
| TD-60 | ADR auth-pipeline-ordning + IClaimsTransformation-disciplin | Docs-pass | ~45 min |
| TD-61 | Audit-trail-evidence-test för seeder | Observability-pass / Fas 6 | ~1h |

**Aktiva TDs efter denna session:** TD-39, TD-41, TD-51, TD-52, TD-53, TD-56, TD-57, **TD-58, TD-59, TD-60, TD-61**.

### Tester (full svit grön — pending push)

- Domain.UnitTests: **163** (+6 från Block A)
- Application.UnitTests: **201** (+5 från Block B)
- Architecture.Tests: **32** (+1 från Block C)
- Migrate.UnitTests: **6**
- Api.IntegrationTests: **179** (+1 från Block B prod-bubble-test)
- Worker.IntegrationTests: **26**
- **Total: 607** (+13 från Block A+B+C)

### Pending commits (4, väntar Klas-diff-granskning)

| Commit | Scope | Filer (huvudsakliga) |
|--------|-------|-----------------------|
| 1 | `refactor(domain): N-1 + N-3 + H-4 — domain event-konsistens + DomainException + paging-rename` | Domain + Application/Queries + Api/Program.cs catch + tester (Block A) |
| 2 | `fix(infra): N-2 — IdempotentAdminRoleSeeder prod-gate-hardening` | Infrastructure/Identity + csproj InternalsVisibleTo + tester (Block B) |
| 3 | `refactor(auth): H-3 — SoC-split role-fetch till IClaimsTransformation` | Infrastructure/Auth + DI + arch-test (Block C) |
| 4 | `docs: Fas 2 polish-block session-end — TD-58/59/60/61 + session-logg + steg-tracker` | docs/tech-debt + docs/current-work + docs/steg-tracker + docs/sessions (Block D) |

**OBS:** `ConnectionStringLeakageTests.cs` har en harmlös `dotnet format`-driven reindentation av nested foreach. Inkluderas i Block A-commit som format-disciplin (inga semantiska ändringar).

---

## När nästa session startar

Klas reviewar diff per CLAUDE.md §6.3 punkt 4 (manuell diff-granskning). Vid GO: 4 commits + push.

Sedan optionell väg:

- **Väg A:** TD-60 (auth-pipeline-ADR) som dedikerat docs-pass (~45 min)
- **Väg B:** TD-61 (audit-trail-evidence-test) som observability-pass (~1h)
- **Väg C:** Fortsätt feature-arbete — Fas 2 JobTech-integration (blockerad till ADR 0005) eller annan icke-blockerad Fas 1-feature
- **Väg D:** Pausa, ny session

Inga aktiva TDs blockerar Väg C. Polish-block levererar "100% clean" före Fas 2-feature-arbete.

---

## Föregående session-summary (referens) — Arch-audit Fas 1 Discovery

**Stationär-CC-session 2026-05-11 ~12:15:** dotnet-architect-agenten verifierade Clean Arch-isolering, DDD-invariant-skydd, CQRS-pipeline-disciplin, SOLID/DRY/SoC-status över 6 src-projekt + 24 architecture-tester. CLAUDE.md §5.1 anti-pattern-katalogen gav noll Grep-träffar i `src/`. 22 STEG-rader klassade grön/gul. 4 Minor + 3 Nit dokumenterade med fil-ref + scope-rek. Rapport: `docs/reviews/2026-05-11-arch-audit-discovery.md`.

**Audit-fynd som denna polish-block adresserade:**
- N-1 ✓ stängd (events uppåt på Application + JobSeeker SoftDelete)
- N-3 ✓ stängd (DomainException + Resume.MasterVersion-guard)
- H-4 ✓ stängd (PageNumber → Page i 2 queries)
- N-2 ✓ stängd (env-gate på 42P01-catch)
- H-3 ✓ stängd (IClaimsTransformation SoC-split)
- H-1 → TD-58 (Fas 6 admin-impersonation)
- H-2 → TD-59 (Fas 6 impersonation)

---

## Pre-existing infra (oförändrat)

| Resurs | Identifier |
|---------|-----------|
| Public URL | `https://dev.jobbpilot.se/api/ready` |
| API task-def | `jobbpilot-dev-api` (post-TD-38 apply) |
| Worker task-def | `jobbpilot-dev-worker` (post-TD-38 apply) |
| Tag (senaste) | `v0.1.2-dev` på SHA `7cde3c7` |

---

## Workflow-disciplin (oförändrad)

Per CLAUDE.md §9.2 + §9.6:

1. Discovery först
2. Multi-approach-val → senior-cto-advisor auto-invokeras (3 CTO-beslut denna session: N-1 Riktning A, N-3 Alt A, N-2 Alt A — alla entydigt motiverade mot källor)
3. STOPP-rapport till Klas innan implementation om CTO osäker / fas-strategiskt (denna session: Klas gav "kör utan stanna" så block-flödet körde sammanhängande)
4. Agent-reviews parallellt vid relevant scope (5 reviews denna session: dotnet-architect×2, code-reviewer×2, security-auditor×2)
5. In-block-fix-default per 4h-regel (3 Major fixade in-block)
6. Commit + push efter Klas-diff-granskning (direct-push till main per ADR 0019)
