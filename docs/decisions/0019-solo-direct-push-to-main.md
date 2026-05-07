# ADR 0019 — Solo direct-push till main

**Datum:** 2026-05-07
**Status:** Accepted
**Kontext:** Session 4b.2 Turn 2 — efterspel av PR #2
**Beslutsfattare:** Klas Olsson
**Superseder:** ADR 0004 (GitHub Flow)
**Relaterad:** ADR 0007 (Branch protection — omvärderas separat)

## Kontext

ADR 0004 (GitHub Flow, 2026-04-18) etablerade feature-branches + PR + squash-merge mot `main`. Under Fas 0 bootstrap körde projektet pragmatisk direct-push enligt ADR 0004:s implementationsstatus-sektion ("fram till första riktiga feature-scaffold").

Den första riktiga PR-cykeln i projektets historia var PR #2 (Session 4b.2 Turn 2 — `feat(frontend): /mig + (app) layout, middleware rename, DTO sync`). Erfarenheten avslöjade tre problem som tillsammans visar att PR-overhead saknar motsvarande värdehöjning för projektets nuvarande form:

1. **Branch-state-tracking divergerade mellan webb-Claudes mental modell och faktiskt repo.** Föregående chatts handover beskrev "merge + cleanup" som genomfört, men lokal `main` hade inte fått squash-commiten och feature-branchen fanns kvar både lokalt och remote. Uppdagades först via mikro-discovery i nästa chatt — efter att vi nästan byggt vidare på fel utgångspunkt.

2. **PR-flödet ger ingen reell granskningsspärr för solo-dev.** GitHubs "approve" är teoretisk när det är samma person som godkänner. Den faktiska granskningen sker i chat-dialog mellan webb-Claude och Klas, innan CC ens börjar koda.

3. **Squash-merge skapar två commits för samma innehåll** — den lokala feature-commiten (`df9e40e`) och GitHub-side squash-commiten (`4175db4`). Detta är inneboende i PR-mekaniken, men tillsammans med (1) skapade det en reset/cleanup-skuld som inte hade existerat med direct-push.

Den faktiska granskningsspärren i projektet är:

- **Plan-design** (chat-granskning med webb-Claude): scope, sekvens, risker och alternativ utvärderas innan kod skrivs
- **STOPP-disciplin** (CC): explicit halt vid varje övergång — inga str_replace, inga commits, ingen analys mellan STOPP och GO
- **Agent-invocation-disciplin**: security-auditor, code-reviewer, dotnet-architect invokeras vid relevant scope och rapporterna granskas innan commit
- **Manuell diff-granskning** av Klas innan varje push
- **Pre-push hooks** (gitleaks, dotnet format, lint-staged)

Ingen av dessa kommer från PR-mekaniken. PR är overhead, inte spärr.

## Beslut

JobbPilot använder **direct-push till `main` som permanent praxis**:

- `main` är enda branch — inga feature-branches, inga PRs
- Conventional Commits-format består (per CLAUDE.md §6.2)
- Granskningsspärrar listade i Kontext-sektionen ovan består
- Pre-push hooks består
- Deploy via taggar består (per ADR 0004:s tag-strategi): `v*-dev`, `v*-rc*`, `v*`

**Inga PRs. Inga feature-branches.**

## Konsekvenser

**Positivt:**

- Enklare branch-state — `main` är alltid utgångspunkten, ingen risk för divergens mellan lokal och remote-state av samma feature
- Snabbare iteration — ingen merge-overhead per moment
- Mental modell matchar verkligheten — solo-dev med chat-granskning behöver inte simulera team-flöde
- Branch-name-kollisioner och stale-branches är omöjliga
- Mikro-discovery för repo-state behövs sällan — `git log -3` och `git status` är full statusbild

**Negativt:**

- Ingen GitHub-side review-record — chat-history (Klas + webb-Claude) är primär granskningstrail
- Branch protection som spärr förlorar mening (ADR 0007 omvärderas)
- Om bidragsgivare tillkommer behövs övergång tillbaka till PR-flöde — inte kostnadsfri men inte komplex
- Force-push-skydd och oavsiktlig historik-förstöring blir extra viktigt — gitleaks pre-push hook och Git-side reflog är de spärrar som finns

## Alternativ övervägda

**Alt 1 — Behåll ADR 0004 (GitHub Flow med feature-branches + PR).** Avvisat. PR-mekaniken bidrog till branch-state-felet i Turn 2 utan att tillföra granskningsvärde. Chat-granskning är redan starkare än self-review-PR.

**Alt 2 — Trunk-Based Development med strikta CI-gates.** Övervägt. Skiljer sig från detta beslut främst i CI-gating: ren TBD kräver att CI är gating innan push. JobbPilot har inte CI-pipeline ännu (kommer Fas 1+) och kan inte gata på det sättet. När CI finns kan TBD-mönstret formaliseras genom uppdatering av denna ADR.

**Alt 3 — Hybrid: direct-push för bootstrap + dokumentations-arbete, PR för feature-arbete.** Avvisat. Skapar gränsfall ("är detta en feature eller chore?") och regression-risk. Bättre med en regel.

## Trigger för återgång till PR-flöde

Detta beslut omvärderas vid något av följande:

1. **Bidragsgivare tillkommer** — andra utvecklare (även medstudent, även tillfälligt) behöver granskningsspärr som inte beror på chat-dialog mellan Klas och webb-Claude
2. **Lärar-krav** — om kursbedömningen explicit kräver PR-evidence och chat-history inte räcker
3. **Disciplin-regression** — om STOPP-disciplinen havererar 2 gånger i rad (definierat som: CC bypassar STOPP-punkt och pushar utan godkännande), återgå till PR som hård spärr

Vid trigger: ny ADR skapas som superseder denna. ADR 0004 kan inte återupplivas — ny ADR med uppdaterade premisser krävs.

## Relation till andra beslut

- **ADR 0004 (GitHub Flow):** Superseded av denna ADR.
- **ADR 0007 (Branch protection Fas 0):** Omvärderas i separat ADR. B-nivå-protection som blockerar direct-push från admin är inte längre önskat. Force-push-skydd och deletion-skydd behålls.
- **CLAUDE.md §6.1, §6.3, §9.1:** Uppdateras parallellt med denna ADR (samma chatt-cykel) för att reflektera ny praxis. Ny §9.4 om discovery-disciplin tillkommer.

## Implementationsstatus

**Aktiv från:** 2026-05-07 (denna ADR:s acceptans). PR #2 var första och sista PR i projektet — inga ytterligare feature-branches kommer skapas.
