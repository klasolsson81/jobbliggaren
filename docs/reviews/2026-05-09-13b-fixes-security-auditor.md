# Security-audit: STEG 13b-fix-paket (Sec-Major-1 + Sec-Major-2 verifiering)

**Status:** Sec-Major-1 + 2 stängda — STEG 13b approved för commit
**Granskat:** 2026-05-09
**Auktoritet:** GDPR Art. 32, ADR 0026 (Accepted), CLAUDE.md §5.4

**Granskat scope:**
- `docs/decisions/0026-alb-http-only-fas0.md` (verbatim)
- `src/JobbPilot.Api/Program.cs:114-124`
- `infra/terraform/environments/dev/main.tf:175, 226-246`
- `infra/terraform/environments/dev/variables.tf:134-141`
- `docs/tech-debt.md` TD-29 + TD-30

---

## Sec-Major-1 status — STÄNGD

ADR 0026 är komplett mot Alt B-kraven specificerade i ursprungs-rapporten:

- **Tidsfönster konkret:** 30 dagar från 2026-05-09 → deadline **2026-06-08**. Datum-bundet, inte "tas bort senare".
- **5 triggers:** domän+ACM, multi-tenant (24h-fönster vid första icke-Klas-konto), tidsgräns, säkerhetsincident, Fas 2 — alla supersession-villkor täckta inkl. ursprungs-rekommendation "när någon utöver Klas får dev-credentials".
- **Mitigation-stack reell:** rate-limiting (TD-21), IP-anonymisering (ADR 0024 D7), audit-cascade Art. 17, CloudTrail, restriktiv egress (ADR 0025), ALB-DNS-ej-marknadsföring. Stacken existerar i kod redan — inte löfte om framtida arbete.
- **Supersession-procedur dokumenterad:** ny ADR + flippa `alb_https_enabled = true` + ACM-cert + uppdatera current-work/steg-tracker.
- **TD-30 lagd** som operativ task — kopplad till ADR-trigger 1, korrekt scope-separation.
- **Validerings-disciplin:** ADR §Validering kräver "kontrollera ADR-status varannan vecka via git log" + forced trigger 3 senast 2026-06-08. Anti-glidnings-mekanism explicit.

GDPR Art. 32-bedömningen står: dev-fas-context med solo-utvecklare + mitigation-stack är proportionerlig grund för accepted-risk. När context skiftar (multi-tenant) triggas supersession inom 24h.

## Sec-Major-2 status — STÄNGD

`Program.cs:114-122` env-gate:ar `UseHttpsRedirection()` korrekt:

```csharp
var albHttpsEnabled = builder.Configuration.GetValue<bool>("Alb:HttpsEnabled");
if (builder.Environment.IsDevelopment() || albHttpsEnabled)
    app.UseHttpsRedirection();
```

Logiken är konsistent på alla axlar:
- **Dev (dotnet run lokalt):** `IsDevelopment() = true` → redirect aktiv → Kestrel dev-cert hanterar HTTPS lokalt
- **Dev-deploy bakom HTTP-only-ALB:** `IsDevelopment() = false` (env=Production i container) + `Alb:HttpsEnabled = false` → redirect skippad → ALB-health-check får 200 OK på HTTP. Deploy-circuit-breaker triggar inte.
- **Vid ADR 0026-trigger:** flippa `var.alb_https_enabled = true` → `Alb__HttpsEnabled = "true"` env-var injiceras → `UseHttpsRedirection()` aktiv samtidigt som ALB HTTPS-listener kommer upp. Atomisk konsistens.

Single source of truth: en Terraform-variabel styr både ALB-listener-modul och app-redirect-middleware. Ingen drift möjlig.

## Övrigt observerat

**Sec-Minor-8 — acceptabel som TD-29.** TODO-kommentar på `Program.cs:124-128` är explicit om att `/api/ready` är liveness, inte readiness, och flaggar Fas 2-trigger för strict readiness via `AddDbContextCheck` + `AddRedis`. För Fas 0/MVP är ALB-target-group-felklassificering låg-konsekvens (solo-dev, ingen multi-task load-balancing att dölja DB-degradering bakom). Acceptabel deferral.

**Worker_secrets minskning — säkerhetsmässigt rimlig.** `dev/main.tf:231-237` har 2 secrets för Worker (Postgres + HangfireStorage) istället för 3 (ingen Redis). Verifierat mot Worker/Program.cs — laddar inte `AddIdentityAndSessions` → Redis-clienten injiceras inte → secret skulle vara död yta. Mindre secret-yta = mindre läckage-yta vid komprometterad task-role + mindre Decrypt-anrop till KMS. Konsistent med least-privilege.

**Sec-Nit-1 (Worker har både `Postgres` + `HangfireStorage`)** kvarstår men är dokumenterad i kod-kommentar `main.tf:231-233` — Klas verifierat att Worker behöver app-data (ghosted-detection). Acceptabelt.

## Slutlig bedömning

**Sec-Major-1 + 2 stängda — STEG 13b approved för commit.**

Inga nya säkerhetsfynd. Inga GDPR-blockers kvarstår. Mitigation-stacken bakom ADR 0026 är reell (rate-limiting + IP-anon + audit-cascade aktiv i kod). Tidsfönstret 2026-06-08 är datum-bundet och Klas har validerings-disciplin via ADR §Validering. När trigger uppfylls flippar **en** Terraform-variabel både ALB-listener och app-redirect-middleware atomiskt — ingen split-state-risk.
