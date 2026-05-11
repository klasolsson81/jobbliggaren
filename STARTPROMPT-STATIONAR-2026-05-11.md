# Stationär CC startprompt — efter Fas 2 polish-block

**Skapad:** 2026-05-11 ~17:00 av laptop CC efter Fas 2 polish-block pushad.
**Du:** Fresh CC på stationär (kanske aldrig sett detta sessionsspår tidigare).
**Filtyp:** Ephemeral — du tar bort den i sista commit (se "Slutsteg" nedan).

---

## Förkrav

1. **Repo:** uppdatera till senaste main:
   ```bash
   git pull origin main
   ```

2. **Verifiera HEAD = `c0ada25`:**
   ```bash
   git log --oneline -6
   ```
   Förväntat topp-6:
   ```
   c0ada25 docs: Fas 2 polish-block session-end — TD-58/59/60/61 + session-logg + steg-tracker
   35b9dc0 refactor(auth): H-3 — SoC-split role-fetch till IClaimsTransformation
   a683ae1 fix(infra): N-2 — IdempotentAdminRoleSeeder prod-gate-hardening
   ff3704f refactor(domain): N-1 + N-3 + H-4 — domain event-konsistens + DomainException + paging-rename
   30776b6 docs(reviews): arch-audit Fas 1 discovery — STEG 1-14 retrospektiv
   233ec32 docs(research): startprompt för retrospektiv arkitekturell audit
   ```

3. **`git status` ska vara clean** (förutom denna `STARTPROMPT-STATIONAR-*.md` om laptop pushade den).

4. **AWS SSO behövs INTE** för Väg A/B/C nedan (rena kod- och docs-vägar utan AWS-anrop).

5. **Lokala krav:**
   - .NET 10 SDK (per `global.json`)
   - Node 22+ med pnpm
   - Docker Desktop (för Testcontainers — Api.IntegrationTests + Worker.IntegrationTests behöver Postgres/Redis-containers)

---

## Mandatory reads vid session-start (CLAUDE.md §1.5)

1. **CLAUDE.md** — hela, särskilt:
   - §2 kärnprinciper (Clean Arch + DDD + CQRS)
   - §3 .NET-standarder
   - §5 anti-patterns
   - §9.2 agent-invocation
   - §9.6 4h-regel + CTO-disciplin

2. **`docs/current-work.md`** — senaste session-state. Innehåller hela Fas 2 polish-block-leveransen, CTO-beslut, agent-review-summary, TDs lyfta, test-läge.

3. **`docs/sessions/2026-05-11-1630-fas2-polish-block.md`** — per-block-leverans-detalj.

4. **`docs/reviews/2026-05-11-arch-audit-discovery.md`** — arch-audit-rapporten. Alla 5 in-block-fynd (N-1 + N-3 + H-4 + N-2 + H-3) är nu stängda. H-1 + H-2 → TD-58 + TD-59.

5. **`docs/tech-debt.md`** (skim, fokus på TD-58 till TD-61 + andra aktiva).

6. **`docs/steg-tracker.md`** v1.17 — skim, nyaste rad i §4 dokumenterar Fas 2 polish-block.

---

## Läget — Fas 2 polish-block KLAR

Backend 594 → **607 tester gröna**. 4 commits pushade.

**Audit-fynd-status:**

| Fynd | Status | Hur |
|------|--------|-----|
| N-1 (events SoftDelete) | ✓ Stängd | Application + JobSeeker raisar nu events (CTO Riktning A) |
| N-2 (seeder 42P01-gate) | ✓ Stängd | Env-gate + prod-bubble anti-regression-test (CTO Alt A) |
| N-3 (DomainException) | ✓ Stängd | Domain.Common + Resume.MasterVersion-guard (CTO Alt A) |
| H-3 (SoC-split role-fetch) | ✓ Stängd | SessionRoleClaimsTransformation + sentinel-claim + arch-test |
| H-4 (PageNumber → Page) | ✓ Stängd | 2 queries + validators + arch-test strikt |
| H-1 → TD-58 | Defer | Fas 6 admin-impersonation (~2h) |
| H-2 → TD-59 | Defer | Fas 6 impersonation (~2-3h) |

**Aktiva TDs:** TD-39, TD-41, TD-51, TD-52, TD-53, TD-56, TD-57, TD-58, TD-59, TD-60, TD-61.

**Inga aktiva TDs blockerar feature-arbete.** Polish-block levererade "100% clean" före Fas 2.

---

## Uppdrag — Klas väljer väg

Klas paused laptop-sessionen efter polish-block pushad. Nästa beslut är hans. **Innan du börjar något — fråga Klas vilken väg.**

### Väg A — TD-60 (auth-pipeline-ADR + IClaimsTransformation-disciplin)

**Scope:** ~45 min CC-tid, **pure docs**.

Skapa ny ADR som dokumenterar:
1. Pipeline-ordning (Authentication → IClaimsTransformation → Authorization-policy)
2. Var nya security-kritiska claims läggs till (transformation vs handler)
3. Per-request-fetch vs cache-modell (CTO A1-beslut 2026-05-11 — security-first, ingen cache)
4. Konsument-allowlist-mönstret (`ClaimsTransformationAllowlistTests` som strukturell spärr)

**Next ADR-nummer:** kolla `docs/decisions/README.md` index — sannolikt ADR 0029.

**Process:** invokera `adr-keeper` via `/new-adr <slug>` eller direkt med `Agent({subagent_type: "adr-keeper", ...})`. ADR-numrering är sekventiell.

### Väg B — TD-61 (audit-trail-evidence-test för IdempotentAdminRoleSeeder)

**Scope:** ~1h CC-tid, integration-test.

Seederns XML-doc (rad 19-22) hävdar att admin-tilldelning är "observerbar via samma audit-log som admin-vyn själv granskar". Inget verifierar att audit-händelse faktiskt skapas vid bootstrap.

**Föreslagen åtgärd:** Integration-test som efter `EnsureUserIsAdminAsync` läser audit-loggen och asserterar att en relevant `AuditLogEntry` finns för Admin-role-add.

**Triggerpunkt vid CC-arbete:**
- Discovery: läs `IdempotentAdminRoleSeeder.cs:90, 127` + `UserManager.AddToRoleAsync`-flow. Verifiera om audit-write hänger på Identity-pipeline eller separat Mediator-event. Om separat: testet behöver subscribe-mönster.
- Befintliga audit-tester i `tests/JobbPilot.Api.IntegrationTests/Admin/AdminAuditLogTests.cs` är mall för audit-log-läsning.

### Väg C — Fortsätt feature-arbete

**Fas 2 (JobTech-integration):** Blockerad till ADR 0005 (go-to-market) + kostnadsskydd-design. Discovery rekommenderas innan stort scope.

**Fas 1-features:** Kolla `docs/steg-tracker.md` § 5 Aktuellt + BUILD.md §18 för icke-blockerade features. **OBS:** Klas-GO krävs för STEG-start (CLAUDE.md §9.2: "Påbörja ny session-fas baserat på 'logiskt nästa steg' kräver explicit GO").

### Väg D — Pausa, vänta nästa idé

Sluta direkt. Markera startprompten som "ej använd" i slutsteg.

---

## Workflow-disciplin (oförändrad)

Per CLAUDE.md §9.2 + §9.6:

1. **Discovery först** — läs befintliga filer + arch-tester som låser ytan.
2. **CTO-invocation** vid multi-approach-val (Variant A/B/C, agent-review-Major-fynd, TD-skapande-validering).
3. **Agent-reviews parallellt** vid relevant scope:
   - Väg A: code-reviewer + dotnet-architect (ADR-claim mot kod)
   - Väg B: security-auditor + code-reviewer (audit-yta + test-coverage)
   - Väg C: enligt feature-scope (security-auditor/dotnet-architect/code-reviewer)
4. **In-block-fix-default per 4h-regel.** Major-fynd från reviews fixas in-block.
5. **STOPP-rapport till Klas** vid relevant scope (CTO-osäkerhet, fas-strategisk transition).
6. **Manuell diff-granskning av Klas** innan push (CLAUDE.md §6.3 punkt 4).
7. **Commit + push efter Klas-GO** (direct-push till `main` per ADR 0019).

---

## Förbud (default — kan lyftas av Klas)

- **INGA Fas 2-JobTech-features** utan ADR 0005-lyft + kostnadsskydd-design
- **INGA STEG-starter** utan Klas-GO
- **INGA ändringar** av `BUILD.md` / `CLAUDE.md` / `DESIGN.md` utan explicit Klas-instruktion
- **INGA deploys** till staging/prod utan Klas-godkännande

---

## Slutsteg (när du är klar med valt arbete)

I sista commit (vilken väg det än blir):

1. **Ta bort denna fil:**
   ```bash
   git rm STARTPROMPT-STATIONAR-2026-05-11.md
   ```
2. Inkludera bortagningen i din session-end-commit (docs-commit per CLAUDE.md §1.5).
3. Producera ny startprompt för **nästa** session per CLAUDE.md §1.5 punkt 5 (om mer arbete väntar).

Om Väg D (pausa direkt utan arbete): commita bortagningen separat med:
```
chore: ta bort använd startprompt (Väg D — paus utan arbete)
```

---

## Snabbreferens — sökvägar

- **Audit-rapport:** `docs/reviews/2026-05-11-arch-audit-discovery.md`
- **Senaste session-logg:** `docs/sessions/2026-05-11-1630-fas2-polish-block.md`
- **Föregående session-logg (audit-discovery):** `docs/sessions/2026-05-11-1215-arch-audit-fas1-discovery.md`
- **ADR-index:** `docs/decisions/README.md`
- **TD-katalog:** `docs/tech-debt.md`
- **STEG-tracker:** `docs/steg-tracker.md` (v1.17)
- **Pre-existing infra:** dev-stack på `https://dev.jobbpilot.se/api/ready`, tag `v0.1.2-dev` på SHA `7cde3c7`
