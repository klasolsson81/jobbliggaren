# dotnet-architect — STEG 13c HSTS-implementation review

**Datum:** 2026-05-10
**Agent:** dotnet-architect (delegering från security-auditor Sec-Major-2)
**Scope:** HSTS-implementation i Api/Program.cs + HstsOptions.cs + appsettings.Production.json + HstsOptionsTests.cs
**Approval-status:** APPROVE-with-fixes

> **Anmärkning:** dotnet-architect-agenten är read-only-konfigurerad (kan inte skriva filer). Rapporten levererades inline; persistas hit av parent-agent.

## Sammanfattning

Pipeline-ordningen, gate-logiken, config-symmetrin och test-täckningen är korrekta enligt ASP.NET Core 10-docs och projektets ForwardedHeadersConfig-pattern. Två viktiga fynd kring `EnsureSafeForEnvironment`-paritet och pipeline-ordningens beroende av `UseForwardedHeaders`, plus tre nice-to-have kring observability och integration-test.

## Fynd per bedömningspunkt

### 1. Pipeline-ordning — `UseHsts()` FÖRE `UseHttpsRedirection()` — KORREKT

Verifierat mot Microsoft Learn (ASP.NET Core 10): rekommenderad ordning är `UseExceptionHandler → UseHsts → UseHttpsRedirection → ...`. HSTS-headern måste sättas på alla HTTPS-svar inklusive 307-redirect-svaret från HttpsRedirection.

**Viktig nyans:** `UseForwardedHeaders` ligger redan före `UseHsts` (rad 112 vs 137), vilket är korrekt enligt MS-docs ("Forwarded Headers Middleware must be run before calling UseHsts when working with proxy servers"). Bakom ALB är `Request.IsHttps` annars false → HSTS-header sätts aldrig.

**Föreslagen åtgärd:** Lägg till en rad i kommentaren ovanför `if (!builder.Environment.IsDevelopment() && albOptions.HttpsEnabled)`:
```csharp
// Förutsätter att UseForwardedHeaders körts redan (rad 112) — annars är
// Request.IsHttps false bakom ALB och HSTS-headern sätts aldrig.
```

**Status:** ✅ Adresserat — kommentar tillagd i Program.cs.

### 2. Gate-logik — `!IsDevelopment() && HttpsEnabled` — KORREKT

Symmetri med UseHttpsRedirection-gate. Localhost skyddas dubbelt: (a) `IsDevelopment()` exkluderar dev-env, (b) HSTS-headern sätts ändå bara på HTTPS-svar (ASP.NET-default) så ren HTTP-localhost får aldrig headern. Browser-lock-risken som dokumenteras i HstsOptions.cs är reell — gaten hanterar den korrekt.

### 3. Config-binding — `Get<HstsOptions>() ?? new HstsOptions()` — KORREKT

Symmetriskt med `AlbOptions` och `ForwardedHeadersConfig`. Sealed class + init-only props + `public const SectionName` följer projektets etablerade pattern.

### 4. AddHsts()-placering — efter AddJobbPilotRateLimiting() — APPROVE

Placering är OK eftersom HSTS är en API-pipeline-bekymmer (transport-security-header), inte en Application/Infrastructure-bekymmer. Hör hemma i Api-projektet. Att lägga den i `AddInfrastructure` skulle bryta Clean Arch.

**[Nice-to-have]** Extrahera till `HstsConfigurationExtensions.AddJobbPilotHsts(this IServiceCollection, IConfiguration)` för symmetri med `AddJobbPilotRateLimiting()`. Defererat — STEG 13c håller smal scope.

### 5. EnsureSafeForEnvironment()-style production-defense — VIKTIGT FYND

**Vad:** HstsOptions saknade production-defense motsvarande `ForwardedHeadersConfig.EnsureSafeForEnvironment`.

**Varför:** Om någon deployar med `Hsts:MaxAgeDays=0` eller `Hsts:Preload=true` UTAN att hstspreload.org-submission gjorts (eller med MaxAgeDays<31536000 vilket är preload-spec-minimum) i Production → tyst säkerhetsregression. Pattern finns redan i ForwardedHeadersConfig (Sec-Major-1 från STEG 12) och är JobbPilots etablerade fail-loud-disciplin (CLAUDE.md §9.1).

**Status:** ✅ Adresserat — `EnsureSafeForEnvironment(string env)` implementerat i `HstsOptions.cs` med tre invarianter:
- `MaxAgeDays >= 365` utanför Development/Test
- `Preload=true` kräver `MaxAgeDays >= 365` OCH `IncludeSubDomains=true`
- Tom environment-name → ArgumentException

Anropas i Program.cs gate:at på `albConfig.HttpsEnabled` så HTTP-only Fas 0 (ADR 0026) inte triggar throw.

### 6. Test-täckning — VIKTIGT FYND

**Vad:** Saknas pipeline-gating-test (motsvarande TD-31:s `UseHttpsRedirectionGateTests`).

**Varför:** Sec-Major-2 anti-regression. Risken är inte hypotetisk: gate-villkoret är två booleska och flippas av Terraform-driven env-var. En framtida refactoring kunde råka invertera villkoret.

**Status:** ⏳ Lyfts som **TD-33** i ADR 0027 — `WebApplicationFactory<Program>`-baserade tester:
- (a) Production+HttpsEnabled=true → response innehåller `Strict-Transport-Security`-header
- (b) Production+HttpsEnabled=false → header saknas
- (c) Development → header saknas oavsett HttpsEnabled

Sex existerande config-tester räcker för att låsa default-värden + binding-pattern + EnsureSafeForEnvironment-invarianter. Bristen är i pipeline-täckningen.

### 7. Observability — logga HSTS-config vid startup? — NICE-TO-HAVE

**Vad:** Inget startup-log för HSTS-config (eller AlbOptions/ForwardedHeadersConfig).

**Varför:** Post-deploy-verifiering av env-var-injicering är idag manuell. INFO-log vid startup skulle göra Hsts-state synlig i CloudWatch.

**Status:** Defererat — separat TD om generaliserbar `LogStartupConfig`-extension som täcker AlbOptions + ForwardedHeadersConfig + HstsOptions. Inte STEG 13c-scope.

## Tester som tagits bort (per fynd)

`MaxAgeDays_Zero_DisablesHstsEffectively` testade `TimeSpan.FromDays(0) == TimeSpan.Zero` — testar .NET BCL, inte JobbPilot-invariant. Borttaget.

`AddHsts_MapsToAspNetCoreHstsOptions` + `AddHsts_PreloadFlag_PropagatesToFrameworkOptions` — krävde DI-extension `AddHsts` som inte är referenced i test-projektet, och testade i grunden ASP.NET Cores config-binding (BCL-test). Borttaget.

Ersatta med 6 EnsureSafeForEnvironment-tester (totalt 11 testmetoder, 17 test-cases inkl. Theory-expansion).

## Sammanfattande blockers

Inga blockers. Sec-Major-2 är arkitekturellt rätt löst. Två viktiga fynd (5 + 6) adresserade: fynd 5 i denna commit, fynd 6 lyfts som TD-33.

## Filer som granskats

- `src/JobbPilot.Api/Configuration/HstsOptions.cs`
- `src/JobbPilot.Api/Configuration/AlbOptions.cs`
- `src/JobbPilot.Api/Configuration/ForwardedHeadersConfig.cs`
- `src/JobbPilot.Api/Program.cs`
- `src/JobbPilot.Api/appsettings.Production.json`
- `src/JobbPilot.Api/appsettings.json`
- `tests/JobbPilot.Api.IntegrationTests/Configuration/HstsOptionsTests.cs`
- `tests/JobbPilot.Api.IntegrationTests/Configuration/ForwardedHeadersConfigTests.cs`
- `docs/tech-debt.md` (TD-31-spec)

## Källor (ASP.NET Core 10 docs verifierade 2026-05-10)

- [ASP.NET Core Middleware (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0)
- [Enforce HTTPS in ASP.NET Core (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0)
- [HstsBuilderExtensions.UseHsts (aspnetcore-10.0)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.hstsbuilderextensions.usehsts?view=aspnetcore-10.0)
- [Configure ASP.NET Core to work with proxy servers and load balancers (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
