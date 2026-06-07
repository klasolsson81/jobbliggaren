# Code-review: GitHub Security Hardening — CodeQL SAST (PR `chore/gh-security-hardening`)

**Agent:** code-reviewer (agentId `a88d1ad3529feaafa`)
**Status:** ✓ APPROVED
**Granskat:** 2026-06-07
**Auktoritet:** CLAUDE.md §2.5 (observe-only-ratchet-disciplin), §6 (CI-konventioner), §9.6 (in-block-fix vs TD), ADR 0045 (observe→gate-precedens), ADR 0065 (PR-gate)
**Scope:** CI/IaC — `.github/workflows/codeql.yml` (ny), `.github/codeql/codeql-config.yml` (ny), `docs/tech-debt.md` (ren docs)

Inga Blockers, inga Major, inga Minor.

## YAML-/CodeQL-korrekthet (verifierad)

- **Steg-ordning korrekt.** Manual build-mode kräver bygget MELLAN `codeql-action/init` och `analyze` — det gör det (init → build → analyze). Enda ordning som inte failar CodeQL i manual-läge.
- **Matrix-guards korrekta.** `if: matrix.language == 'csharp'` på setup-dotnet, Cache NuGet och build. JS/TS-cellen hoppar dessa, kör `build-mode: none`.
- **`fail-fast: false`** rätt för observe-only (oberoende språk-signal).
- **`continue-on-error: true`** på jobb-nivå, korrekt placerad.
- **Concurrency-block** följer build.yml-mönstret (`codeql-${{ github.ref }}`).
- **Permissions** minimala/korrekta.

## Determinism/paritet med build.yml (verifierad)

- **global.json-pinning korrekt + kommentaren stämmer.** codeql.yml-kommentaren säger `latestFeature` — matchar global.json verbatim. (build.yml rad 34 hade `latestPatch`-fel — **åtgärdat in-block i denna PR** per §9.6, samma docs-matchar-verkligheten-hygien.)
- **Build-recept identiskt:** `dotnet restore JobbPilot.sln` + `dotnet build JobbPilot.sln --no-restore -c Release`.
- **NuGet-cache-nyckel identisk** med build.yml (varm cache, ingen divergens).

## Observe-only-korrekthet (verifierad)

- Jobbet ligger **inte** i `ci.needs` (oförändrat `[backend, frontend, coverage]`) → kan aldrig blockera merge under ADR 0065. Speglar lighthouse/loadtest/audit.
- Flip→blockerande dokumenterat som medveten Klas-GO-ratchet, konsekvent med ADR 0045.

## codeql-config.yml (verifierad)

- Giltig struktur (`name` + `paths-ignore`). Värden rimliga (`.next`, `coverage`, `playwright-report`, `test-results` — genererade artefakter). Kommentaren korrekt om att node_modules auto-exkluderas + paths-ignore bara gäller tolkade språk.

## §9.6 self-veta (in-block-fix vs TD)

- **DRY-dubblering av build-receptet** i två workflow-filer: lyfts ej. CTO-dom `af8997b2f5987e1ee` accepterade trade-offen (composite-action = framtida YAGNI). Ingen TD.
- **docs/tech-debt.md** ren docs, korrekt utförd. Inga §9.7-regler brutna (inget nytt TD-ID, ingen arkiv-flytt, bara formuleringskorrigering).
- **CLAUDE.md §11.3-spec-edit** ingår korrekt INTE i denna review — klassificerar-blockad, väntar Klas approve-hook, kommer i samma PR efter GO.

## Bra gjort

- Manual build-mode väl motiverat inline (Mediator.SourceGenerator → auth-pipeline-täckning). CTO-resonemang bevarat där läsare hittar det.
- Kommentar-stil följer build.yml-konventionen. Cron på udda minut med motivering.

## Dom

**APPROVED. 0 Blockers, 0 Major, 0 Minor.** Paritetsbunden mot grön CI, observe-only-disciplinen intakt, docs-korrigeringen ren. Mergeklar (CLAUDE.md §11.3-spec-edit tillkommer separat i samma PR efter Klas approve-hook).
