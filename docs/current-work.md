# Current work — JobbPilot

**Status:** **EDITOR-BASELINE + DOCS-DRIFT-FIX LEVERERAD 2026-06-10 (branch `chore/editor-baseline`, PR mot main).** Extern idé-triage (Gemini-prompt via Klas) avtäckte spec-drift: CLAUDE.md §11.2 lovade `.editorconfig` + `.vscode/settings.json` + `.vscode/extensions.json` som inte existerade — nu skapade per senior-cto-advisor-dom (Variant B-editorconfig: endast CLAUDE.md §3-spårbara regler på warning; EF-migrations `generated_code = true`). Session-start-templatens AWS-förkrav (döda per ADR 0066) ersatta med lokal-stack-checks. `current-work.md` splittad: historik → `docs/current-work-archive.md` (tech-debt-archive-precedensen 2026-05-11). Graphify avvisad som CLAUDE.md-mandat (pilotbar efter MVP); Cline avvisad (parallell-agent-risk). **Föregående: Platsbanken sök-paritet Fas C2 MERGAD + APPLICERAD (se nedan). Nästa: Fas D1 (facet-counts + typeahead) ELLER Fas E (FE-picker) — Klas-GO.**

**Levererat denna session (editor-baseline-PR):**

- **`.editorconfig` (NY):** whitespace/charset-bas för alla filtyper + C#-stilregler. Warning-severity ENDAST för CLAUDE.md §3-spårbara regler (IDE0161 file-scoped namespace §3.1, IDE0044 readonly §2.2/§3.3, naming: I-prefix interfaces + `_camelCase` private fält §3.2); var/braces/usings-placering = suggestion. `src/JobbPilot.Infrastructure/**/Migrations/*.cs` = `generated_code = true` (EF-scaffold är block-scoped). Async-suffix-naming-regel MEDVETET utelämnad (xUnit-testnamn `<Class>_<Scenario>_<Expected>` är async utan suffix). Verifierad: full rebuild 0 warn/0 err + `dotnet format --verify-no-changes` exit 0.
- **Imports-normalisering (11 filer):** `dotnet_sort_system_directives_first` avtäckte 11 filer med osorterade usings — fixade mekaniskt via `dotnet format` (CTO-villkor 3, trivialt mekaniskt).
- **`.vscode/extensions.json` (NY):** csdevkit, eslint, prettier, tailwindcss, editorconfig, errorlens, gitlens, vscode-containers, claude-code. `unwantedRecommendations`: Cline (`saoudrizwan.claude-dev`) + Copilot ×2 — Claude Code är enda sanktionerade agent-kanalen.
- **`.vscode/settings.json` (NY):** formatOnSave + eslint-fixAll-on-save + per-språk-formatters + `eslint.workingDirectories` — speglar pre-commit-gates som on-save-feedback (Variant B).
- **`docs/runbooks/session-start-template.md`:** AWS-förkrav (dev-curl + SSO + AWS CLI) → Docker Compose-check + lokal `/api/ready`-check (ADR 0066-drift stängd).
- **`docs/current-work-archive.md` (NY):** all historik (C2-pre-merge-blocket och äldre, 863 rader) flyttad hit verbatim, omvänd kronologi dokumenterad i header.
- **Agent-domar:** senior-cto-advisor (4 beslut a–d, `a40d8b4eb06b197fd`) + code-reviewer (se PR-body).
- **PENDING spec-edit (Klas):** CLAUDE.md §1.6-tabellrad för `current-work-archive.md` kräver `approve-spec-edit.sh`.

---

**(Föregående) Status:** **PLATSBANKEN SÖK-PARITET — FAS C2 KLAR: MERGAD (PR #33, squash `481e1f0`) + MIGRATION APPLICERAD MOT DEV-DB + STACK OMSTARTAD 2026-06-10.** SearchCriteria-VO bär OccupationGroup + Municipality (Ssyk helt avvecklad ur sök-identiteten; occupation-name-substratet orört); sparade/recent sökningar filtrerar via filter-SPOT:en (C1:s no-op-fönster stängt); capture-gapet för yrkesgrupp-/kommun-sökningar stängt. Full pre-merge-detalj i `docs/current-work-archive.md` (C2-blocket) + `docs/sessions/`.

**C2 slutläge (verifierat av Klas 2026-06-10):**

- Migration `20260609214512_C2SearchParityReverseLookupAndRecentExpansion` i `__EFMigrationsHistory`; `recent_job_searches`: `ssyk_list` borta, `occupation_group_list` + `municipality_list` (NOT NULL, text[]) på plats, 0 rader (3 cache-rader raderade by design); `saved_searches`: 0 rader med legacy-Ssyk-nyckel (transform ren no-op). Applicerad i single transaction, ON_ERROR_STOP, temp-filer städade.
- Stack omstartad i deploy-ordning (migration före binär): Api 5049 (taxonomi-seeder OK), Worker (Platsbanken-stream-sync: fetched=172, added=56, updated=5, archived=106, errors=0), FE 3000.
- CodeQL main-scan manuellt triggad (run 27239886564) — automerge tystar main-push-workflows (känd mekanik).
- 6 agent-granskningar GO, 6 Minor fixade in-block, 1707 tester gröna över sex sviter, inga TDs, inga nya dependencies, FE orörd.

**Commit-kedja (squash-merge-SHA på main, sök-paritet-bågen):**

| SHA | PR | Beskrivning |
|---|---|---|
| `01a6039` | #29 | Fas A — ADR 0067 + ADR 0043-amendment (design, ingen kod) |
| `154fb07` | #30 | Fas B1 — Klass 1 data-layer (STORED kommun/yrkesgrupp + snapshot v30) |
| `1fd9600` | #31 | Fas B2 — Klass 2 data-layer (STORED anställningsform/omfattning + re-ingest-trigger) |
| `bc54a84` | #32 | Fas C1 — query/filter-layer + yrke-nivåbyte (OccupationGroup) |
| `481e1f0` | #33 | Fas C2 — VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat |
| (denna) | — | chore/editor-baseline — .editorconfig + .vscode + docs-drift-fix |

---

## Pending operativt för Klas

1. **Kör `bash .claude/hooks/approve-spec-edit.sh`** för CLAUDE.md §1.6-raden (`current-work-archive.md` i docs-tabellen) — CC pushar raden till editor-baseline-PR:n efter approve.
2. Granska editor-baseline-PR + C2-PR (#33) post-merge när du vill.
3. **GO för nästa fas:** D1 (facet-counts + typeahead-suggest, NBomber-gate) eller E (FE-picker, planerad sist per ADR 0067). Startprompt genereras vid val.
4. Re-ingest Klass 2 (B2-avslut) opåverkad, valfri timing: `POST /api/v1/admin/job-ads/backfill-klass2`.
5. Graphify-pilot = medvetet deferrad till efter MVP / vid trigger (CTO-triage 2026-06-10); ingen TD lyft (verktygs-experiment, ej skuld).

---

## Historik

All tidigare session-historik (Fas C2 pre-merge och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
