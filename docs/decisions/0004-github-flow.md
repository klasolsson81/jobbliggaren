# ADR 0004 — GitHub Flow över GitFlow

**Datum:** 2026-04-18
**Status:** Accepted
**Kontext:** Session 2 (bootstrap-beslut), formaliserad session 4 STEG 9
**Beslutsfattare:** Klas Olsson
**Relaterad:** BUILD.md §6.1, ADR 0007 (Branch protection)

## Kontext

Branch-strategi påverkar dagligt arbete från dag ett. Två huvudmönster dominerar i .NET/JavaScript-ekosystemet:

- **GitFlow** (Vincent Driessen, 2010): långlivade `develop`- och `master`-branches, feature/release/hotfix-branches, komplex merge-topologi
- **GitHub Flow** (GitHub, 2011): en huvudbranch (`main`), kortlivade feature-branches, deploy från main, staging som *miljö* istället för *branch*

JobbPilot är solo-dev i Fas 0 med:
- En deploy-pipeline planerad (dev → staging → prod via taggar)
- Inga parallella release-trains
- Ingen hotfix-urgency-skillnad (alla fixes går via samma flöde)
- Fokus på snabbt iterativt arbete snarare än strukturerad release-kadens

## Beslut

JobbPilot använder **GitHub Flow**:

- `main` är enda långlivade branch
- Feature-branches: `feat/<scope>-<beskrivning>`, `fix/<beskrivning>`, `chore/<beskrivning>`
- Pull request → review → squash-merge till `main`
- Deploy sker via taggar på `main`:
  - `v*-dev` → dev-miljö
  - `v*-rc*` → staging
  - `v*` (utan `-rc`) → prod (manuell approval)

**Ingen `develop`-branch. Ingen `release/*`-branch. Inga `hotfix/*`-branches.**

## Konsekvenser

**Positivt:**

- Enklare mental modell — en branch att synka mot, aldrig tveka om var en fix ska landa
- Snabbare merge-kadens — inga release-branches som långsamt ackumulerar ändringar
- Staging är *miljö*, inte *branch* — reflekterar verkligheten (miljöer är infrastructure, inte git-state)
- Branch protection är enklare att konfigurera (bara `main`)
- GitHub-native — verktyget är byggt för detta mönster (CODEOWNERS, required reviews, deploy environments)

**Negativt:**

- Mindre disciplinerat än GitFlow om flera team jobbar parallellt (ej relevant i Fas 0-1)
- "Feature flags" krävs för att dölja ofärdigt arbete i `main` — vi använder det ändå enligt BUILD.md §11
- Release-historik blir mindre tydlig utan `release/*`-branches — kompenseras av taggar och release notes

## Alternativ övervägda

**Alt 1 — GitFlow (Vincent Driessen).** Avvisat. För komplext för solo-dev; `develop`-branch är overhead utan värde när det bara är en dev. Passar stora team med strukturerade release-trains (t.ex. kvartals-releases) — inte JobbPilots kontinuerliga-deploy-modell.

**Alt 2 — Trunk-Based Development (ren).** Övervägt. I ren TBD committar alla direkt till `main` med feature flags. JobbPilot väljer feature-branches + PR som mellanvariant eftersom det ger naturlig review-gate (auto-code-review via agenter) utan att betala för full GitFlow-overhead. Om teamet växer kan vi migrera mot ren TBD med CI-gates som primär review-mekanism.

**Alt 3 — GitLab Flow.** Likvärdigt med GitHub Flow + miljö-branches. Avvisat eftersom vi redan är på GitHub — extra miljö-branches utan motsvarande värdehöjning.

## Relation till andra beslut

- **ADR 0007 (Branch protection):** bygger på detta beslut. B-nivå-protection skyddar `main` mot force push och deletion.
- **BUILD.md §6.1** uppdaterades i session 3 STEG 1 för att reflektera valet (borttagande av `develop`-branch-referens, tillägg av tag-baserad deploy).
- **`.github/pull_request_template.md`** (STEG 8.1) följer GitHub Flow-konventionen: kortlivade branches, squash-merge.

## Implementationsstatus

**Aktiv sedan:** start av projektet. Session 3-4-arbetet har använt direkt-commit till `main` eftersom det är solo-dev-bootstrap. Feature-branches + PR introduceras när:

- Första riktiga feature-scaffold sker (Fas 0/1)
- CI-workflows finns (senare STEG) som kan gate:a PRs

Fram till dess är `main`-baserat flöde med manuell review i varje commit pragmatiskt.
