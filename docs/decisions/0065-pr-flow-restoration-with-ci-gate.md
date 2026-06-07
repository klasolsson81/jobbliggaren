# ADR 0065 — PR-flöde återinfört med CI-gate (superseder ADR 0019)

**Datum:** 2026-05-25
**Status:** Accepted
**Kontext:** Klas-direktiv 2026-05-25 — Pre-launch-disciplin
**Beslutsfattare:** Klas Olsson
**Amendment 2026-06-07:** Automerge-default för CC:s egna PR:er — se [§Amendment 2026-06-07](#amendment-2026-06-07--automerge-default-för-ccs-egna-prer).
**Superseder:** ADR 0019 (Solo direct-push till main, 2026-05-07)
**Amends:** ADR 0007 (Branch protection för main i Fas 0, 2026-04-18) — Fas 0-protectionprofilen utökas till PR-gate-profil när CI-aggregatet `ci` finns på plats; ADR 0007 force-push- och deletion-skydd består.
**Relaterad:** ADR 0019 §"Trigger för återgång till PR-flöde", `.github/workflows/build.yml` (`ci`-aggregat-job)

## Kontext

ADR 0019 (2026-05-07) etablerade direct-push till `main` som permanent praxis under skälen:

- Chat-granskning (Klas + webb-Claude) är primär granskningstrail
- STOPP-disciplin, agent-invocation och manuell diff-granskning utgör de faktiska spärrarna
- PR-mekaniken bidrog till state-divergens-bug i PR #2 utan motsvarande granskningsvärde
- CI existerade inte ännu — PR-gate på CI var inte möjligt

ADR 0019 §"Trigger för återgång till PR-flöde" namngav tre triggers:

1. Bidragsgivare tillkommer
2. Lärar-krav
3. Disciplin-regression (CC bypassar STOPP 2 gånger i rad)

Sedan ADR 0019 har två premisser ändrats:

**1. CI-aggregat-jobbet `ci` finns på plats.** `.github/workflows/build.yml` rad 419–433 definierar en aggregat-status-check `ci` med `needs: [backend, frontend, coverage]` (orkestrerad via `if: always()` + explicit verify-steg). Workflowens egen kommentar (rad 412–418): *"Gör branch-protection-rules enkla att konfigurera (bara `ci` som required check istället för en check per matrix-cell)."* CI-gating är därmed inte längre en framtida fas — det är en aktuell möjlighet.

**2. JobbPilot närmar sig launch.** Sluten beta-utrullning, väntelista-flöde, första riktiga användare. Kvalitets-spärrar som "STOPP + manuell diff" har räckt under solo-fas men kommer behöva CI-evidence och PR-tråden som granskningstrail när:

- riktiga användare påverkas av regressioner
- post-launch bug-triage behöver per-ändring-attribution (PR-tråd är starkare än chat-history)
- ev. extern reviewer (säkerhetsaudit, lärar-bedömning, framtida bidragsgivare) behöver granskningsspår oberoende av Claude-historik

Klas konstaterade 2026-05-25 att det "nu är läge" — pre-launch-disciplin före launch-pressen, inte efter en incident. Detta är en proaktiv ratchet, inte en reaktiv återgång.

## Beslut

JobbPilot återgår till **PR-baserat flöde mot `main`** med följande spärrar (GitHub classic branch protection):

### Protection-konfiguration (`/repos/klasolsson81/jobbpilot/branches/main/protection`)

```json
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["ci"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 0,
    "dismiss_stale_reviews": false,
    "require_code_owner_reviews": false,
    "require_last_push_approval": false
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true
}
```

**Innebär:**

- **PR krävs.** Inga direct-pushes till `main` — alla ändringar går via feature-branch + PR.
- **CI-pass krävs.** `ci`-aggregatet (backend + frontend + coverage) måste vara grönt innan merge. Lighthouse/loadtest/audit förblir observe-only (continue-on-error per ADR 0045 Beslut 5).
- **Up-to-date branch krävs (`strict: true`).** PR-branch måste rebasas/mergas mot senaste `main` innan merge — CI-pass mäts mot mergad state, inte stale branch.
- **0 approving reviews krävs.** Solo-projekt — Klas kan inte approva sin egen PR. Spärren är CI + PR-tråd, inte review-godkännande. När bidragsgivare tillkommer höjs approvals till ≥1 via ADR-amendment.
- **Linear history krävs.** Inga merge-commits — squash eller rebase. Conventional Commits-historia på `main` förblir ren.
- **Force-push och radering blockerade.** Historik-skydd på `main` (ADR 0007-väg, fortsätter).
- **Required conversation resolution.** Alla PR-kommentartrådar måste vara resolved innan merge — för agent-review-trådar (security-auditor, code-reviewer) krävs explicit avslut.
- **Admin enforce (`enforce_admins: true`).** Klas själv måste också gå via PR + CI-pass. Mastercard-disciplin: ingen bypass av regeln man satt själv. Om akut hotfix kräver bypass: toggla `enforce_admins: false` tillfälligt (dokumenterat i incident-log), gör fixen, toggla tillbaka.

### Operativt flöde

1. **Feature-branch** skapas från senaste `main`: `<type>/<short-slug>` (t.ex. `fix/laptop-demo-audit`, `feat/byok-onboarding`). Conventional Commits-prefix matchar commit-typ.
2. **Commits enligt CLAUDE.md §6.2** (Conventional Commits, svenska eller engelska konsekvent per PR).
3. **Push feature-branch** till origin. Pre-push-hooks (gitleaks, dotnet format, lint-staged) körs som tidigare.
4. **PR-skapande** via `gh pr create`. PR-titel = svensk eller engelsk imperativ form, max 70 tecken. Body innehåller Summary + Test plan + agent-review-resultat (inline från STOPP-rapport).
5. **CI körs automatiskt** mot PR (build.yml `on.pull_request.branches: [main]`).
6. **Klas reviewar diff + agent-reports + CI-resultat** i PR-vyn.
7. **Merge:**
   - **Squash-merge** för feature-PRs (default — håller `main` ren)
   - **Rebase-merge** för triviala docs/chore/config-PRs där per-commit-historik tillför värde
   - Aldrig merge-commit (linear history enforced)
8. **Branch-cleanup:** GitHub raderar feature-branch efter merge (default-konfig). Lokal cleanup via `git branch -d`.

### Tag-baserad deploy bevaras

Deploy-flödet via taggar (`v*-dev` → dev, `v*-rc*` → staging, `v*` → prod) per ADR 0019/0004 är **oförändrat**. Taggar skapas på `main` efter merge, inte på feature-branches.

## Konsekvenser

**Positivt:**

- **CI är gate**, inte rekommendation. Coverage-regression (ADR 0044) + arch-tests + frontend-tests måste passera innan kod hittar `main`.
- **PR-tråd som granskningstrail.** Agent-reports (security-auditor, code-reviewer, design-reviewer) bifogas PR-body — granskningstrailen finns kvar oberoende av chat-history.
- **Linear history** bevarar Conventional Commits-disciplinen på `main`.
- **Force-push + radering blockerade** även för admin — historik-säkerhet höjd.
- **Required conversation resolution** tvingar explicit avslut på agent-trådar, eliminerar "implicit acceptance"-risken som chat-granskning hade.
- **Pre-launch-säkring.** PR-spår finns för framtida bug-triage och ev. extern reviewer.

**Negativt:**

- **Per-PR-overhead.** Solo-utveckling tar lite längre — feature-branch + push + PR + vänta-CI + merge i stället för direct-push.
- **CI-flakiness blir blockerande.** Om CI har transient fail blir merge blockerad tills omkörd. Mitigering: workflow concurrency cancel-in-progress redan på plats; flaky tester ska tas på allvar och åtgärdas, inte ignoreras.
- **Klas måste leva med samma spärrar som CC.** Ingen admin-bypass-vana — om en spärr blir hindrande är rätt fix att ändra spärren (ADR-amendment), inte att kringgå den.
- **Branch-state-tracking återkommer.** Risken som ADR 0019 §Kontext (1) flaggade — divergens mellan lokal feature-branch och remote — är åter relevant. Mitigering: CC kör alltid `git fetch origin main && git status` vid sessionsstart, och feature-branches raderas efter merge (`gh pr merge --delete-branch`).
- **Squash-merge skapar två commits per fix** (samma som ADR 0019 §Kontext (3) flaggade) — den lokala feature-commiten + GitHub-side squash-commiten. Acceptabelt pris för CI-gate och PR-trail.

## Alternativ övervägda

**Alt 1 — Behåll ADR 0019 (direct-push).** Avvisat. CI finns nu, pre-launch-tröskel passerad, kvalitets-spärrar via PR-tråd har högre värde än per-PR-overhead.

**Alt 2 — Rulesets i stället för classic branch protection.** Övervägt. GitHub Rulesets är nyare och stöder bypass-listor, conditional rules, m.m. Avvisat för denna ADR: classic är väl beprövat, Klas-direktiv 2026-05-25 explicit ("classic"), färre rörliga delar, samma effektiva spärrar för vårt scope. Migration till rulesets är låg-risk om/när vi behöver conditional rules — ADR-amendment vid behov.

**Alt 3 — `required_approving_review_count: 1`.** Avvisat för solo-fas. GitHub blockerar self-approval; en review-krav skulle tvinga admin-bypass varje PR, vilket urvattnar `enforce_admins: true`-disciplinen. När bidragsgivare tillkommer höjs counten via ADR-amendment.

**Alt 4 — `enforce_admins: false`** (Klas kan bypassa). Avvisat. Spärr som inte gäller dess-författare är teater. Mastercard-disciplin per CLAUDE.md §1.

**Alt 5 — Lägga till `lighthouse`/`loadtest`/`audit` i `required_status_checks.contexts`.** Avvisat. Per ADR 0045 Beslut 5 är dessa observe-only Fas 1. Flip→blockerande sker via separat Klas-GO-ratchet (ADR 0045 Beslut 6), inte som sido-effekt av PR-flow-restoration.

## Trigger för omvärdering

Detta beslut omvärderas (ny ADR som superseder) vid något av följande:

1. **Bidragsgivare tillkommer** → `required_approving_review_count` höjs till ≥1, ev. CODEOWNERS aktiveras.
2. **CI-tider blir hindrande** (median PR-vänt > 15 min) → överväg parallellisering eller subset-gate per touch-yta.
3. **PR-overhead bevisat skadlig** (definierat som: per-PR-overhead > granskningsvärdet under 4 veckor i följd, dokumenterat med konkreta incidenter) → omvärdera mot lättviktigt direct-push-mönster med starkare lokala spärrar.

Vid trigger: ny ADR skapas som superseder denna. ADR 0019 kan inte återupplivas — ny ADR med uppdaterade premisser krävs.

## Amendment 2026-06-07 — Automerge-default för CC:s egna PR:er

**Beslutsfattare:** Klas Olsson. **Kontext:** Klas-direktiv 2026-06-07 efter att automerge-infrastrukturen (`label-automerge.yml`, PR #18) etablerats men inte använts.

Grindmekanismen i Beslut §"Operativt flöde" steg 6 (**"Klas reviewar diff + agent-reports + CI-resultat i PR-vyn"** *innan* merge) var i originalbeslutet en **pre-merge**-spärr. Detta amendment flyttar den till **post-merge** för Claude Codes egna PR:er:

- **CC sätter `automerge`-labeln på alla egna PR:er** direkt efter `gh pr create` (`gh pr edit <nr> --add-label automerge`). `label-automerge.yml` aktiverar då auto-merge (squash) som verkställs så snart required `ci` är grön. Klas läser diffen **efter** merge istället för före.
- **Motiv:** maximalt tempo i solo-fasen. Klas valde "auto på alla egna PR:er" framför "bara när jag säger till" (strikt original-default) och "auto på låg-risk". Kvaliteten bärs av de spärrar som förblir **pre-merge**: agent-invocation (#3), CI-gate (#5), pre-push hooks (#6), required conversation resolution och `enforce_admins`. Bara den mänskliga diff-läsningen (#4) blir post-merge.

**Oförändrat:** Allt annat i ADR 0065 består. Required `ci`-aggregatet, linear history, force/deletion-skydd, `enforce_admins: true` och required conversation resolution gäller precis som förut — automerge verkställs *genom* grindarna, inte runt dem.

**Undantag där CC INTE auto-mergar (STOPP till Klas först):**

1. **Ej-åtgärdat agent-Blocker/Major** — om security-auditor/code-reviewer/CTO lämnar ett ej-fixat Blocker eller Major, sätts ingen label förrän Klas tagit ställning.
2. **Spec-edits (BUILD/CLAUDE/DESIGN)** — själva editen kräver fortfarande `approve-spec-edit.sh`/Klas-GO; men PR:n som bär en godkänd spec-edit får automerge-labeln som vanligt.
3. **Klas säger explicit annat** för en specifik PR.

**Trigger för återgång (pre-merge-review återinförs):** om en regression som en pre-merge diff-läsning hade fångat når `main` via automerge, eller om Klas bedömer post-merge-granskningen otillräcklig → detta amendment rivs (label-default tas bort; #4 återgår till pre-merge). Dokumenteras då i ny amendment.

**Berörda dokument (uppdaterade i samma PR som detta amendment):** CLAUDE.md §6.3 mekanism #4 + §9.1 steg 8.

## Relation till andra beslut

- **ADR 0019 (Solo direct-push till main):** **Superseded av denna ADR.**
- **ADR 0007 (Branch protection för main i Fas 0):** **Amended.** Fas 0:s B-nivå-profil (force-push + deletion blockerade) består — denna ADR utökar med required_status_checks (`ci`), required_pull_request_reviews (0 approvals), enforce_admins, required_linear_history och required_conversation_resolution. ADR 0007 är inte superseded — dess Fas 0-grunder lever vidare; protectionprofilen växer.
- **ADR 0044 (Test-coverage-policy) + ADR 0045 (Performance-budgetar):** Oförändrade. Coverage-gate är redan en del av `ci`-aggregatet; observe-only-jobben (lighthouse/loadtest/audit) förblir utanför `ci.needs` per Beslut 5.
- **CLAUDE.md §6.1, §6.3, §9.1:** Måste uppdateras parallellt — kräver explicit Klas-instruktion enligt CC-gränser (§9.2). Föreslagna edits i separat STOPP-rapport.
- **BUILD.md §15.3 (CI/CD-strategi) + §15.4 (om finns):** Verifieras för konsistens — om text beskriver direct-push måste omformuleras till PR-flow.

## Implementationsstatus

**Aktiv från:** 2026-05-25 (denna ADR:s acceptans + GitHub protection-API PUT-anrop verifierad samma datum).

**Verifierad konfiguration (gh api GET `repos/klasolsson81/jobbpilot/branches/main/protection`):**

```
required_status_checks.strict: true
required_status_checks.contexts: ["ci"]
required_pull_request_reviews.required_approving_review_count: 0
enforce_admins.enabled: true
required_linear_history.enabled: true
required_conversation_resolution.enabled: true
allow_force_pushes: false
allow_deletions: false
```

**Sista direct-push:** `ee87f14` (2026-05-25, audit-fixar Medel-4 + Medel-7 inför laptop-demo). Denna commit gick direct under ADR 0019 strax innan protection aktiverades — sista commit utan PR.

**Första PR-cykeln under denna ADR:** öppnas vid nästa förändring efter ADR 0065-mergen.
