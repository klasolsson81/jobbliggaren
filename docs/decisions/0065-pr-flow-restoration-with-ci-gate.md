# ADR 0065 — PR-flöde återinfört med CI-gate (superseder ADR 0019)

**Datum:** 2026-05-25
**Status:** Accepted
**Kontext:** Klas-direktiv 2026-05-25 — Pre-launch-disciplin
**Beslutsfattare:** Klas Olsson
**Amendment 2026-06-07:** Automerge-default för CC:s egna PR:er — se [§Amendment 2026-06-07](#amendment-2026-06-07--automerge-default-för-ccs-egna-prer).
**Amendment 2026-07-25:** CI triggras base-oberoende (#861) — se [§Amendment 2026-07-25](#amendment-2026-07-25--ci-triggras-base-oberoende-mekanism-drift-861).
**Amendment 2026-07-27:** Tvålabelsgrind (`automerge` = avsikt, `agents-done` = tillstånd) + undantaget för en ren bas-merge (#836) — se [§Amendment 2026-07-27](#amendment-2026-07-27--tvålabelsgrind-och-undantaget-för-en-ren-bas-merge-836).
**Amendment 2026-07-28:** Vuln-grinden får acceptera bara det den inte kan reparera; en override-nyckel grindas bara för att partitionera — se [§Amendment 2026-07-28](#amendment-2026-07-28--the-blocking-vuln-gate-may-accept-a-risk-only-where-repair-is-unavailable).
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

**1. CI-aggregat-jobbet `ci` finns på plats.** *(Ögonblicksbild vid ADR:ns författande 2026-05-25 — rad- och jobbuppräkningen nedan beskriver trädet då, inte nu; den gällande `ci.needs` har ETT hem, `build.yml`.)* `.github/workflows/build.yml` rad 419–433 definierar en aggregat-status-check `ci` med `needs: [backend, frontend, coverage]` (orkestrerad via `if: always()` + explicit verify-steg). Workflowens egen kommentar (rad 412–418): *"Gör branch-protection-rules enkla att konfigurera (bara `ci` som required check istället för en check per matrix-cell)."* CI-gating är därmed inte längre en framtida fas — det är en aktuell möjlighet.

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
5. **CI körs automatiskt** mot PR (build.yml `on.pull_request.branches: [main]`). — *Mekanismen är ändrad: base-filtret är borttaget, se Amendment 2026-07-25 (#861). Originaltexten står kvar som historiskt beslutsunderlag.*
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

## Amendment 2026-07-25 — CI triggras base-oberoende (mekanism-drift, #861)

**Kontext:** #861. Beslut §"Operativt flöde" steg 5 beskrev CI-triggern som `build.yml on.pull_request.branches: [main]`. Det base-filtret är **borttaget** (även ur `codeql.yml` och `e2e.yml`); `push` är fortfarande filtrerad till `main`.

**Varför:** filtret gjorde detta ADR:s egen merge-grind overkställbar för en **stackad PR** — en vars base är en annan feature-branch, vilket är den normala formen när ett issue är förutsättning för nästa. En sådan PR matchade ingen trigger, fick **noll checks**, och "inga checks" går inte att skilja från "grön CI" vid en blick. Merge-regeln i steg 5–7 är "merge på grön `ci`", och ett PR utan `ci`-aggregat ger grinden ingenting att utvärdera. Mätt: en PR mot en feature-branch-bas rapporterar efter ändringen `build` + `codeql` + `e2e` (alla `event=pull_request`).

**Räckvidd — signal, inte enforcement:** branch protection ligger bara på `main` (required checks = exakt `["ci"]`, `strict: true`, `enforce_admins: true`; noll repo-rulesets). En stackad PR har alltså fortfarande inga *required* checks. Det ändras inte här; enforcement-halvan är #836 (PR-babysittaren får inte merga före agent-grinden svarat).

**Oförändrat:** allt annat i ADR 0065 och i amendmentet ovan. Required `ci`, linear history, `enforce_admins`, automerge-flödet och undantagen gäller precis som förut — bara *vilka* PR:er som får en `ci` att vara grön har vidgats.

## Amendment 2026-07-27 — tvålabelsgrind, och undantaget för en ren bas-merge (#836)

**Kontext:** #836. Amendment 2026-06-07 ovan beskriver mekanismen i två meningar: *"CC sätter `automerge`-labeln på alla egna PR:er direkt efter `gh pr create`. `label-automerge.yml` aktiverar då auto-merge (squash) som verkställs så snart required `ci` är grön."* **Den ANDRA meningen är från och med nu falsk** och ersätts här. Den första består oförändrad: `automerge` sätts fortfarande vid `gh pr create` — det är dess BETYDELSE som smalnat, från "merga den här" till enbart avsikt. Beslutet i amendmentet står; bara dess mekanik har bytts.

**Vad som ändrades.** `automerge` bar två betydelser: AVSIKT (sann vid `gh pr create`) och TILLSTÅND (sann först när granskningen är klar). Varje aktör som legitimt uttryckte avsikt beviljade därför ofrivilligt tillstånd — mätt två gånger, PR #832 och PR #1083, båda med oåtgärdade Blocker/Major i `main`. Symbolerna är nu delade:

- **`automerge` = AVSIKT.** Sätts vid `gh pr create`, av CC eller PR-babysittern.
- **`agents-done` = TILLSTÅND.** Sätts ENBART av den ägande sessionen, efter att §9.2:s obligatoriska agenter svarat utan oåtgärdat Blocker/Major.

`label-automerge.yml` armerar först när **båda** sitter. En push som bär eget innehåll tar bort `agents-done` och stänger av auto-merge; en återkallad label gör detsamma.

**Undantaget:** en **ren bas-merge** avväpnar inte. `main` har `strict: true`, så up-to-base är obligatoriskt och konstant; en grind som avväpnade på var och en av dem hade aldrig konvergerat. `.github/scripts/is-pure-base-merge.sh` (fixturtestad, `ci`-grindad) jämför det pushade trädet med det träd en automatisk merge hade gett — vilket är exakt vad `gh pr update-branch` ger — och är fail-closed: varje fel och varje form den inte kan intyga avväpnar.

**Undantag 1 i amendment 2026-06-07 ("ej-åtgärdat agent-Blocker/Major → ingen label") får därmed sitt första maskinuttryck.** Regeln är inte ny; den var overkställbar. **Men dess HANDLING ändras, och undantagets ordalydelse ("sätts ingen label") ska läsas om:** `automerge` sätts som vanligt vid `gh pr create` — det är `agents-done` som hålls inne tills Blockern/Majoren är åtgärdad eller Klas tagit ställning. En session som följer den gamla ordalydelsen bokstavligt håller inne fel label. Undantag 2 (spec-edits) är sedan 2026-06-25 ersatt av det autonoma flödet och lyfts inte här.

**Oförändrat:** required `ci`-aggregatet, linear history, `enforce_admins: true`, required conversation resolution. Automerge verkställs fortfarande *genom* grindarna.

**Berörda dokument (uppdaterade i samma PR):** CLAUDE.md §6/§6.5, `docs/runbooks/parallel-sessions.md` §3.3/§8/§8.1/§9.5, `docs/runbooks/session-start-template.md`, ADR 0045 och ADR 0044 (`ci.needs`-uppräkningen).

## Amendment 2026-07-28 — the blocking vuln gate may accept a risk only where repair is unavailable

*(Written in English per CLAUDE.md §1, which lists ADRs among the artefacts authored
in English. The Swedish body above is not retranslated.)*

**Context.** The blocking supply-chain gate in `.github/workflows/dependabot-automerge.yml`
— ratified by Klas-GO "B" 2026-06-07 against this ADR's *Avvisat* alt. 5 and ADR 0045
Beslut 7 — audits the **whole tree**, not the diff. It therefore fails a PR over
vulnerabilities that PR did not introduce. Measured 2026-07-28: PR #1042 (`next`
16.2.9 → 16.2.11), which alone closes 18 of the repo's 28 Dependabot alerts, sat open
three days with green `ci` and this job red, failed by six packages it does not touch.
All six were **transitive**, and transitive packages never get their own Dependabot PR.
The gate was blocking precisely the PRs that reduce risk.

**The mechanism gained an exception, so the exception needs a written rule.** pnpm's
`auditConfig.ignoreGhsas` makes a suppression cheap, one line, and invisible in effect.
The first draft of #1119 used it on **all seven** advisories, reasoning that "transitive
⇒ no Dependabot PR ⇒ unfixable". That inference is invalid — it establishes only that
Dependabot will not fix them *for* us. Six of the seven were repairable the day it was
written, by `pnpm.overrides` in the same JSON object four lines above, a mechanism that
object already used twice. Both `dotnet-architect` and `security-auditor` found this
independently; the shipped PR repairs six and accepts one.

**Beslut.**

1. **Repair outranks acceptance.** An advisory may enter `ignoreGhsas` only when no
   dependency-level fix exists. "Dependabot does not open a PR for it" is **not** such
   a demonstration, and neither is "it is transitive" — `pnpm.overrides` is the repo's
   ratified instrument for exactly that case.
2. **Every accepted entry names, in the workflow comment, three things:** why it cannot
   be repaired, what condition or actor would remove it, and why it is tolerable
   meanwhile (reachability, not merely severity). An entry that cannot carry all three
   does not qualify under (1).
3. **A lowered `--audit-level` is not an alternative.** It hides the same acceptance
   without naming what was accepted, and with zero criticals in the tree it deletes the
   gate while keeping its name.
4. **Accepting a vulnerability rather than repairing it is a `security-auditor` trigger.**
   Reducing exposure is not — the rule deliberately does not tax the direction that
   makes the tree safer, since taxing that is what produced the #1042 deadlock. The
   trigger is on the *action*, not on one instrument: growing `pnpm.auditConfig.ignoreGhsas`
   is the named case, but so are lowering `--audit-level` (which Beslut 3 forbids and
   which no trigger would otherwise catch) and the .NET analogue — suppressing
   `NuGetAudit` or `NoWarn`-ing NU1901–NU1904 in the backend half of this repo.
5. **The gate's semantics are otherwise unchanged**: still blocking, still fail-closed
   (an unreachable registry and a misspelled GHSA were both measured to exit non-zero),
   still per-advisory rather than per-package, so a *new* advisory in an already-accepted
   package is still caught.
6. **An override key is gated only to PARTITION.** A version selector earns its place
   only when it carries what the target cannot: *which line moves*, or *which line is
   spared*. Where a package has one line and one target, the selector merely restates
   the target's floor — one knowledge piece written as two numbers, and the two can
   drift. Measured against the base lockfile: `postcss` (8.5.15, 8.5.16), `sharp`,
   `tmp` (0.0.33, 0.1.0 — both to the same target), `fast-uri` and `undici` need no
   partition and are written open; `js-yaml@>=4.0.0 <4.3.0` spares the 3.x line, and
   the two `brace-expansion` keys carry two different targets, so those three stay
   gated.
   *Mechanism, because getting it backwards costs a deadlock:* the selector is matched
   against each consumer's **declared range** by intersection — not against the resolved
   version — and the **target** applies regardless of whether it falls outside the
   selector (pnpm's own documented example, `"bar@^2.1.0": "3.0.0"`, has exactly that
   shape). So the target is what moves resolution, and the target is what rots. The old
   `postcss@<8.5.10` did not stop matching; it matched, forced consumers to `^8.5.10`,
   and 8.5.15/8.5.16 satisfy `^8.5.10` while being vulnerable. **At the next advisory,
   raise the TARGET.** Raising the selector alone does nothing. Open form makes that
   error unrepresentable for the five open keys — there is no second number to raise.
   *The obligation open form creates, priced rather than assumed:* a bare key has no
   range, so it matches **every** consumer forever. Today that is a no-op — the resolved
   graph is byte-identical to the gated form — but when a consumer legitimately crosses
   a major, an open key pins it **back** into the pinned line, and that failure is
   **silent**, where the gated form's failure was loud (a stale pair lets a vulnerable
   version through and the gate goes red). The trade is deliberate: a measured, already
   realised loud failure for a hypothetical silent one. It carries a duty — raise the
   target across majors too, and re-measure "needs no partition" when a consumer crosses
   one, because that judgement is a snapshot of today's tree, not a permanent property.
   The *shape* already exists — an open key reaching past a consumer's declared major
   boundary, here forward rather than back: `next@16.2.9` declares `sharp: ^0.34.5`
   while the tree resolves 0.35.3. The silent pin-back itself has not occurred. Note also which entry forces hardest: `tmp`, where neither consumer's
   declared 0.x range admits 0.2.7 — it is listed above as needing no partition because
   both lines share one target, not because the forcing is small. Its blast radius is
   dev-only (`@lhci/cli`, `external-editor`).

**Konsekvenser / Negativt.** `auditConfig` lives in `package.json`, so it also filters
`audit (observe-only)` in `build.yml` — a control's exception reaching an instrument.
Accepted knowingly: the register of record is GitHub's Dependabot alerts, which
`auditConfig` cannot touch. Gate-local scoping via `pnpm audit --ignore` **is** available
for the surviving entry — it carries `CVE-2026-14257` — and was rejected rather than
found impossible. The reason is durability, not risk: `--ignore` accepted CVE ids before
pnpm v11 and accepts only GHSA ids from v11 ([pnpm CLI docs](https://pnpm.io/cli/audit)),
so a gate-local suppression would key on an identifier whose accepted format depends on
the pnpm major, and would oblige us to maintain a CVE↔GHSA mapping the advisory database
already owns. `auditConfig.ignoreGhsas` is GHSA-keyed on every version that reads it, and
keeps the acceptance in one place.
*Not a reason, and deliberately not claimed:* that the format change would be silent. It
would be fail-**closed** — on v11 a CVE id would match no advisory, brace-expansion would
count, and the gate would go red. That is the same property Beslut 5 records as a virtue
four paragraphs above, so it cannot be cited as a hazard here.

**The boundary, measured, and it is wider than this decision.** pnpm **11 does not read
the `pnpm` field in `package.json` at all**. Verbatim from 11.17.0 against this tree:
*"The `pnpm` field in package.json is no longer read by pnpm. The following keys were
ignored: `pnpm.overrides`, `pnpm.auditConfig` …"* (elided; the field also carries
`ignoredBuiltDependencies`) The summary line loses its
`(1 ignored)` — and, far more seriously, **`pnpm.overrides` is ignored too, so the six
repairs this amendment is built on do not apply either.** 9.15.9 and 10.28.2 both read
the field; 11 does not. Two consequences must be written down rather than discovered:

- **What holds the line is the CI pin, not the mechanism.** All five `pnpm/action-setup@v6`
  call sites pin `version: 9`, so CI is unexposed today. **Raising that pin to 11 or later is a
  MIGRATION, not a version bump** — it silently drops both the acceptance and the repairs.
  A developer running a locally-installed pnpm 11 already gets neither.
- **This inverts one half of the choice above.** `pnpm audit --ignore` *survives* the
  major (11 takes GHSA ids); it is the chosen instrument's **location** that does not. The
  decision stands on its other grounds — one place, no CVE↔GHSA mapping to maintain — but
  not on durability across majors, and it must not be cited that way.

The new home is `pnpm-workspace.yaml` ([pnpm settings](https://pnpm.io/settings),
[migration guide](https://pnpm.io/migration)). Note the migration is **three** keys, not
two: `ignoredBuiltDependencies` is **removed** in v11 rather than relocated, replaced by
an `allowBuilds` matcher — so it has no destination under the same name, and the
independent support it lends the `sharp` residual in `dependabot-automerge.yml` ends on
11 as well. Moving them is deliberately **not** done here: it is a change to how every pnpm invocation
in the repo resolves its settings, it must be verified on 9 as well as 11, and it does not
belong in the same diff as the repair. It is the migration's first step, and it is owed
before the pin moves.

**Known gap — WATCHED IN PART since 2026-07-30 by
`.github/scripts/audit-suppression-guard.sh`.** The rule below stands as the rule; the
"unpinned today" state it describes is the state *before* that guard. It watches **three**
of the four directions named here — a stale `ignoreGhsas` entry, an accepted advisory that
has entered the production set, and an `overrides` key absent from the lockfile. That third
one is the *measured instance* of its direction, not the whole of it: a selector that
intersects no consumer's declared range names a package that IS present, and is invisible
for the same pnpm-lock v9 reason as the fourth direction below. The guard runs in
observe-only `audit`, and has a **named consumer**: `security-auditor`, audit area 8
(CLAUDE.md §9.2). That consumer is load-bearing, not decoration — this repo's own
`dependabot-automerge.yml` header records that *no human reads observe-only audit at
auto-merge*, so a warning with no reader would have been the empty signal, not a fix.

**The fourth direction — Beslut 6's silent pin-back — remains OPEN, and is not
lockfile-detectable.** A guard for it was built and removed 2026-07-30, on measurement.
The signature is the opposite of what a floor comparison sees: an override forces
resolution *to* the floor, so the resolved version lands at or above it, never below
(`sharp` floor 0.35.0 resolves 0.35.3 — this ADR's own named instance, and the check never
fired on it). Detecting a real pin-back needs each consumer's **declared** range for the
overridden package, and pnpm-lock v9 does not carry that for transitive edges:
`next@16.2.11` records `sharp: 0.35.3(...)`, a resolved version. It would take reading
every consumer's published manifest — an installed tree or the network — which the guard's
file-only design excludes by construction.

**What that removed guard did detect is already caught, blockingly and upstream:** a
manifest declaring a repair the lockfile does not carry. Measured 2026-07-30 — raising one
override target without regenerating the lockfile makes `pnpm install --frozen-lockfile`
exit 1 with `ERR_PNPM_LOCKFILE_CONFIG_MISMATCH … the current "overrides" configuration
doesn't match the value found in the lockfile`, inside the **required** `frontend` job.
That knowledge has one owner, and it is not this guard.

**Reader-reachability, stated rather than assumed.** The guard runs on every PR, including
Dependabot's. But Dependabot PRs are auto-merged by `dependabot-automerge.yml` **without
invoking any agent**, and no `schedule:` consults the measurement — so on the auto-merged
patch/minor Dependabot PRs, which are the bulk of what drives the tree drift these checks
detect, there is no reader. Be precise about the boundary rather than rounding it to
"exactly": `dependabot-automerge.yml` marks major and unknown update types ineligible, and
a PR that fails the vuln gate falls back to manual review. On that remainder the guard
is surfaced but unowned — its `::warning::` reaches the Checks view, and `audit` is
`continue-on-error` and outside `ci`'s `needs`, so nothing obliges anyone to read it
and a finding moves no merge signal. (Say it that way rather than "nothing surfaces
it": in this document's own vocabulary a warning IS the surface, and "unwatched" was
defined as *no warning and no exit difference*.) The readerless set is therefore
**larger** than the auto-merged set, not identical to it.

A cadence is a follow-up PR, triaged by senior-cto-advisor 2026-07-30 as a follow-up and
explicitly **not** a TD (the phase rule is not met). **No owner is assigned.** An earlier
revision of this paragraph said the gap was named "with a named owner"; the only name in
it was the agent that performed the triage, which is not the same thing as someone who
will close it.

*The rule, unchanged:* **A suppression whose blast
radius is not pinned, and an override key whose liveness is not checked, are declared as
gaps rather than left silent.* Both were unpinned before the guard: a stale `ignoreGhsas` entry
produces no warning and no exit difference, a bare GHSA would silently cover a **new**
path if the package re-entered the production tree, and an `overrides` key that matches
no consumer is equally silent (measured: an invented key exits 0 with no output). The
guard owes **both** directions: a key that matches *nothing* is dead, and a key that
matches *more than intended* — the open-form obligation in Beslut 6 — silently pins a
consumer back into an older major. *(Written 2026-07-28. The second half was measured
undeliverable two days later — see above: it is not lockfile-detectable at all, so it is
not an outstanding obligation on this guard but an open gap with no file-only owner. The
first half ships.)* The
guard belongs in observe-only `audit` — never inside the merge control, which is the
principle that kept a hand-rolled delta-differ out of the gate in the first place. Own PR
(senior-cto-advisor 2026-07-28: follow-up, explicitly **not** a TD — the phase rule is
not met). **That PR is this one — the plan is executed, and this parenthesis records
the triage that authorised it, not outstanding work.** Do not read it against the
cadence parenthesis three paragraphs up, which carries a different date (2026-07-30), a
different subject, and is still open. The current instance of the gap is recorded at
the mechanism, in `dependabot-automerge.yml`.

**Two override keys deliberately reach wider than today's tree.**
`brace-expansion@>=2.0.0 <=5.0.7` spans the 2.x/3.x/4.x lines, which are empty here —
the only consumer is `minimatch@10.2.5` declaring `^5.0.5`. The width is intentional:
`GHSA-mh99-v99m-4gvg` has no patch below 5.0.8, so for a hypothetical 2.x consumer there
would be no repair at all, only acceptance. And `js-yaml@>=4.0.0 <4.3.0` leaves
`js-yaml@3.15.0` (via `@lhci/utils`) untouched — not merely for compatibility, but
because the advisory's own affected range is `>=4.0.0 <4.3.0`, so 3.x is outside it.
That distinction matters under Beslut 1: leaving a line alone for compatibility would be
an undeclared acceptance, which this amendment forbids; leaving it alone because it is
not affected is not.

**Berörda dokument (samma PR):** `.github/workflows/dependabot-automerge.yml`,
`web/jobbliggaren-web/package.json`, `web/jobbliggaren-web/pnpm-lock.yaml`.

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
