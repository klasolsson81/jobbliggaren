# Cost recovery scripts

PowerShell-scripts för manuell sekundärskydd vid AWS-kostnadsblowout.
Refererade från [docs/runbooks/aws-cost-recovery.md](../../../docs/runbooks/aws-cost-recovery.md).

## Scripts

| Script | Syfte | När |
|---|---|---|
| `stop-ecs-services.ps1` | Stoppar api + worker (desired_count=0) | Vid bekräftad blowout efter Steg 2 i runbook |
| `restore-ecs-services.ps1` | Detachar deny + skalar upp services | Efter grundorsaken är fixad (Steg R1) |

## Användning

Båda scripts tar `-Cluster`, `-Profile`, etc. som parametrar med dev-defaults.

```powershell
# Stoppa
.\stop-ecs-services.ps1

# Återställ
.\restore-ecs-services.ps1
```

Se `Get-Help <script> -Full` för komplett dokumentation.

## Säkerhet

- Inga credentials hårdkodade — använder `aws --profile`-parameter
- `$ErrorActionPreference = "Stop"` så partial-fail inte fortsätter
- Exit-codes verifieras efter varje `aws`-anrop
- Idempotent: `detach-role-policy` skippas om policyn ej attachad

## Test-procedur

Scripts är säkra att köra utan kostnadsimpact — de stoppar och startar
tjänsterna. Test 5 i runbooken beskriver komplett rundtripcykel.

```powershell
.\stop-ecs-services.ps1
# Vänta 60s, smoke-test 503
.\restore-ecs-services.ps1
# Vänta 3-5 min, smoke-test 200
```
