---
session: gh-security-hardening (CodeQL code-scanning + §11.3/TD-101 doc-korrigering)
datum: 2026-06-07
slug: gh-security-hardening-codeql
status: PR öppen — väntar Klas approve-spec-edit.sh (§11.3) + ci grönt
bas-HEAD: 54b5da1
branch: chore/gh-security-hardening
commits:
  - f409e56 ci(security) CodeQL code-scanning observe-only + build.yml-kommentarsfix
  - 689adae docs(tech-debt) korrigera TD-101/TD-104 Serilog/Seq-formulering
  - (docs-sync) current-work + session-log
agenter:
  - senior-cto-advisor af8997b2f5987e1ee (build-mode-dom)
  - security-auditor a2e68a0122b279f3a (PASS)
  - code-reviewer a88d1ad3529feaafa (APPROVED)
---

# Session: GH-säkerhetshärdning — CodeQL code-scanning + doc-korrigering

## Mål

Liten, hög-värde, låg-risk hygien-PR (`chore/gh-security-hardening`):

1. Lägga CodeQL code-scanning (SAST) som **observe-only** — C#/.NET 10 +
   JS/TS/Next 16. Ska INTE blockera required `ci` (samma ratchet-disciplin som
   ADR 0045 fitness-functions).
2. Korrigera CLAUDE.md §11.3 + TD-101 "Serilog/Seq"-formulering mot
   verkligheten (ingen Serilog finns — appen loggar default till console via
   Microsoft.Extensions.Logging; Seq-containern kör men ingen sink wirar dit).
3. Docs-sync i samma PR.

## Vad som levererades

### CodeQL (`f409e56`)

- `.github/workflows/codeql.yml` — matrix över `csharp` (build-mode manual) +
  `javascript-typescript` (build-mode none). Triggers: push main, PR main,
  veckovis cron (`23 4 * * 1`), workflow_dispatch. `permissions:`
  security-events:write + contents:read + actions:read (job-scoped least-privilege).
  `continue-on-error: true` + utanför `ci.needs` → observe-only. codeql-action@v4.
- `.github/codeql/codeql-config.yml` — paths-ignore för `.next`/coverage/
  playwright-report/test-results (node_modules auto-exkluderas, listas ej).
- In-block: `build.yml`-kommentar `latestPatch`→`latestFeature` (matchade ej
  global.json; code-reviewer-fynd, samma docs-matchar-verkligheten-hygien).

### Doc-korrigeringar (`689adae`)

- TD-101: "ConsoleEmailSender (loggar till Serilog/Seq)" → "loggar till console
  via Microsoft.Extensions.Logging — ingen Serilog/Seq-sink finns wirad, se TD-104".
- TD-104 punkt 3 markerad delvis adresserad (TD-101-formulering fixad här;
  CLAUDE.md §11.3-spec-edit kvarstår).
- Agent-rapporter i `docs/reviews/2026-06-07-gh-security-hardening-*.md`.

## Beslut & detourer

- **Build-mode för C# (multi-approach → CTO).** senior-cto-advisor
  (`af8997b2f5987e1ee`) valde **Variant C — `build-mode: manual`** över A (none)
  och B (autobuild). Avgörande on-disk-fakta: `Mediator.SourceGenerator` är
  registrerad som `OutputItemType="Analyzer"` i Api + Worker och emitterar
  CQRS-dispatch/DI/auth-pipeline-kod endast vid kompilering. `build-mode: none`
  skulle lämna just auktoriseringsytan osynlig för SAST. Manual återbrukar
  build.yml:s bevisade recept → autobuilds täckning utan dess heuristik/
  flakiness-risk (ADR 0045-doktrin: "flaky gate sämre än ingen gate"). CC gav
  ingen egen rekommendation (§9.6 + memory `feedback_cto_decides_multi_approach`).
- **Ingen ny ADR.** CTO-dom: CodeQL observe-only är en ny instans av ADR 0045:s
  redan accepterade mönster, inte ett nytt arkitekturbeslut. Trigger för framtida
  ADR = flip observe-only→blockerande.
- **CC gick direkt till impl** efter CTO-beslut (entydigt motiverat, scope redan
  låst observe-only av Klas) — ingen separat Klas-GO behövdes (§9.6 punkt 5).
- **Web-search (§9.5):** verifierade codeql-action **v4** aktuell major (Node 24;
  v3 deprekeras dec 2026), bundle 2.25.6, C# 14-stöd (CodeQL 2.25.4), build-mode-
  alternativ för compiled languages. Källor: github.blog/changelog +
  docs.github.com/code-security.

## Operativ detour — parallell-build-lås

Pre-commit-hooken bygger hela .NET-lösningen. De **körande** lokala Api+Worker
(via `dotnet run`-parent-processer som äger .exe-barnen) höll lås på Domain.dll/
Application.dll/Infrastructure.dll → MSB3026/3027 build-copy-fel. Exakt det
Förkrav 4 varnar för. Tester passerade (Domain 422, Application 624) — rent
fil-lås, ingen kodfix. Första kill av enbart .exe-barnen räckte inte (parent
`dotnet run` respawnade). Lösning: döda parent + barn tillsammans, committa,
**starta om Api+Worker efteråt** (pending, se nedan).

## Reviews

- **security-auditor** `a2e68a0122b279f3a`: **PASS** — 0 Block / 0 Major /
  1 Minor (floating action-pinning = repo-brett policyval, ej denna PR, ej TD;
  self-vetad). Permissions least-privilege bekräftade, ingen script-injection-yta,
  `pull_request` (ej `pull_request_target`) korrekt, ingen GDPR-konsekvens.
- **code-reviewer** `a88d1ad3529feaafa`: **APPROVED** — 0/0/0. Steg-ordning
  (build mellan init/analyze) + matrix-guards + determinism-paritet verifierade.

## Pending / nästa session

1. **Klas:** `bash .claude/hooks/approve-spec-edit.sh` för CLAUDE.md §11.3, el.
   explicit GO att CC kör i Bypass. Verbatim-korrigering i STOPP-rapporten.
   Läggs i SAMMA PR.
2. **Starta om lokala Api+Worker** (`dotnet run --project src/JobbPilot.Api
   --launch-profile http` → vänta build → `dotnet run --project src/JobbPilot.Worker`).
3. Merge PR efter `ci` grönt + §11.3 applicerad (auto-merge via `automerge`-label
   möjlig).
4. Verifiera CodeQL-körning grön + syns som egen check (ej i required ci) +
   alerts i Security-fliken.
