# STEG 14a CI/CD — dotnet-architect review

**Granskningsdatum:** 2026-05-10
**Scope:** `.github/workflows/build.yml`, `.github/workflows/deploy-dev.yml`, `infra/terraform/modules/github_oidc/main.tf`
**Granskare:** dotnet-architect (read-only, arkitektur-lins)

## Sammanfattning

**APPROVE-WITH-FIXES** — 0 BLOCK, 2 viktiga fynd, 4 mindre fynd, 3 nits.

CI/CD-arkitekturen är välskriven, image-only-deploy-mönstret är korrekt val för IaC/CI-separation, och OIDC-trust-policy:n är solid. Två `--no-build`-relaterade problem som kommer bita er när source-generators körs och en CI-yta som påverkar Clean Architecture-feedback-loopen (architecture-tests separerade från unit-tests). Inget av det är blocker för terraform apply (Terraform-modulen är intakt).

## Fynd

### Viktiga

**[Viktigt 1]** `.github/workflows/build.yml:53,59` — `dotnet build --no-restore` följt av `dotnet test --no-build`

Mediator.SourceGenerator producerar handler-discovery vid build-tid. Microsoft.Testing.Platform-runners (xunit.v3.mtp-v2) bygger varje testprojekt som `OutputType=Exe`. `dotnet test --no-build` på sln-nivå kan tysta swallow-a build-fel i sourcegen-output om sln innehåller projekt med `TestingPlatformDotnetTestSupport=true` blandat med icke-test-projekt.

**Föreslagen åtgärd:** släpp `--no-build` i test-steget. Kostar ~10s extra men gör fel deterministiska.

**[Viktigt 2]** `.github/workflows/build.yml:59` — `--logger "trx"`

MTPlatform-runner är **inte VSTest** och har egen logger-CLI. `--logger trx` är VSTest-syntax, fungerar via VSTest-kompat-shim men fragile. Native flaggan är `--report-trx`.

**Föreslagen åtgärd:**
```yaml
- name: Test
  run: dotnet test JobbPilot.sln -c Release -- --report-trx --report-trx-filename TestResults.trx --results-directory TestResults
```

### Mindre

**[Mindre 1]** Architecture-tests blandas med unit/integration-tests

Architecture-tests verifierar Clean Arch-gränser. Bör köras separat (cheap, deterministiska) före tunga Testcontainers-tester för snabb fail-fast.

**Föreslagen åtgärd:** Splittra till två steps i samma backend-job — Architecture-tests först (50ms), sedan resten.

**[Mindre 2]** Race-condition mellan Terraform och CI-deploy

Pipeline:n hämtar task-def via `describe-task-definition`, mutar `image`-fältet, registrerar ny revision. Om Terraform `apply` körs under en pågående deploy och muterar t.ex. `cpu`/`memory`/`secrets` kan CI:n registrera ny revision baserad på *gammal* describe-output.

**Föreslagen åtgärd (Fas 0):** dokumentera "kör inte `terraform apply` på dev-stacken samtidigt som tag-deploy". Concurrency-block skyddar inte mot detta.

**[Mindre 3]** Worker-deploy-strategi för 14a

Worker deploy:as med `wait-for-service-stability: false` pga PLACEHOLDER-creds. Arkitektoniskt försvarbart — pipelinens jobb i 14a är att verifiera image-deploy-mekaniken.

**Föreslagen åtgärd:** lägg till explicit warn-output i deploy-summary:
```yaml
- name: Worker stability warning
  run: |
    echo "::warning::Worker-deploy verifierar inte runtime-stability i 14a (PLACEHOLDER-creds). 14b sätter creds + aktiverar wait-for-service-stability."
```

**[Mindre 4]** `infra/terraform/modules/github_oidc/main.tf:30` — `thumbprint_list = []`

Korrekt sedan AWS började acceptera GitHub OIDC via TLS-cert-bundle (juli 2023). I provider 4.x finns perpetual-diff-bug — om diff: lägg till `lifecycle { ignore_changes = [thumbprint_list] }`.

### Nits

**[Nit 1]** `build.yml:88-91` — Node-version hårdkodad till '22'
Lägg `.nvmrc` med `22.x` i `web/jobbpilot-web/` och byt till `node-version-file:`.

**[Nit 2]** `deploy-dev.yml:198-211` — Smoke-retry sleep:ar 10s efter sista försöket
Spara 10s wall-clock genom att flytta `sleep` ovanför sista-försök-checken.

**[Nit 3]** `github_oidc/main.tf:78-81` — Sub-claim listan har trailing comma
Inte tekniskt fel (HCL accepterar det), inkonsistent med resten av repo.

### Specifika frågor besvarade

1. **Build-strategi (sourcegen + MTPlatform):** Restore→build separat OK för Mediator.SourceGenerator. Problem ligger i `--no-build` på test-steget kombinerat med MTPlatform.
2. **Test-runner:** Använd `--report-trx` via `--`-separator istället för `--logger trx`.
3. **Integration-tests + Testcontainers på ubuntu-latest:** **OK utan ändringar.** Docker-on-host via socket-mount fungerar default.
4. **Architecture-tests separation:** Bör splittras för Clean Arch-feedback-loop.
5. **Image-only deploy + Terraform-race:** Acceptabel för Fas 0, behöver mitigation för Fas 1.
6. **Worker-deploy 14a:** Försvarbart, men dokumentera tydligare.
7. **Clean Arch-påverkan från CI:** Pipelinen påverkar inte lager-gränser. **OK.**

## Beslut

**APPROVE-WITH-FIXES.** Ingen blocker mot `terraform apply`. Workflows-fixarna kan göras antingen före första `v*-dev`-tag-push eller som follow-up commit. Klas-rekommendation: fixa Viktigt 1 + 2 i en `fix(ci): ...`-commit innan första taggen.

## Referenser

- CLAUDE.md §2.1 — Clean Arch lager-gränser
- CLAUDE.md §7 — Architecture tests som separat krav
- BUILD.md §15.3, §15.4 — CI/CD-arkitektur
- ADR 0019 — direct-push till main, granskningsspärrar
- ADR 0023 — Worker-host (no-HTTP, Hangfire)
- xunit.net/docs/v3-microsoft-testing-platform — MTPlatform-native CLI
