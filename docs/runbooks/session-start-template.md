# Session-start-template

Strukturell guide för start-prompter till nya Claude Code-sessioner i JobbPilot.

**Hur den används:** Vid session-end genererar CC en ny startprompt enligt
denna struktur, anpassad för nästa-session-uppgift, och levererar den som ett
copy-paste-block i chatten — **aldrig** som ny fil i repot (håller repot rent).

**Antagande:** varje startprompt körs i en helt ren `/clear`-session utan
tidigare kontext. Måste vara self-contained.

---

## Struktur — obligatoriska sektioner

### 1. Hälsning + uppgift one-liner

```
Hej. Klas-prompt: {fas/scope-namn} — {kort beskrivning av leveransmål}.
```

### 2. Förkrav (pre-flight)

```
## Förkrav

1. `git pull origin main`
2. Verifiera HEAD = `{förväntat-sha}` via `git log --oneline -10`
   Förväntat senaste rad: `{senaste commit-meddelande-snippet}`
3. `git status` clean (eller bara specifika ignored-filer)
4. (Om relevant) AWS dev live: `curl -sI https://dev.jobbpilot.se/api/ready` → HTTP 200
5. (Om relevant) AWS SSO aktiv: `aws sts get-caller-identity --profile jobbpilot`
6. Lokala krav: .NET 10 SDK ({version}), Node 22 + pnpm, Docker, AWS CLI
```

### 3. Mandatory reads (CLAUDE.md §1.5)

Lista konkreta filer + specifika sektioner. Inte generisk.

```
## Mandatory reads

1. **CLAUDE.md** — hela. Särskilt §{relevanta-sektioner}
2. **BUILD.md** §{relevanta-sektioner}
3. **docs/current-work.md** — senaste status
4. **docs/sessions/{senaste-session-log}.md** — föregående session
5. **docs/decisions/{relevanta-ADR-filer}.md** — med ev. amendments
6. **docs/tech-debt.md** — särskilt TD-{nummer} ({titel})
7. **docs/runbooks/{relevant}.md** — om uppgiften kräver runbook
```

### 4. Memory att läsa

Lista relevanta memory-filer för uppgiften, inte bara "MEMORY.md".

```
## Memory att läsa

Hela `MEMORY.md` + särskilt:
- `feedback_nonstop_with_pr_reports` — STOPP bara efter varje PR
- `feedback_cto_decides_multi_approach` — CTO vid multi-approach
- `feedback_td_lifting_discipline` — pressa TD mot §9.6
- `feedback_di_with_handlers_same_commit` — DI + handlers i samma commit
- `feedback_dont_delete_auto_files` — aldrig auto-skapade filer utan GO
- {andra relevanta memorys för uppgiften}
```

### 5. Uppdrag

Detaljerad scope med numrerade punkter. Specificera filer som ska skapas/ändras
om de är kända.

```
## Uppdrag: {fas-namn}

Per {ADR-referens} {scope-spec}:

1. {Punkt 1 — konkret leverans, ev. fil-pointer}
2. {Punkt 2 — konkret leverans}
...
```

### 6. Discovery / web-search

**Kritisk sektion** per Klas-direktiv. Om uppgiften kräver verifiering av
externa fakta (AWS-features, package-versioner, API-specs, framework-syntax),
specificera **vad** som ska sökas och **varför**. Per CLAUDE.md §9.5:
externa fakta uppdateras konstant → web-search > gissning från training-data.

```
## Discovery / web-search-targets

Verifiera följande innan implementation (CLAUDE.md §9.5):

- {Vad}: {Varför} — sök efter "{konkret query}"
- {AWS-feature X}: är den GA i eu-north-1? — `https://aws.amazon.com/...`
- {Package Y@version}: senaste stabila? CVE-status? — search `nuget.org` + `github.com/advisories`
- {API Z}: senaste spec-version + breaking changes
```

Om uppgiften är ren intern (refactor av existerande kod, ingen extern dep),
skriv tydligt: "Ingen extern discovery krävs — uppgiften är ren intern."

### 7. Klas-STOPP-flaggor

Default: minimera STOPP per Klas-direktiv 2026-05-13 ("håll det så automatiserat
som möjligt, fråga mig endast när det måste, eller vid stort beslut").

```
## Klas-STOPP-flaggor

- {Vad triggar Klas-input — t.ex. ADR-amendment, fas-skifte, deploy-beslut}
- {ev. flaggor specifika för uppgiften}

Default: CC kör non-stop med STOPP-rapport efter varje PR-push (PR-länk i
rapporten). CTO-rond avgör multi-approach. Klas-STOPP endast vid:
ADR-amendments, prod-deploys, BUILD.md/CLAUDE.md/DESIGN.md-ändringar (kräver
även `bash .claude/hooks/approve-spec-edit.sh`).
```

### 8. Disciplin-påminnelser (kritiskt)

```
## Disciplin (från memory + CLAUDE.md)

- **Agenter INLINE** per CLAUDE.md §9.2 — inte post-hoc:
  - `dotnet-architect` INNAN kod (design-skiss för {specifika punkter})
  - `senior-cto-advisor` vid multi-approach (memory `feedback_cto_decides_multi_approach`)
  - `code-reviewer + security-auditor` INNAN commit ({varför säkerhetskänsligt})
  - `db-migration-writer` om ny EF-migration
  - `test-writer` för nya domain-typer/handlers/jobs
  - `docs-keeper` vid session-end

- **DI-registrering i samma commit som handlers** (memory `feedback_di_with_handlers_same_commit`)
- **Multi-approach → CTO INNAN egen rekommendation** (memory `feedback_cto_decides_multi_approach`)
- **TD-lyftningar pressas mot §9.6** (memory `feedback_td_lifting_discipline`)
- **Lyft inga nya TDs som kan fixas direkt** (Klas-direktiv 2026-05-13)
- **Non-stop arbete, STOPP bara efter PR-push** (memory `feedback_nonstop_with_pr_reports`)
- **Aldrig ta bort auto-skapade filer utan GO** (memory `feedback_dont_delete_auto_files`)

### PR-flöde per ADR 0065 (mandatory 2026-05-25)

- **Feature-branch obligatorisk:** `<type>/<short-slug>` (t.ex. `fix/laptop-demo-audit`, `feat/byok-onboarding`). Type-prefixet matchar Conventional Commits-typen.
- **Inga direct-pushes till `main`.** Classic branch protection blockerar — `enforce_admins: true` gäller även Klas.
- **PR krävs** med `ci`-aggregatet grönt (backend+frontend+coverage). Lighthouse/loadtest/audit är observe-only (ADR 0045 Beslut 5).
- **Docs-sync ingår i SAMMA PR som scope/issue** (Klas-direktiv 2026-05-25). Skapa inte "docs only"-uppföljnings-PR.
- **Squash- eller rebase-merge** — linear history krävs, inga merge-commits.
- **Required conversation resolution:** alla agent-trådar i PR-body måste vara explicit avslutade innan merge.
- **STOPP-rapport efter PR-push** innehåller PR-URL (`gh pr create` output) + agent-report-summering + CI-länk.

### Parallell-CC-disciplin (intjänad 2026-05-17 — tre process-glidningar i en parallell körning)

- **Worktree-per-parallell-CC obligatoriskt.** Körs flera CC:er samtidigt: varje CC i egen `git worktree` (ej delat working tree). Bevisat: delat träd → CC A `git commit -a` svepte CC B:s ostagade Resume-fix in i fel commit (62c9dc7); isolerad worktree (CC C) hade noll kontaminering. (memory `project_parallel_cc_worktree_isolation`)
- **`git commit -- <explicita paths>` enda tillåtna form.** `git commit -a` och pathspec-lös `git commit` är **förbjudet** så länge någon parallell CC är aktiv — det var rotorsaken till cross-CC-svepet. Verifiera alltid med `git show --stat HEAD` efter commit. (memory `feedback_pathspec_commit_parallel_cc`)
- **Sub-agenter får ALDRIG kringgå pre-push-hooks.** Inget `--no-verify`, inget `core.hooksPath=/dev/null`, ingen annan gitleaks/format-bypass. Blockerar en hook: STOPP + rapportera till Klas, kringgå aldrig. (docs-keeper-incident 60f845a; memory `feedback_subagent_hook_bypass_watch`)
- **docs-keeper auto-pushar INTE under öppen incident/öppen Klas-kvittens.** Vid pågående process-incident: docs-synk hålls tills Klas kvitterat.
- **§9.2 spec-edits (BUILD/CLAUDE/DESIGN): människan kör `approve-spec-edit.sh`.** Agenten själv-godkänner ALDRIG en spec-edit och själv-beviljar ALDRIG permission i `.claude/settings.json`. Auto-mode-klassificerarens hård-block av detta är **korrekt säkerhetsbeteende, ej bugg** — försök inte kringgå det; be Klas köra approve-scriptet manuellt. (memory `feedback_spec_edit_approve_classifier_block` — notera: tidigare "false-positive"-framing felaktig, korrigerad)
```

### 9. Förbud

```
## Förbud

- INGA direct-pushes till `main` — alla ändringar via feature-branch + PR (ADR 0065)
- INGA prod-deploys/applies utan Klas-GO
- INGA BUILD.md/CLAUDE.md/DESIGN.md-ändringar utan explicit instruktion (kräver `bash .claude/hooks/approve-spec-edit.sh`)
- INGA tag-pushes utan Klas-GO
- INGA infra-config-ändringar (Terraform ALB-timeout, IAM, etc.) utan Klas-GO
- INGA merge-commits till `main` (linear history enforced — squash/rebase only)
- INGET `git commit -a` / pathspec-lös `git commit` när parallell CC aktiv (cross-CC-svep)
- INGEN sub-agent-bypass av pre-push-hooks (`--no-verify`, `core.hooksPath`)
- INGEN agent-själv-edit av `.claude/settings.json`-permissions / agent-config (klassificerar-hård-block är korrekt)
- INGA separata "docs only"-PRs ovanpå en feature-PR — docs-sync ingår i samma PR som scope
- {ev. uppgifts-specifika förbud}
```

### 10. Pending operativt för Klas

Lista vad som väntar Klas-action sedan föregående session — operativa items
som inte är CC-leverans.

```
## Pending operativt för Klas

- {Vad som behöver Klas-handgrepp utanför CC-scope, t.ex. AWS-konfig, API-key-registrering, frontend-deploy}
```

### 11. Förväntat sluttillstånd

Konkret + verifierbart. Inkluderar tag-version om relevant.

```
## Förväntat sluttillstånd

- {Leverans 1 levererad}
- {Leverans 2 levererad}
- Backend-tester {N}+ gröna
- **PR #{nummer} öppen mot `main`** med `ci`-aggregatet grönt + agent-reports inline (per ADR 0065)
- Docs-sync (current-work.md + session-log + ev. ADR-uppdateringar) committad i samma PR
- Tag `v{version}-dev` LIVE på dev (om deploy-batch — efter Klas merge:r PR)
- {TD-stängningar}
```

### 12. Avslutning

```
Lycka till.
```

---

## Regler för leverans

- **Alltid copy-paste-block** i chatten, aldrig ny fil i repot
- Block i fenced code (` ``` ` eller ` ```text `) så Klas kan kopiera helt
- Self-contained — antar `/clear`, ingen tidigare kontext
- Specifik för uppgiften — inte generisk eller copy-paste-från-template
- Faktiska värden — inte placeholders: SHA:n från senaste commit,
  versions-nummer, datum, fil-paths
- Discovery-sektion ska vara konkret om uppgiften kräver det

---

## CC-checklist innan startprompt levereras

- [ ] HEAD-SHA stämmer med senaste merged PR (`git log --oneline -3` verifierad)
- [ ] Mandatory reads pekar på filer som faktiskt finns
- [ ] Memory-listan är aktuell (`grep -l "metadata: type: feedback" memory/`)
- [ ] CTO/architect/reviewer-disciplin är listad
- [ ] Memory-direktiv från Klas är inkluderade
- [ ] Discovery-targets är specificerade vid extern fakta-behov
- [ ] Förväntat sluttillstånd är konkret + verifierbart, **inkluderar PR-leverans**
- [ ] Pending operativt-listan är updaterad mot current-work.md
- [ ] Klas-STOPP-flaggor är specifika för uppgiften, inte generiska
- [ ] PR-flöde per ADR 0065 är reflekterat i §"Disciplin" och §"Förbud"

---

## Versionshistorik

- **2026-05-13:** Skapad efter Klas-direktiv att standardisera startprompter
  + lyfta workflow till CLAUDE.md §1.5. Trigger: glömt CTO-invocation i
  F2-P8c-startprompten.
- **2026-05-25:** Uppdaterad för PR-flöde per ADR 0065. Förändringar:
  - §"Klas-STOPP-flaggor": "PR-rapport efter varje push" → "STOPP-rapport efter varje PR-push (PR-länk i rapporten)"; spec-edit-flagga nämner `approve-spec-edit.sh`
  - §"Disciplin": ny sub-sektion "PR-flöde per ADR 0065 (mandatory 2026-05-25)" med feature-branch, ci-gate, docs-i-samma-PR, linear history, conversation resolution, STOPP-rapport-PR-URL
  - §"Förbud": tillagt "INGA direct-pushes till main", "INGA merge-commits", "INGA separata docs-only-PRs"
  - §"Förväntat sluttillstånd": PR-leveransen är nu en explicit punkt; tag-deploy sker efter Klas-merge
  - §"CC-checklist": HEAD-SHA stämmer mot senaste **merged PR**; ny rad för PR-flöde-reflektion
