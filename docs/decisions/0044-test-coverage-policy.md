# ADR 0044 — Test-coverage-policy: reproducerbar in-repo-mätning, first-party-filter och regressions-gate

**Datum:** 2026-05-17
**Status:** Proposed
**Kontext:** Test-coverage-sidospår före FAS 3 (Application Management). Klas vill ha en reproducerbar in-repo-coverage-mekanism på MasterClass-nivå som senare kan ligga till grund för en README-coverage-sektion.
**Beslutsfattare:** Klas Olsson (Accepted-flip + gate-aktivering); senior-cto-advisor (tröskel-modell + Hangfire-test-approach); dotnet-architect (infra-design); Claude Code (implementation)
**Relaterad:** ADR 0009 (inga repositories — testbarhet via IAppDbContext), ADR 0010 (Worker separat composition root), ADR 0032 (JobTech-jobben — Hangfire-orchestrators), CLAUDE.md §2.4/§7/§8

---

## Kontext

Före detta beslut fanns **ingen** in-repo-coverage-mekanism. `dotnet test --collect:"XPlat Code Coverage"` stöds inte under xUnit v3 / Microsoft.Testing.Platform (`global.json` `"runner": "Microsoft.Testing.Platform"`), och inget coverage-paket refererades. Coverage kunde därför inte mätas reproducerbart, inte spåras över tid, och inte gate:as i CI.

Krafter som spelar in:

- **Reproducerbarhet > ad-hoc.** Mätningen ska vara ett versionerat repo-mål (script + tool-manifest), inte en maskin-lokal global-tool-installation.
- **Ärlig siffra > sifferkosmetik.** Genererad kod (OpenApi, Mediator-source-gen), entrypoints (`Program.cs`, `JobbPilot.Migrate`) och migrationer blåser upp nämnaren. Den rapporterade siffran måste spegla *verklig testbar kvalitet*.
- **Audit-trail.** Rå coverage-data får inte filtreras destruktivt — granskningsbarhet kräver att filtreringen är reversibel och deklarativ.
- **Goodharts lag (Fowler, "TestCoverage").** En coverage-*måltavla* inbjuder till assertion-fria täckningstester. Gaten ska skydda mot *regression*, inte vara ett ideal som rutinmässigt kringgås.
- **Asymmetrisk testbarhet per lager (CLAUDE.md §2.4).** Domain bär invarianter (hög branch-täckning rimlig); Api/Worker-komposition bär inga (låg täckning korrekt, ej slapp).

## Beslut

### 1. Mätmekanism (dotnet-architect-design)

- **Collection:** `Microsoft.Testing.Extensions.CodeCoverage` (Microsoft, MTP-native), central `PackageReference` i `tests/Directory.Build.props`, version pinnad i `Directory.Packages.props` (CPM). Körs som `dotnet test --solution JobbPilot.sln -c Release -- --coverage --coverage-output-format cobertura`.
- **Rapport:** `dotnet-reportgenerator-globaltool` via in-repo local tool manifest (`.config/dotnet-tools.json`, version pinnad) — **inte** MSBuild-NuGet (separerar rapportering från build-grafen; BUILD.md §3.1 dependency-disciplin: minst invasiva nya dependency-form).
- **Körväg:** `scripts/coverage.ps1` (Windows-primär) + `scripts/coverage.sh` (ubuntu-CI-paritet). Output → gitignorad `artifacts/coverage/` (Html + Cobertura + JsonSummary + TextSummary + MarkdownSummaryGithub).

### 2. First-party-filter (rå data ofiltrerad — filtrering report-time)

Rå cobertura per testprojekt lämnas **oförändrad** i `TestResults/` (audit-trail, Fowler 2018 granskningstrail-anda). All exclusion sker report-time i ReportGenerator:

- **assemblyfilters:** `+JobbPilot.Domain;+JobbPilot.Application;+JobbPilot.Infrastructure;+JobbPilot.Api;+JobbPilot.Worker;-JobbPilot.Migrate;-*.UnitTests;-*.IntegrationTests;-*.Architecture.Tests`
- **classfilters:** `-JobbPilot.Api.Migrations.*;-*.Migrations.*;-Mediator.*;-*.OpenApi.Generated.*`
- **filefilters:** `-**/Migrations/*.cs;-**/obj/**;-**/*.g.cs;-**/*.Generated.cs;-**/Program.cs`

`JobbPilot.Migrate` exkluderas på assembly-nivå (DDL-init-entrypoint, ingen affärslogik). `Program.cs` filtreras (komposition, otestbar utan host). ReportGenerator honorerar dessutom `[ExcludeFromCodeCoverage]` — attributet appliceras *minimalt* och endast på genuint omätbar ekvivalens-kod (strongly-typed `*Id`-record-structs, SmartEnum-härledd equality, Result-factory-one-liners) som opportunistisk samma-fas-refinement; det är **inte** ett krav för den ärliga siffran (filtren räcker), och sprids inte brett denna touch (CLAUDE.md §9.6 — jaga ej brus).

### 3. Uppmätt first-party-baseline (HEAD `b3772a3`, denna mekanism)

| Lager | Line | Branch |
|---|---|---|
| JobbPilot.Domain | 95.3% | 93.3% |
| JobbPilot.Application | 96.2% | 88.8% |
| JobbPilot.Infrastructure | 84.0% | 71.1% |
| JobbPilot.Api (efter filter) | 93.7% | 82.9% |
| JobbPilot.Worker | 30.7% | (få grenar) |
| JobbPilot.Migrate | exkluderad | — |
| **Totalt first-party** | **91.5%** | **83.7%** |

Detta är den auktoritativa baslinjen framåt (mätt av denna reproducerbara mekanism). Den skiljer sig medvetet från tidigare ad-hoc-siffror (86.6/72.7) eftersom filtret är striktare och ärligare. B-luckorna (DeleteAccount, ListInvitations, Hangfire-grenar) höjer Application ytterligare när de stängs i samma session.

### 4. Regressions-gate (senior-cto-advisor — icke-regression-ratchet, ej måltavla)

- **Modell:** per-lager-golv, **inte** global tröskel (global döljer asymmetri). Golv = `baseline − 2pp` line (absorptionsmarginal mot icke-deterministisk branch-mätning → undviker gate-trötthet). Ratchet: golvet höjs *manuellt* (Klas-GO) när faktisk coverage stabilt ligger högre — det är non-regression, inte target (Fowler/Goodhart).
- **Branch-gate:** endast Domain + Application (lagren som bär invarianter/affärslogik). Domain branch-golv ~90%, Application branch-golv ~75% (CTO-satta; slutgiltiga värden pinnas vid Accepted-flip mot då-aktuell uppmätt baseline efter B1/B2/B3).
- **Infrastructure:** medel line-golv, ingen branch-gate. **Api:** låg/medel line-golv efter filter. **Worker:** ingen numerisk gate Fas 1 (observe-only — jobblogiken testas i Application-lagret per Beslut 5; Worker-assembly är tunn bootstrap). **Migrate:** exkluderad helt.
- **Mekanism:** CI-jobbet `coverage` (`build.yml`) parsar `Summary.json` (`jq`, förinstallerat — ingen ny supply-chain-yta) och fail:ar steget under golv. Designat men **PROPOSED — ej enforce:at**: `continue-on-error: true` + `exit 0` i gate-steget tills Accepted-flip. Det snabba `backend`-jobbet rörs ej (fast fail-feedback bevaras); coverage kör parallellt och ligger ej i `ci.needs` förrän aktivering.

### 5. Hangfire-jobb-test-approach (korsref — ej eget ADR)

Jobblogiken (`SyncPlatsbankenSnapshotJob`, `AuditLogRetentionJob`, `PurgeStaleRawPayloadsJob`) testas **in-place i Application-lagret** via mockade abstraktioner (Approach (a), senior-cto-advisor 2026-05-17), inte via extraherad service (Gemini-förslag (b) avvisat — YAGNI/SoC; jobben är redan tunna ADR 0032-strukturerade orchestrators med injicerade portar, jfr ADR 0032 §5). Detta är ett test-implementations-val, inte ett arkitekturbeslut → ingen egen ADR (CLAUDE.md §8 punkt 9), endast denna korsref + session-log.

## Konsekvenser

**Positiva:** reproducerbar mätning lokalt + CI; ärlig first-party-siffra; rå audit-trail bevarad; regressionsskydd utan Goodhart-incitament; per-lager-rättvisa; grund för framtida README-coverage-sektion.

**Negativa + mitigering:** (a) 2pp-marginal döljer 1–2pp genuin regression → accepterat: en gate man litar på > en exakt gate som ger falsklarm och ignoreras. (b) Worker utan gate Fas 1 → jobblogiken täcks via Application-tester (Beslut 5). (c) Ny tooling-dependency → minimerad: local tool manifest + central PackageReference, inga MSBuild-NuGet, ingen tredjeparts-action (jq/awk inbyggt). (d) Filter kan dölja genuint otestad icke-genererad kod → mitigeras av att rå cobertura bevaras ofiltrerad för djupgranskning.

## Alternativ övervägda

1. **Ingen mekanism (status quo) — avvisat:** omöjliggör spårning/gate; MasterClass-kravet ouppfyllt.
2. **Coverlet + `--collect` — avvisat:** stöds ej under MTP-runnern (global.json).
3. **ReportGenerator som MSBuild-NuGet — avvisat:** läcker in i build-grafen + transitiv yta under TreatWarningsAsErrors; kopplar rapport till build-cykel (fel SoC). Local tool manifest valt.
4. **Collection-time-filter (.runsettings exclude) — avvisat:** förstör rådatan destruktivt → ingen audit-trail. Report-time-filter valt.
5. **Global enkel tröskel — avvisat:** döljer per-lager-asymmetri; blir antingen meningslöst låg eller orättvist blockerande (Domain subventionerar Worker).
6. **Ambitiös absolut målnivå (t.ex. 95% överallt) — avvisat:** Goodhart/Fowler — inbjuder assertion-fri täckning; gate ska skydda mot regression, inte vara ideal.
7. **[ExcludeFromCodeCoverage] brett sprayat — avvisat denna touch:** filtren ger ärlig siffra utan källkods-spray; bred applicering är scope-expansion mot §9.6 (jaga ej brus). Minimal opportunistisk applicering tillåten.

## Implementationsstatus

**Klart (denna session, CC kör direkt — entydigt mot principer per §9.6):**
- Mätmekanism: paket + tool-manifest + scripts + .gitignore — verifierad fungerande (1139/1139, first-party Line 91.5% / Branch 83.7%).
- First-party-filter (assembly/class/file) — verifierad.
- CI-jobb `coverage` PROPOSED (continue-on-error, gate `exit 0`).
- Denna ADR i status **Proposed**.

**Väntar Klas-GO (Klas-STOPP-flagga — strategisk transition, §9.2):**
- ADR 0044 `Proposed → Accepted`-flip.
- Gate-aktivering: ta bort `continue-on-error` + `exit 0`, pinna slutgiltiga per-lager-golv mot då-aktuell baseline (efter B1/B2/B3), lägg `coverage` i `ci.needs`. ADR Accepted + gate-aktivering kopplas ihop (annars beskriver en Accepted ADR en gate som inte gäller — CLAUDE.md §1.5).

**Framtida (incident-trigger, ej TD, ej nu):** "cacha-ej-tom-laddning"-defensiv hardening i `TaxonomyReadModel` (övervägt i PRIO-1 CI-fix-triagen, senior-cto-advisor 2026-05-17) — noteras här som framtida revision vid observerat genuint-tomt-snapshot-incident.

---

*ADR-index underhålls av docs-keeper. Detta beslut formaliserar test-coverage-sidospårets infrastruktur- och policy-leverans.*
