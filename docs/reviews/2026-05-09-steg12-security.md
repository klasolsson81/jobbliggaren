# Security-audit: STEG 12 (Fas 0/Alt A1) — pre-launch-gates

**Status:** Approved with Major-finding (Sec-Major-1 fixad in-block)
**Granskat:** 2026-05-09
**Auktoritet:** GDPR Art. 5/6/32, CLAUDE.md §5.4, ADR 0024 D7, TD-17, TD-21, security-auditor STEG 11 Sec-Major-1

**Granskat scope (working tree, ej pushat):**
- `src/JobbPilot.Worker/Hosting/HangfireConnectionStringResolver.cs` (ny)
- `src/JobbPilot.Worker/Program.cs` (modifierad rad 62-66)
- `src/JobbPilot.Api/Configuration/ForwardedHeadersConfig.cs` (ny)
- `src/JobbPilot.Api/Program.cs` (modifierad rad 1-4 + 86-110)
- `src/JobbPilot.Api/appsettings.Production.json` (ny)
- `src/JobbPilot.Worker/appsettings.Production.json` (ny)
- Tester i Api.IntegrationTests + Worker.IntegrationTests (totalt 22 nya)

Tre fail-loud-mönster utförda korrekt. Inga GDPR-blockers, inga secrets i overlays, ingen PII i loggning, ingen IDOR-yta. Två Major (en fixad in-block, en docs-only), tre Minor, två Nit. Flera Praise.

---

## Critical

Inga.

---

## Major

### Sec-Major-1 — KnownNetworks-tom-array saknar production-defense vid uppstart

**Filer:** `src/JobbPilot.Api/Configuration/ForwardedHeadersConfig.cs:28`, `src/JobbPilot.Api/Program.cs:99-109`, `src/JobbPilot.Api/appsettings.Production.json:17`

Default `KnownNetworks = []` är medvetet och bibehåller ASP.NET-default (loopback only). I prod bakom ALB betyder det att `X-Forwarded-For` ignoreras → `Connection.RemoteIpAddress` blir ALB:s VPC-internal-IP → IP-baserad rate-limiting hamnar i en enda bucket → effektivt no-op = trivialt brute-force-fönster mot `/auth/login` och `/auth/register`.

Pre-launch-gate finns dokumenterad i `docs/runbooks/aws-setup.md §3.3` (manuell verifiering). Men koden i sig hade ingen `IsProduction()`-guard som motsvarar Worker-mönstret i `Program.cs:74-82` (`safeForAutoSchema` allow-list). Symmetri-luckan är reell: om STEG 13/14 deployar utan att overlay populerats med VPC-CIDR — fail-loud triggar inte och rate-limit-no-op blir tyst.

**Konsekvens:** OWASP A07 (Identification & Auth Failures) — credential-stuffing-window öppet i prod-launch-fönstret.

**Fix in-block (STEG 12):** lade till production-defense i `Api/Program.cs` motsvarande Worker-mönstret:

```csharp
var safeForEmptyKnownNetworks =
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
if (!safeForEmptyKnownNetworks && forwardedCfg.KnownNetworks.Length == 0)
{
    throw new InvalidOperationException(
        $"ForwardedHeaders:KnownNetworks måste sättas till ALB:s VPC-CIDR utanför " +
        $"Development/Test (aktuell miljö: {builder.Environment.EnvironmentName}). " +
        "Tom array bakom proxy gör IP-baserad rate-limiting till no-op. " +
        "Se docs/runbooks/aws-setup.md §3.3.");
}
```

**Status:** ✓ Fixad. ECS startar inte container vid tom KnownNetworks utanför Development/Test.

### Sec-Major-2 — Overlay-kommentar för ForwardLimit=2 är vilseledande om CloudFront

**Fil:** `src/JobbPilot.Api/appsettings.Production.json:19`

Prod-overlay sätter `ForwardLimit = 2` med kommentar "ALB → CloudFront (om används)". Men:
1. ASP.NET ForwardedHeaders-middleware iterar X-Forwarded-For-kedjan baklänges från ALB. För ForwardLimit=2 måste BÅDA hops vara i KnownNetworks/KnownProxies — annars stoppar middleware vid första untrusted-hop och behåller den IP:n som RemoteIpAddress.
2. CloudFront edge-IP-rangerna är inte stabila och lever i AWS-managed prefix-list (`com.amazonaws.global.cloudfront.origin-facing`), inte i VPC-CIDR. KnownNetworks med bara VPC-CIDR ger ForwardLimit=1-effekt även om talet är 2.

**Fix:** uppdatera kommentaren och `aws-setup.md §3.3` med explicit anteckning om CloudFront-prefix-list. ALB-only-deploy bör sätta `ForwardLimit = 1`.

**Status:** ✓ Fixad in-block (overlay + runbook).

---

## Minor

### Sec-Minor-1 — `ForwardedHeaders` flag-set hårdkodad

**Fil:** `src/JobbPilot.Api/Program.cs:101`

`ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto` är hårdkodat. ASP.NET-options stödjer också `XForwardedHost`. Idag inte använd, men gör options:en config-driven om det blir aktuellt. Inte säkerhetsrelevant idag.

**Status:** Defererad. Lyfts om CloudFront-Host-routing blir aktuell.

### Sec-Minor-2 — Worker-allow-list `IsEnvironment("Test")` är död kod

**Fil:** `src/JobbPilot.Worker/Program.cs:75`

`safeForAutoSchema` accepterar både Development och `IsEnvironment("Test")`. Ingen test-fixture sätter `EnvironmentName="Test"` — dead branch. Inte säkerhetsrelevant — bara konsistens.

**Status:** Defererad. Pliktoffer-cleanup vid nästa Worker-test-skrivning.

### Sec-Minor-3 — Felmeddelanden vid CIDR/IP-parse läcker config-värdet

**Fil:** `src/JobbPilot.Api/Configuration/ForwardedHeadersConfig.cs:55-57, 76-77`

`ex.Message` innehåller raw-värdet (`'not-a-cidr'`). CIDR/IP är publik infra-info — ingen secret-leak. Defensiv pattern: trunkera värdet eller använd index-only. Inte blockerande, men ändra mönstret innan det kopieras till en secret-bärande config-loop.

**Status:** Defererad. Acceptabel idag.

---

## Nit

### Sec-Nit-1 — appsettings.Production.json JSON-comments

ASP.NET stödjer det (`JsonReaderOptions.CommentHandling = Skip`). Verifierat OK. Prettier i `lint-staged` strippar kommentarer per default — verifiera `.jsonc`-undantag eller använd `.jsonc`-extension.

**Status:** Defererad. Klas verifierar prettier-config.

### Sec-Nit-2 — HangfireConnectionStringResolver felmeddelande kan peka explicit på worker-roll

Liten förbättring: `"...sätts via Secrets Manager (jobbpilot_worker-roll, GRANT-modell §4); i dev räcker..."`. Inte säkerhetsrelevant.

**Status:** Defererad.

---

## Praise

- `HangfireConnectionStringResolver` lyft till statisk testbar metod — TD-17 punkt 4 implementerad korrekt.
- `Resolve` prefereras `HangfireStorage` över `Postgres` — exakt rätt routning så prod alltid landar i `jobbpilot_worker`-rollen.
- `ForwardedHeadersConfig.ParseKnown*` använder `System.Net.IPNetwork.TryParse` — modernt API, bättre IPv6-stöd.
- 17 enhetstester på `ForwardedHeadersConfig` med Theory-täckning — strukturell anti-regression mot TD-21:s "tyst no-op"-failure-mode.
- Prod-overlays committar EXPLICIT INTE secrets — header-kommentarer dokumenterar att ConnectionStrings injiceras via env-vars + Secrets Manager.
- `ForwardLimit`-range-validering 1-10 — fail-loud-grain rätt.
- Pipeline-ordning oförändrad — `app.UseForwardedHeaders` placerad FÖRE `UseAuthentication/UseAuthorization/UseRateLimiter`. Korrekt.
- Inga PII i nya kod-paths.
- Worker overlay sätter explicit `PrepareSchemaIfNecessary: false` + production-defense throwar om någon flippar tillbaka till true.
- `ArgumentNullException.ThrowIfNull(configuration)` i resolver — pliktdefensiv guard.

---

## Sammanfattning

3 block granskade, 2 Major (Sec-Major-1 + Sec-Major-2) — båda fixade in-block. 3 Minor + 2 Nit defererade. Inga GDPR-blockers, inga secrets, ingen PII-läckage, ingen auth-bypass, ingen IDOR, ingen logging-hygiene-överträdelse.

**Block-status:** Approved för commit till `main` per ADR 0019. Sec-Major-1-fix gör att STEG 14 prod-deploy nu blockeras i kod (inte bara runbook) om VPC-CIDR saknas i overlay.
