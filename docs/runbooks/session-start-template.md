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

Default: CC kör non-stop med PR-rapport efter varje push. CTO-rond avgör
multi-approach. Klas-STOPP endast vid: ADR-amendments, prod-deploys,
BUILD.md/CLAUDE.md/DESIGN.md-ändringar.
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
- **Non-stop arbete, STOPP bara efter PR** (memory `feedback_nonstop_with_pr_reports`)
- **Aldrig ta bort auto-skapade filer utan GO** (memory `feedback_dont_delete_auto_files`)
```

### 9. Förbud

```
## Förbud

- INGA prod-deploys/applies utan Klas-GO
- INGA BUILD.md/CLAUDE.md/DESIGN.md-ändringar utan explicit instruktion
- INGA tag-pushes utan Klas-GO
- INGA infra-config-ändringar (Terraform ALB-timeout, IAM, etc.) utan Klas-GO
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
- Tag `v{version}-dev` LIVE på dev (om deploy-batch)
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

- [ ] HEAD-SHA stämmer med senaste push (`git log --oneline -3` verifierad)
- [ ] Mandatory reads pekar på filer som faktiskt finns
- [ ] Memory-listan är aktuell (`grep -l "metadata: type: feedback" memory/`)
- [ ] CTO/architect/reviewer-disciplin är listad
- [ ] Memory-direktiv från Klas är inkluderade
- [ ] Discovery-targets är specificerade vid extern fakta-behov
- [ ] Förväntat sluttillstånd är konkret + verifierbart
- [ ] Pending operativt-listan är updaterad mot current-work.md
- [ ] Klas-STOPP-flaggor är specifika för uppgiften, inte generiska

---

## Versionshistorik

- **2026-05-13:** Skapad efter Klas-direktiv att standardisera startprompter
  + lyfta workflow till CLAUDE.md §1.5. Trigger: glömt CTO-invocation i
  F2-P8c-startprompten.
